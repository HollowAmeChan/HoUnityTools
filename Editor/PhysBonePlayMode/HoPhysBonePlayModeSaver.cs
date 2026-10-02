#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// ─────────────────────────────────────────────────────────────────────────────
// 播放模式里调 PhysBone，退出播放模式时把**数值**写回同一个组件。
//
// 为什么要有它：
//   PhysBone 的 pull / spring / stiffness / gravity / radius 这些只有播放模式才有物理表现，
//   在编辑模式拖滑杆看不到任何变化 —— 所以调参必须在 Play 里做。但 Play 里改的值退出后会被
//   还原，于是就有了「怎么把它带回来」的问题。
//
//   现成的 jp.lilxyzw.editortoolbox 有个 PlayModeSaver（组件菜单 Save changes in PlayMode），
//   但它在 prefab 实例上会把引用写成空：
//     · 它用 EditorJsonUtility 把**整个组件**序列化成 JSON 来回倒，而引用是以 fileID 存的，
//       fileID 只在所属序列化上下文里有效；回写时又是在 Instantiate 出来的临时克隆上
//       FromJsonOverwrite，prefab 里的那些骨骼引用在那个上下文里解析不回来。
//     · 它的 CopyProperties 逐条复制**所有**序列化属性，只排除了 6 个 prefab / 内部字段，
//       所以 rootTransform / ignoreTransforms / colliders / 各种 filter 全在复制之列。
//
// 本工具的做法（差别就一句话：**只抄数值，引用一律不碰**）：
//   1. 记录：EditorJsonUtility 把组件快照成 JSON，连同「场景层级路径 + 组件类型 + 同类序号」
//      存进 SessionState（这样即使播放模式进出触发了域重载也不会丢）。
//   2. 回写：退出播放模式后按路径找回同一个组件，把 JSON 灌进一个临时克隆，再**只把数值字段**
//      抄回组件 —— 走 SerializedObject + ApplyModifiedProperties()，所以在 prefab 实例上会
//      正确落成 prefab override，组件与 prefab 的关联也不会断。
//
//   PhysBone 调参只会改数值（含 AnimationCurve 与 LayerMask），Root Transform、Colliders、
//   Ignore Transforms、各种 filter 这些引用从来不参与调参，跳过它们不会有任何损失。
// ─────────────────────────────────────────────────────────────────────────────

namespace Hollow.HoUnityTools.Editor.PhysBonePlayMode
{
    /// <summary>
    /// 播放模式的 PhysBone 数值改动，退出时写回原组件。
    /// </summary>
    internal static class HoPhysBonePlayModeSaver
    {
        private const string SessionKey = "Hollow.HoUnityTools.PhysBonePlayMode";

        /// <summary>支持的组件类型名。本包不引用 VRC SDK 程序集，所以按名字认。</summary>
        private static readonly HashSet<string> SupportedTypeNames = new HashSet<string>
        {
            "VRCPhysBone",
            "VRCPhysBoneCollider",
        };

        /// <summary>
        /// 无论如何都不抄的字段。前六个就是 prefab 关联本身 —— 碰它们等于把组件从 prefab 上摘下来。
        /// </summary>
        private static readonly HashSet<string> AlwaysSkip = new HashSet<string>
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_EditorHideFlags",
            "m_Script",
            "m_Name",
        };

        // ── 持久化 ────────────────────────────────────────────────────────────
        //
        // 用 SessionState 而不是静态字段：播放模式进出时如果发生域重载，静态字段会没，
        // 而这个工具最怕的就是「以为存了其实没存」。SessionState 活到编辑器会话结束。

        [Serializable]
        private sealed class Entry
        {
            /// <summary>场景层级路径（含场景根物体名）。播放模式下场景是只读的，所以它不会变。</summary>
            public string path;

            /// <summary>组件类型名，如 VRCPhysBone。</summary>
            public string typeName;

            /// <summary>同一个物体上同类组件的序号（一个物体挂两个 PhysBone 时靠它区分）。</summary>
            public int index;

            /// <summary>EditorJsonUtility 打出来的组件快照。</summary>
            public string json;
        }

        [Serializable]
        private sealed class Payload
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static Payload Load()
        {
            var raw = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return new Payload();

            try
            {
                return JsonUtility.FromJson<Payload>(raw) ?? new Payload();
            }
            catch
            {
                return new Payload();
            }
        }

        private static void Store(Payload payload)
        {
            if (payload.entries.Count == 0) SessionState.EraseString(SessionKey);
            else SessionState.SetString(SessionKey, JsonUtility.ToJson(payload));
        }

        // ── 入口 ──────────────────────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static bool CanRecord => EditorApplication.isPlaying || EditorApplication.isPaused;

        [MenuItem("CONTEXT/VRCPhysBone/HoUnityTools/保存播放模式改动", false, 1000)]
        private static void RecordFromPhysBone(MenuCommand command) => Record(command.context as Component);

        [MenuItem("CONTEXT/VRCPhysBone/HoUnityTools/保存播放模式改动", true)]
        private static bool RecordFromPhysBoneValidate() => CanRecord;

        [MenuItem("CONTEXT/VRCPhysBoneCollider/HoUnityTools/保存播放模式改动", false, 1000)]
        private static void RecordFromCollider(MenuCommand command) => Record(command.context as Component);

        [MenuItem("CONTEXT/VRCPhysBoneCollider/HoUnityTools/保存播放模式改动", true)]
        private static bool RecordFromColliderValidate() => CanRecord;

