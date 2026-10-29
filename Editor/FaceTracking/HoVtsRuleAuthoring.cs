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
    /// <summary>Detached rule drafts, explicit apply/save, and recoverable pre-save snapshots.</summary>
    public static class HoVtsRuleAuthoring
    {
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
            serializedObject.ApplyModifiedProperties();
            var receiver = (HoVtsParameterReceiver)target;
            var rules = receiver.Middleware;
            EditorGUILayout.LabelField("角色规则", (rules?.inputs?.Count ?? 0) + " 输入 / " + (rules?.outputs?.Count ?? 0) + " 输出");
            if (GUILayout.Button("编辑角色规则")) HoFaceProfileWindow.Open(receiver);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("状态", !receiver.HasFrame ? "等待输入" : receiver.InputExpired ? "输入超时" : receiver.Tracked ? "追踪中" : "未追踪");
                EditorGUILayout.LabelField("来源", receiver.Source);
                if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
            }
        }
    }
}
