using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>Reusable JSON profile references, migration from legacy inline rules, and editor assignment.</summary>
    public static class HoVtsRuleAuthoring
    {
        public static void AssignProfile(HoVtsParameterReceiver component, TextAsset profile, bool saveToPrefab = false)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            HoVtsParameterReceiver destination = saveToPrefab ? PrefabSource(component) : component;
            if (destination == null) throw new InvalidOperationException("当前实例没有可保存的预制件来源。");
            if (EditorUtility.IsPersistent(destination) && !AssetDatabase.IsOpenForEdit(AssetDatabase.GetAssetPath(destination)))
                throw new InvalidOperationException("角色预制件不可编辑。");

            Undo.RecordObject(destination, "指定面捕规则 JSON");
            destination.SetProfile(profile);
            EditorUtility.SetDirty(destination);
            PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
            if (PrefabUtility.IsPartOfPrefabAsset(destination))
                PrefabUtility.SavePrefabAsset(destination.transform.root.gameObject);
            else if (destination.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);

            if (destination != component) component.SetProfile(profile);
        }

        /// <summary>把旧组件里的内嵌规则一次性写成可复用 JSON，并接回组件。</summary>
        public static TextAsset CreateProfileFromInline(HoVtsParameterReceiver component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            string path = EditorUtility.SaveFilePanelInProject(
                "创建面捕规则 JSON", "face-profile" + HoFaceProfile.Extension,
                "hoface.json", "把当前组件规则保存为可复用的 JSON 资产。");
            if (string.IsNullOrEmpty(path)) return null;
            System.IO.File.WriteAllText(path, HoFaceProfile.Write(component.Middleware), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            var profile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            AssignProfile(component, profile);
            return profile;
        }

        public static HoFaceMiddleware Clone(HoFaceMiddleware rules)
        {
            if (!HoFaceProfile.TryParse(HoFaceProfile.Write(rules), out var copy, out string error))
                throw new InvalidOperationException(error);
            return copy;
        }

        public static HoVtsParameterReceiver PrefabSource(HoVtsParameterReceiver component)
        {
            if (component == null) return null;
            return EditorUtility.IsPersistent(component) ? component : PrefabUtility.GetCorrespondingObjectFromSource(component);
        }

        /// <returns>The pre-save backup path, or null for a temporary play-mode apply.</returns>
        public static string Commit(HoVtsParameterReceiver component, HoFaceMiddleware draft, bool saveToPrefab = false)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            HoFaceMiddleware copy = Clone(draft);
            bool temporary = Application.isPlaying && !EditorUtility.IsPersistent(component) && !saveToPrefab;
            if (temporary)
            {
                ApplyLive(component, copy);
                return null;
            }
            HoVtsParameterReceiver destination = saveToPrefab ? PrefabSource(component) : component;
            if (destination == null) throw new InvalidOperationException("当前实例没有可保存的预制件来源，请在编辑模式配置角色组件。");
            if (EditorUtility.IsPersistent(destination) && !AssetDatabase.IsOpenForEdit(AssetDatabase.GetAssetPath(destination)))
                throw new InvalidOperationException("角色预制件不可编辑。");

            bool updateLive = destination != component && Application.isPlaying;
            bool hadFrame = updateLive && component.HasFrame;
            var raw = hadFrame ? new Dictionary<string, float>(StringComparer.Ordinal) : null;
            if (hadFrame) component.CopyRawTo(raw);
            double receivedAt = component.LastFrameTime;
            bool wasTracked = component.Tracked;
            string frameSource = component.Source;

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/HoVtsRuleBackups"));
            Directory.CreateDirectory(directory);
            string backup = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".hoface.json");
            using (var writer = new StreamWriter(new FileStream(backup, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
                writer.Write(HoFaceProfile.Write(destination.Middleware));

            Undo.RecordObject(destination, "保存 VTS 角色规则");
            destination.SetMiddleware(copy);
            EditorUtility.SetDirty(destination);
            PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
            if (PrefabUtility.IsPartOfPrefabAsset(destination))
                PrefabUtility.SavePrefabAsset(destination.transform.root.gameObject);
            else if (destination.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);

            if (updateLive)
            {
                component.SetMiddleware(Clone(copy));
                // Saving a prefab can disable/re-enable its play-mode instances, which clears
                // transient input. Restore the actual packet time instead of refreshing its age.
                if (hadFrame)
                {
                    component.ClearFrame();
                    component.ApplyRawFrame(raw, wasTracked, receivedAt, frameSource);
                    component.Tick(Time.realtimeSinceStartupAsDouble);
                }
            }
            return backup;
        }

        private static void ApplyLive(HoVtsParameterReceiver component, HoFaceMiddleware copy)
        {
            component.SetMiddleware(copy);
            if (component.isActiveAndEnabled && component.HasFrame) component.Tick(Time.realtimeSinceStartupAsDouble);
        }
    }

    [CustomEditor(typeof(HoVtsParameterReceiver))]
    public sealed class HoVtsParameterReceiverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("inputTimeoutSeconds"), new GUIContent("输入超时（秒）"));
            var profileProperty = serializedObject.FindProperty("profileAsset");
            EditorGUILayout.PropertyField(profileProperty, new GUIContent("面捕规则 JSON"));
            serializedObject.ApplyModifiedProperties();
            var receiver = (HoVtsParameterReceiver)target;
            var rules = receiver.Middleware;
            if (receiver.ProfileAsset == null)
            {
                EditorGUILayout.HelpBox("当前组件还没有引用规则 JSON。旧内嵌规则仍可运行，建议先创建可复用资产。", MessageType.Warning);
                if (GUILayout.Button("从当前规则创建 JSON"))
                {
                    try { HoVtsRuleAuthoring.CreateProfileFromInline(receiver); }
                    catch (Exception error) { Debug.LogError("[Ho 面捕] 创建规则 JSON 失败：" + error.Message); }
                }
            }
            else
            {
                EditorGUILayout.LabelField("规则", AssetDatabase.GetAssetPath(receiver.ProfileAsset));
                EditorGUILayout.LabelField("规则行", (rules?.inputs?.Count ?? 0) + " 输入 / " + (rules?.outputs?.Count ?? 0) + " 输出");
            }
            if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
            using (new EditorGUI.DisabledScope(receiver.ProfileAsset == null))
                if (GUILayout.Button("编辑规则 JSON")) HoFaceProfileWindow.Open(receiver);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("状态", !receiver.HasFrame ? "等待输入" : receiver.InputExpired ? "输入超时" : receiver.Tracked ? "追踪中" : "未追踪");
                EditorGUILayout.LabelField("来源", receiver.Source);
                if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
            }
        }
    }
}
