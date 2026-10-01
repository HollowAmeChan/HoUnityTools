#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>
    /// VRChat 约束的编辑器桥。
    ///
    /// 本包不引用 VRC SDK 程序集（装了 SDK 的工程和没装的工程都要能编译），
    /// 所以这里全部走反射，且反射只碰 VRChat 官方文档
    /// （Constraints API）承诺的公开表面：
    ///
    ///   · 组件类型   VRC.SDK3.Dynamics.Constraint.Components.VRC{Rotation,Parent}Constraint
    ///   · 成员       IsActive / GlobalWeight / Locked / TargetTransform / Sources
    ///                （3.10.5 上前四个是 public 字段，所以字段优先、属性兜底）
    ///   · 方法       ApplyConfigurationChanges() / ActivateConstraint()
    ///
    /// 被驱动对象由 TargetTransform 决定：指向谁就驱动谁，留空则驱动约束自己挂着的
    /// 那个 Transform。本工具把约束挂在根物体底下的独立空物体上（纯挂点），
    /// 再用 TargetTransform 指回被驱动的骨骼 —— 于是"约束集中在一处"与
    /// "骨骼被驱动"两件事同时成立。见
    /// https://vrc-beta-docs.netlify.app/common-components/constraints/constraints-api/
    /// </summary>
    internal static class HoVrcConstraintBridge
    {
        internal const string RotationConstraintTypeName =
            "VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint";

        internal const string ParentConstraintTypeName =
            "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint";

        /// <summary>读不到 SDK 常量时的兜底值（3.10.5 的值是 16），只用于预分配与探测上限。</summary>
        private const int FallbackMaxFlatSourceLength = 16;

        /// <summary>探测 sourceN 槽位的上限，防止 SDK 布局异常时死循环。</summary>
        private const int ProbeSlotLimit = 256;

        private static readonly Dictionary<string, Type> ResolvedTypes =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        private static readonly Dictionary<string, double> NextRetryTime =
            new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>解析失败后的重试间隔：脚本重载/装完 SDK 后不用重启编辑器。</summary>
        private const double RetryIntervalSeconds = 5.0;

        /// <summary>SDK 是否装了（本进程能解析出 VRC 约束类型）。</summary>
        internal static bool IsSdkAvailable
        {
            get
            {
                return ResolveType(ParentConstraintTypeName) != null &&
                    ResolveType(RotationConstraintTypeName) != null;
            }
        }

        /// <summary>按全名解析 VRC 类型；解析失败返回 null 并短暂退避后重试。</summary>
        internal static Type ResolveType(string fullTypeName)
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

            Type resolved = FindType(fullTypeName);
            if (resolved == null)
            {
                NextRetryTime[fullTypeName] = EditorApplication.timeSinceStartup + RetryIntervalSeconds;
                return null;
            }

            NextRetryTime.Remove(fullTypeName);
            ResolvedTypes[fullTypeName] = resolved;
            return resolved;
        }

        private static Type FindType(string fullTypeName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type candidate;
                try
                {
                    candidate = assemblies[index].GetType(fullTypeName, false);
                }
                catch (Exception)
                {
                    continue;
                }
                if (candidate != null && typeof(Component).IsAssignableFrom(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>把计划里的约束种类映射到 VRC 组件类型；不支持的种类返回 null。</summary>
        internal static Type ResolveConstraintType(bool parent)
        {
            return ResolveType(parent ? ParentConstraintTypeName : RotationConstraintTypeName);
        }

        /// <summary>
        /// 给 host（纯挂点）挂上指定 VRC 约束组件，驱动 drivenTransform，
        /// 来源为 sources。成功返回该组件，失败返回 null 并把原因写进 error。
        /// </summary>
        internal static Component AddConstraint(
            GameObject host,
            Type constraintType,
            Transform drivenTransform,
            IList<Transform> sources,
            float weight,
            out string error)
        {
            error = null;
            if (host == null || constraintType == null)
            {
                error = "目标物体或约束类型为空。";
                return null;
            }
            if (drivenTransform == null)
            {
                error = "没有可驱动的 Transform。";
                return null;
            }

            Component constraint = Undo.AddComponent(host, constraintType);
            if (constraint == null)
            {
                error = "无法添加 " + constraintType.Name + "。";
                return null;
            }

            // 先定被驱动对象：TargetTransform 指向被驱动的骨骼，
            // 约束自己挂着的空物体只当挂点，不参与求值。
            if (!SetProperty(constraint, "TargetTransform", drivenTransform))
            {
                error = constraintType.Name + " 缺少 TargetTransform 字段。";
                return null;
            }

            // 配置期间先解锁，配置完再按 SDK 的约定锁上。
            if (!SetProperty(constraint, "Locked", false) ||
                !SetProperty(constraint, "GlobalWeight", Mathf.Clamp01(weight)))
            {
                error = constraintType.Name + " 缺少 Locked / GlobalWeight 之一。";
                return null;
            }

            string sourceError;
            if (!TryWriteSources(constraint, sources, out sourceError))
            {
                error = sourceError;
                return null;
            }

            // 改完属性必须调这个，否则改动可能同步不回约束（SDK 的硬性要求）。
            // 同时它会重新读取被驱动对象并重算绑定，所以要在设完 TargetTransform 之后调。
            ApplyConfigurationChanges(constraint);

            // 关键一步：烘 offset 并锁定。
            //
            // 必须用零参数的 ActivateConstraint()，不能直接调 TryBakeCurrentOffsets()：
            // 后者的签名是 TryBakeCurrentOffsets(BakeOptions bakeOptions = BakeOptions.BakeAll)，
            // 带一个可选参数 —— 可选参数不是重载，用 Type.EmptyTypes 反射是找不到它的。
            // 而 offset 只在编辑器里烘一次（播放模式被当成恒锁），漏了这一步骨骼就会直接
            // 对到来源骨骼上（Parent/MCH 连位置一起吸附），与另外两种形态的行为不一致。
            //
            // ActivateConstraint() 本体就是：Locked=false; IsActive=true;
            // TryBakeCurrentOffsets(); Locked=true —— 与 Inspector 的 Activate 按钮等价。
            if (!InvokeNoArg(constraint, "ActivateConstraint"))
            {
                error = constraintType.Name + " 缺少 ActivateConstraint()，无法烘 offset。";
                return null;
            }

            // 上面的 TargetTransform / IsActive / GlobalWeight / Locked 都是反射直写字段，
            // 不走 SerializedObject；host 又在预制件实例层级里，所以必须显式记录覆盖，
            // 否则重开场景可能被预制件源值盖回去。
            RecordPrefabOverrides(constraint);
            return constraint;
        }

        /// <summary>
        /// 把组件上的改动登记成预制件实例覆盖。非预制件实例场景下是空操作。
        /// </summary>
        internal static void RecordPrefabOverrides(Component component)
        {
            if (component == null)
                return;
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "HoFBX: 记录预制件覆盖失败（" + component.GetType().Name + "）：" +
                    exception.Message);
            }
        }

        /// <summary>
        /// 脚本改完约束属性后必须调用，否则改动可能不会同步回约束
        /// （VRChat Constraints API 的硬性要求）。
        /// </summary>
        internal static void ApplyConfigurationChanges(Component constraint)
        {
            if (constraint != null)
                InvokeNoArg(constraint, "ApplyConfigurationChanges");
        }

        /// <summary>
        /// 调用一个零参数的公开方法。返回 false 表示方法不存在或调用失败 ——
        /// 一律记 warning，不静默：上次就是"方法名对但签名对不上"被无声吞掉，
        /// 结果 offset 从来没烘过。
        /// </summary>
        private static bool InvokeNoArg(Component constraint, string methodName)
        {
            System.Reflection.MethodInfo method = null;
            try
            {
                // 只按名字+可见性找，不传参数表：传 Type.EmptyTypes 只匹配"零参数"，
                // 带可选参数的方法会被漏掉。
                method = constraint.GetType().GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "HoFBX: 查找 " + constraint.GetType().Name + "." + methodName +
                    " 失败：" + exception.Message);
                return false;
            }

            if (method == null)
            {
                Debug.LogWarning(
                    "HoFBX: " + constraint.GetType().Name + " 上找不到方法 " + methodName +
                    "()，相关功能已跳过。");
                return false;
            }

            if (method.GetParameters().Length != 0)
            {
                Debug.LogWarning(
                    "HoFBX: " + constraint.GetType().Name + "." + methodName +
                    " 需要参数，本处只能调用零参数方法，已跳过。");
                return false;
            }

            try
            {
                method.Invoke(constraint, null);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "HoFBX: 调用 " + constraint.GetType().Name + "." + methodName +
                    "() 失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// 直接写序列化的 Sources 列表。
        ///
        /// 用 SerializedObject 而不是去构造 VRCConstraintSource 结构体：
        /// 那个结构体是值类型，反射拿到的装箱副本改完不会写回组件，
        /// 而序列化路径写的就是组件自己的字段，跟 Inspector 完全同一条路。
        ///
        /// 摊平字段名与摊平长度都从 SDK 现读，不写死 16 —— 那是
        /// VRCConstraintSourceKeyableList.MaxFlatLength，将来可能变。
        /// </summary>
        private static bool TryWriteSources(
            Component constraint,
            IList<Transform> sources,
            out string error)
        {
            error = null;
            int count = sources == null ? 0 : sources.Count;
            if (count == 0)
            {
                error = "约束没有来源骨骼。";
                return false;
            }

            var serialized = new SerializedObject(constraint);
            SerializedProperty list = serialized.FindProperty("Sources");
            if (list == null)
            {
                error = constraint.GetType().Name + " 找不到序列化字段 Sources。";
                return false;
            }

            List<SerializedProperty> flatSlots = CollectFlatSourceSlots(list);
            if (flatSlots.Count == 0)
            {
                error = "Sources 里找不到摊平的 sourceN 字段。";
                return false;
            }

            // 实际可用槽位数以"连续找到的 sourceN"为准，SDK 常量只用来交叉校验。
            // （SDK 常量读不到时不能拿兜底值当硬闸门，否则一旦布局变化会把每条约束都判失败。）
            int declared = ReadMaxFlatSourceLength();
            if (declared > 0 && flatSlots.Count != declared)
            {
                Debug.LogWarning(
                    "HoFBX: Sources 摊平槽位数 " + flatSlots.Count + " 与 SDK 声明的 " + declared +
                    " 不一致，已按实际序列化槽位写入。");
            }

            int flat = Mathf.Min(count, flatSlots.Count);
            for (int index = 0; index < flat; index++)
                WriteSource(flatSlots[index], sources[index]);

            SerializedProperty total = list.FindPropertyRelative("totalLength");
            if (total == null)
            {
                error = "Sources 缺少 totalLength 字段。";
                return false;
            }
            total.intValue = count;

            SerializedProperty overflow = list.FindPropertyRelative("overflowList");
            if (count > flatSlots.Count)
            {
                if (overflow == null)
                {
                    error = "Sources 超过 " + flatSlots.Count + " 项但缺少 overflowList 字段。";
                    return false;
                }
                overflow.arraySize = count - flatSlots.Count;
                for (int index = flatSlots.Count; index < count; index++)
                {
                    SerializedProperty source = overflow.GetArrayElementAtIndex(index - flatSlots.Count);
                    if (source == null)
                    {
                        error = "overflowList 元素 " + (index - flatSlots.Count) + " 不可用。";
                        return false;
                    }
                    WriteSource(source, sources[index]);
                }
            }
            else if (overflow != null)
            {
                overflow.arraySize = 0;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// 摊平的 sourceN 槽位，按 N 升序，遇到第一个不存在的名字就停。
        ///
        /// 用序列化本身确定槽位数，而不是先信 SDK 常量再夹取：
        /// 常量读不到、或将来 SDK 改了布局，都不能让"写来源"这件事直接失败。
        /// 探到 0 之后还继续只是白跑，所以上界用一个足够大的探测上限即可。
        /// </summary>
        private static List<SerializedProperty> CollectFlatSourceSlots(SerializedProperty list)
        {
            var slots = new List<SerializedProperty>(FallbackMaxFlatSourceLength);
            for (int index = 0; index < ProbeSlotLimit; index++)
            {
                SerializedProperty slot = list.FindPropertyRelative("source" + index);
                if (slot == null)
                    break;
                slots.Add(slot);
            }
            return slots;
        }

        /// <summary>
        /// 读 VRCConstraintSourceKeyableList.MaxFlatLength 用于交叉校验。
        /// 该类型在 VRC.Dynamics 里，和约束组件类型不在同一个程序集
        /// （组件在 VRC.SDK3.Dynamics.Constraint），所以要跨程序集按名字找。
        /// 读不到返回 0，调用方只在 &gt; 0 时才拿它比对。
        /// </summary>
        private static int ReadMaxFlatSourceLength()
        {
            try
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type keyableList = null;
                    try
                    {
                        keyableList = assembly.GetType(
                            "VRC.Dynamics.VRCConstraintSourceKeyableList",
                            false);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (keyableList == null)
                        continue;

                    var field = keyableList.GetField(
                        "MaxFlatLength",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (field == null)
                        continue;

                    object value = field.GetRawConstantValue();
                    if (value is int length && length > 0)
                        return length;
                }
            }
            catch (Exception)
            {
                // 读不到就不做交叉校验。
            }
            return 0;
        }

        private static void WriteSource(SerializedProperty source, Transform transform)
        {
            SerializedProperty sourceTransform = source.FindPropertyRelative("SourceTransform");
            if (sourceTransform != null)
                sourceTransform.objectReferenceValue = transform;

            SerializedProperty weight = source.FindPropertyRelative("Weight");
            if (weight != null)
                weight.floatValue = 1.0f;
        }

        /// <summary>
        /// 按名字写公开成员，字段优先、属性兜底。
        /// 注意 null 的判定要绕开 IsInstanceOfType —— 那对 null 恒为 false，
        /// 会让"给引用字段写 null"静默失败。
        /// </summary>
        private static bool SetProperty(Component component, string name, object value)
        {
            Type type = component.GetType();
            var field = type.GetField(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null && CanHold(field.FieldType, value))
            {
                field.SetValue(component, value);
                return true;
            }

            var property = type.GetProperty(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanWrite && CanHold(property.PropertyType, value))
            {
                property.SetValue(component, value, null);
                return true;
            }

            return false;
        }

        private static bool CanHold(Type memberType, object value)
        {
            if (value == null)
                return !memberType.IsValueType;
            return memberType.IsInstanceOfType(value);
        }

        /// <summary>
        /// 该物体上属于 VRChat 的约束组件（不包含 Unity 内置约束）。
        /// </summary>
        internal static List<Component> GetConstraintComponents(GameObject host)
        {
            var result = new List<Component>();
            if (host == null)
                return result;

            Type parentType = ResolveType(ParentConstraintTypeName);
            Type rotationType = ResolveType(RotationConstraintTypeName);
            if (parentType == null && rotationType == null)
                return result;

            Component[] components = host.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                    continue;
                Type type = component.GetType();
                if ((parentType != null && parentType.IsAssignableFrom(type)) ||
                    (rotationType != null && rotationType.IsAssignableFrom(type)))
                {
                    result.Add(component);
                }
            }
            return result;
        }

        /// <summary>目标骨架层级里全部的 VRChat 约束组件。</summary>
        internal static List<Component> GetAllConstraintComponents(Transform root)
        {
            var result = new List<Component>();
            if (root == null)
                return result;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                result.AddRange(GetConstraintComponents(transform.gameObject));
            return result;
        }
    }
}
#endif