        // 兜底入口：万一 CONTEXT/ 那两条因为类型名解析不出而没出现，用这个。
        [MenuItem("GameObject/HoUnityTools/保存播放模式改动（选中）", false, 30)]
        private static void RecordFromSelection()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                foreach (var component in go.GetComponents<Component>())
                {
                    if (IsSupported(component)) Record(component);
                }
            }
        }

        [MenuItem("GameObject/HoUnityTools/保存播放模式改动（选中）", true)]
        private static bool RecordFromSelectionValidate() => CanRecord && HasSupportedInSelection();

        private static bool HasSupportedInSelection()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                foreach (var component in go.GetComponents<Component>())
                {
                    if (IsSupported(component)) return true;
                }
            }
            return false;
        }

        private static bool IsSupported(Component component)
            => component != null && SupportedTypeNames.Contains(component.GetType().Name);

        // ── 记录 ──────────────────────────────────────────────────────────────

        private static void Record(Component component)
        {
            if (!IsSupported(component)) return;

            var entry = new Entry
            {
                path = ScenePath(component.transform),
                typeName = component.GetType().Name,
                index = IndexAmongSameType(component),
                json = EditorJsonUtility.ToJson(component),
            };

            var payload = Load();
            payload.entries.RemoveAll(e =>
                e.path == entry.path && e.typeName == entry.typeName && e.index == entry.index);
            payload.entries.Add(entry);
            Store(payload);

            Debug.Log($"[HoUnityTools] 已记录 {entry.typeName} @ {entry.path} 的播放模式改动" +
                      $"（共 {payload.entries.Count} 条待回写）。退出播放模式时只写数值字段，引用一律不碰。");
        }

        // ── 回写 ──────────────────────────────────────────────────────────────

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            var payload = Load();
            if (payload.entries.Count == 0) return;
            SessionState.EraseString(SessionKey); // 先清，免得写回过程中出异常后反复重试

            var written = 0;
            foreach (var entry in payload.entries)
            {
                var target = Resolve(entry);
                if (target == null)
                {
                    Debug.LogWarning($"[HoUnityTools] 找不到回写目标，已跳过：{entry.typeName} @ {entry.path}。");
                    continue;
                }

                var fields = ApplyValues(entry.json, target);
                written++;
                Debug.Log($"[HoUnityTools] 已把播放模式的数值写回 {entry.typeName} @ {entry.path}（{fields} 个字段）。");
            }

            Debug.Log($"[HoUnityTools] 播放模式改动回写完成：{written} / {payload.entries.Count}。");
        }

        /// <summary>按记录时的场景路径找回组件。播放模式下场景只读，所以路径不会变。</summary>
        private static Component Resolve(Entry entry)
        {
            if (string.IsNullOrEmpty(entry.path)) return null;

            var slash = entry.path.IndexOf('/');
            var rootName = slash < 0 ? entry.path : entry.path.Substring(0, slash);
            var rest = slash < 0 ? string.Empty : entry.path.Substring(slash + 1);

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || root.name != rootName) continue;

                    var target = root.transform;
                    if (rest.Length > 0)
                    {
                        target = target.Find(rest);
                        if (target == null) continue;
                    }

                    return NthOfType(target.gameObject, entry.typeName, entry.index);
                }
            }

            return null;
        }

        private static int ApplyValues(string json, Component target)
        {
            // 拿一个临时克隆接 JSON：引用可能解析不回来，但下面根本不抄引用，所以无所谓。
            var probe = Object.Instantiate(target);
            probe.hideFlags = HideFlags.HideAndDontSave;
            if (probe.gameObject != null) probe.gameObject.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, probe);
                return CopyValueFields(probe, target);
            }
            finally
            {
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>
        /// 把所有**数值**字段从 source 抄到 target，返回抄了多少个。
        /// 走 SerializedObject，所以在 prefab 实例上会正确落成 prefab override。
        /// </summary>
        private static int CopyValueFields(Object source, Object target)
        {
            var copied = 0;

            using (var soTarget = new SerializedObject(target))
            using (var soSource = new SerializedObject(source))
            using (var iterator = soSource.GetIterator())
            {
                // Next(true) 一路往下钻：数组元素、结构体成员都在里面，
                // 每一个单独过 IsCopyableValue，引用就在这一步被挡掉。
                var enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;
                    if (!IsCopyableValue(iterator)) continue;

                    soTarget.CopyFromSerializedProperty(iterator);
                    copied++;
                }

                soTarget.ApplyModifiedProperties();
            }

            return copied;
        }

        private static bool IsCopyableValue(SerializedProperty property)
        {
            if (AlwaysSkip.Contains(property.propertyPath)) return false;

            // 数组：元素是 PPtr 的整条不碰（那正是 ignoreTransforms / colliders），
            // 值数组（float[] 之类）整条抄。
            if (property.isArray)
                return !property.arrayElementType.StartsWith("PPtr", StringComparison.Ordinal);

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.AnimationCurve:
                    return true;

                // ObjectReference / Generic / ManagedReference / 其余一律不碰
                default:
                    return false;
            }
        }

        // ── 小工具 ────────────────────────────────────────────────────────────

        private static string ScenePath(Transform transform)
        {
            var builder = new StringBuilder(transform.name);
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                builder.Insert(0, parent.name + "/");

            return builder.ToString();
        }

        private static int IndexAmongSameType(Component component)
        {
            var index = 0;
            var typeName = component.GetType().Name;

            foreach (var other in component.gameObject.GetComponents<Component>())
            {
                if (other == null) continue;
                if (ReferenceEquals(other, component)) return index;
                if (other.GetType().Name == typeName) index++;
            }

            return 0;
        }

        private static Component NthOfType(GameObject gameObject, string typeName, int index)
        {
            var seen = 0;
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null) continue;
                if (component.GetType().Name != typeName) continue;
                if (seen == index) return component;
                seen++;
            }

            return null;
        }
    }
}
#endif
