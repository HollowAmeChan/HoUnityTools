#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PhysBoneTuner
{
    /// <summary>
    /// VRCPhysBone 的编辑器桥。
    ///
    /// 本包不引用 VRC SDK 程序集（装了 SDK 的工程和没装的工程都要能编译），
    /// 所以全部走反射。反射只碰 VRChat 文档里公开的字段：
    ///
    ///   pull / spring / stiffness   [Range(0, 1)]，默认 0.2
    ///   gravity                     [Range(-1, 1)]，默认 0 —— 允许为负 = 往上飘
    ///   radius                      无 [Range]，默认 0
    ///
    /// 这些范围是从 SDK DLL 的 RangeAttribute 上读出来的，不是猜的；
    /// 面板滑杆直接用它们，免得给用户一个 SDK 根本不认的区间。
    ///
    /// 另一个已知事实：PhysBone **不在编辑模式跑模拟**
    /// （PhysBoneManager 只在播放模式创建），所以编辑模式拖滑杆看不到物理变化；
    /// 而 pull/spring/stiffness 是每帧求解时现算的（CalcPull(t) = curve × pull），
    /// 所以播放模式下改值下一帧就生效，不需要任何 refresh。
    /// </summary>
    internal static class HoPhysBoneAccess
    {
        /// <summary>PhysBone 组件类型全名。VRC.Dynamics 里还有 VRCPhysBoneBase 等基类。</summary>
        internal const string PhysBoneTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";

        /// <summary>
        /// 也接受用户按接口/基类拼出来的类型名，只要能赋给其中一个就算命中。
        /// </summary>
        private static readonly string[] AdditionalTypeNames =
        {
            "VRC.Dynamics.VRCPhysBoneBase",
        };

        // ---- 参数名与 SDK 真值范围 ----

        internal const string PullField = "pull";
        internal const string SpringField = "spring";
        internal const string StiffnessField = "stiffness";
        internal const string GravityField = "gravity";
        internal const string RadiusField = "radius";

        internal const float PullMin = 0f, PullMax = 1f;
        internal const float SpringMin = 0f, SpringMax = 1f;
        internal const float StiffnessMin = 0f, StiffnessMax = 1f;
        internal const float GravityMin = -1f, GravityMax = 1f;

        private static readonly Dictionary<string, Type> ResolvedTypes =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double> NextRetryTime =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private const double RetryIntervalSeconds = 5.0;

        /// <summary>工程里是否装了 VRChat SDK 的 PhysBone。</summary>
        internal static bool IsAvailable
        {
            get { return ResolvePhysBoneType() != null; }
        }

        /// <summary>解析 VRCPhysBone 组件类型；解析不到返回 null 并短暂退避后重试。</summary>
        internal static Type ResolvePhysBoneType()
        {
            Type exact = ResolveTypeByName(PhysBoneTypeName);
            if (exact != null)
                return exact;

            for (int index = 0; index < AdditionalTypeNames.Length; index++)
            {
                Type fallback = ResolveTypeByName(AdditionalTypeNames[index]);
                if (fallback != null)
                    return fallback;
            }
            return null;
        }

        private static Type ResolveTypeByName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;
            if (ResolvedTypes.TryGetValue(fullTypeName, out Type cached))
                return cached;
            if (NextRetryTime.TryGetValue(fullTypeName, out double nextRetry) &&
                EditorApplication.timeSinceStartup < nextRetry)
            {
                return null;
            }

            Type resolved = null;
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                try
                {
                    Type candidate = assemblies[index].GetType(fullTypeName, false);
                    if (candidate != null && typeof(Component).IsAssignableFrom(candidate))
                    {
                        resolved = candidate;
                        break;
                    }
                }
                catch (Exception)
                {
                    // 个别程序集反射会抛，跳过即可。
                }
            }

            if (resolved == null)
            {
                NextRetryTime[fullTypeName] = EditorApplication.timeSinceStartup + RetryIntervalSeconds;
                return null;
            }

            NextRetryTime.Remove(fullTypeName);
            ResolvedTypes[fullTypeName] = resolved;
            return resolved;
        }

        /// <summary>
        /// 收集 transform 自身及其子层里的全部 PhysBone。
        /// 只看选中的那几束，不扫整个角色 —— 面板的目标范围就是这么定的。
        /// </summary>
        internal static List<Component> CollectPhysBones(IList<Transform> roots)
        {
            var result = new List<Component>();
            var seen = new HashSet<int>();
            if (roots == null)
                return result;

            Type physBoneType = ResolvePhysBoneType();
            if (physBoneType == null)
                return result;

            for (int index = 0; index < roots.Count; index++)
            {
                Transform root = roots[index];
                if (root == null)
                    continue;

                Component[] found = root.GetComponentsInChildren(physBoneType, true);
                for (int foundIndex = 0; foundIndex < found.Length; foundIndex++)
                {
                    Component component = found[foundIndex];
                    if (component != null && seen.Add(component.GetInstanceID()))
                        result.Add(component);
                }
            }

            // 稳定排序：按层级路径，免得每次重扫顺序乱跳。
            result.Sort((left, right) =>
                string.CompareOrdinal(GetPath(left), GetPath(right)));
            return result;
        }

        internal static string GetPath(Component component)
        {
            if (component == null)
                return string.Empty;
            var names = new List<string>();
            Transform current = component.transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        // ---- 读写单个参数 ----

        /// <summary>读参数；读不到返回 false 并把 value 置 0。</summary>
        internal static bool TryReadFloat(Component physBone, string fieldName, out float value)
        {
            value = 0f;
            if (physBone == null)
                return false;
            var field = FindField(physBone.GetType(), fieldName);
            if (field == null || field.FieldType != typeof(float))
                return false;
            try
            {
                value = (float)field.GetValue(physBone);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>写参数；写不进去返回 false（不抛）。</summary>
        internal static bool TryWriteFloat(Component physBone, string fieldName, float value)
        {
            if (physBone == null)
                return false;
            var field = FindField(physBone.GetType(), fieldName);
            if (field == null || field.FieldType != typeof(float))
                return false;
            try
            {
                field.SetValue(physBone, value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static System.Reflection.FieldInfo FindField(Type type, string name)
        {
            // 参数定义在基类 VRCPhysBoneBase 上，所以要逐基类向上找。
            for (Type current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        // ---- 自适应推导用的几何量 ----

        /// <summary>
        /// 相邻骨长（沿链到第一个子物体的距离），用来推半径。
        /// 取不到返回 0。
        /// </summary>
        internal static float MeasureBoneSize(Component physBone)
        {
            if (physBone == null)
                return 0f;

            Transform root = physBone.transform;
            float total = 0f;
            int samples = 0;
            Transform current = root;
            // 走到链尾或采够样本为止；PhysBone 的链就是"单子物体一路往下"。
            while (current != null && samples < 32)
            {
                if (current.childCount != 1)
                    break;
                Transform child = current.GetChild(0);
                total += Vector3.Distance(current.position, child.position);
                samples++;
                current = child;
            }
            return samples == 0 ? 0f : total / samples;
        }

        /// <summary>
        /// "这条链有多朝下"：0 = 水平，1 = 笔直向下。用世界空间的链首尾方向算。
        /// 重力自适应用它 —— 越朝下的链吃越多重力。
        /// </summary>
        internal static float MeasureVerticality(Component physBone)
        {
            if (physBone == null)
                return 0f;

            Transform first = physBone.transform;
            Transform last = first;
            int guard = 0;
            while (last != null && last.childCount == 1 && guard < 256)
            {
                last = last.GetChild(0);
                guard++;
            }

            Vector3 direction = last.position - first.position;
            if (direction.sqrMagnitude < 1e-8f)
                return 0f;
            return Mathf.Clamp01(-direction.normalized.y);
        }
    }
}
#endif
