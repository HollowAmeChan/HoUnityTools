using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdSequencePlayer))]
    public sealed class HoVmdSequencePlayerEditor : UnityEditor.Editor
    {
        bool advanced;
        string message;
        MessageType messageType;
        HoVmdSequencePlayer Player => (HoVmdSequencePlayer)target;
        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("MMD 影片播放", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选主角色和配套素材，生成一次适配后进入 Play。动作、表情、镜头与 BGM 共用时间；映射保存在本组件内部，不生成映射配置文件。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(P("targetAnimator"));
                EditorGUILayout.PropertyField(P("motionClip"));
                EditorGUILayout.PropertyField(P("expressionClip"));
                EditorGUILayout.PropertyField(P("cameraClip"));
                EditorGUILayout.PropertyField(P("soundtrack"));
                if (GUILayout.Button("查找动作同目录的表情 / 镜头 / 音乐"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Run(FindCompanions);
                    serializedObject.Update();
                }
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(P("referenceRigAsset"),
                    new GUIContent("参考骨架 PMX", "作者的 vmdsizing_bone 或 Sour 模型；仅读取静止骨骼尺寸。角色仍使用选定的 FBX/Humanoid，不需要转成 PMX。"));
                if (Player.ReferenceRigAsset == null && GUILayout.Button("使用上次参考骨架"))
                {
                    Undo.RecordObject(Player, "参考骨架");
                    Player.ReferenceRigAsset = AssetDatabase.LoadMainAssetAtPath(EditorPrefs.GetString("HoVmdSequence.ReferenceRig", ""));
                    EditorUtility.SetDirty(Player);
                    serializedObject.Update();
                }
                if (GUILayout.Button(Player.Body == null ? "生成角色适配与播放配置" : "按当前角色重新生成适配"))
                    Run(() =>
                    {
                        serializedObject.ApplyModifiedProperties();
                        Undo.RegisterFullObjectHierarchyUndo(Player.gameObject, "配置 MMD 影片");
                        if (Player.TargetAnimator != null) Undo.RegisterFullObjectHierarchyUndo(Player.TargetAnimator.gameObject, "角色 MMD 适配");
                        HoVmdSequenceBuilder.Configure(Player);
                        EditorSceneManager.MarkSceneDirty(Player.gameObject.scene);
                        message = "已生成。进入 Play 预览；换动作只需更新素材，换角色再重新生成。";
                    });
                using (new EditorGUI.DisabledScope(Player.Body == null))
                    if (GUILayout.Button("更新动作 / 表情 / 镜头 / 音乐（保留适配）"))
                        Run(() =>
                        {
                            serializedObject.ApplyModifiedProperties();
                            Player.ApplySources();
                            EditorUtility.SetDirty(Player);
                            EditorSceneManager.MarkSceneDirty(Player.gameObject.scene);
                            message = "素材已更新，现有适配和舞台设置保留。";
                        });
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(P("playOnEnable"));
            EditorGUILayout.PropertyField(P("loop"));
            EditorGUILayout.PropertyField(P("preroll"));
            EditorGUILayout.PropertyField(P("soundtrackTimeOffset"),
                new GUIContent("BGM 时间偏移（秒）", "正值延后 BGM，负值提前。实时播放和离线合音使用同一个值；延后会延长自动时长，手动时长保持不变。"));
            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Player.Playing ? "暂停" : "继续")) Player.Playing = !Player.Playing;
                    if (GUILayout.Button("从头播放")) Player.Restart();
                }
                EditorGUILayout.LabelField("时间", Player.CurrentTime.ToString("0.00") + " / " + Player.Duration.ToString("0.00") + " 秒");
            }
            using (new EditorGUI.DisabledScope(Player.ExpressionClip == null || Player.TargetAnimator == null))
                if (GUILayout.Button("检查实际使用的表情"))
                    Run(() =>
                    {
                        var active = ScriptableObject.CreateInstance<HoVmdAnimation>();
                        try
                        {
                            active.morphFrames.AddRange(Player.ExpressionClip.morphFrames.Where(x => Mathf.Abs(x.weight) > .00001f));
                            var audit = HoVmdMorphAudit.Audit(active, Player.TargetAnimator.gameObject, null, Player.Face != null ? Player.Face.MorphBindings : null);
                            message = $"有效表情 {audit.required}；缺少 {audit.missing}。" +
                                (audit.missing > 0 ? "\n" + string.Join("、", audit.missingNames) + "\n可在内部表情播放器补充本地映射。" : "");
                            messageType = audit.missing > 0 ? MessageType.Warning : MessageType.Info;
                        }
                        finally { Object.DestroyImmediate(active); }
                    });
            advanced = EditorGUILayout.Foldout(advanced, "高级 · 时间与内部播放器", true);
            if (advanced)
            {
                EditorGUILayout.PropertyField(P("frameRate"));
                EditorGUILayout.PropertyField(P("duration"));
                foreach (string field in new[] { "body", "face", "retargeter", "cameraPlayer", "music" })
                    EditorGUILayout.PropertyField(P(field));
                EditorGUILayout.HelpBox("内部播放器、参考骨架和 IK 自动生成。调整镜头构图可选中镜头播放器；手工补表情可选中表情播放器。无需创建 AnimationClip 或 BindingProfile。", MessageType.None);
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
            serializedObject.ApplyModifiedProperties();
        }

        void FindCompanions()
        {
            if (Player.MotionClip == null) throw new InvalidOperationException("先选择动作 VMD。");
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(Player.MotionClip));
            Undo.RecordObject(Player, "查找 VMD 配套素材");
            Player.ExpressionClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(folder + "/facial.vmd");
            Player.CameraClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(folder + "/camera.vmd");
            if (Player.Soundtrack == null)
                foreach (string path in Directory.GetFiles(folder).Where(x => new[] { ".wav", ".mp3", ".ogg" }.Contains(Path.GetExtension(x).ToLowerInvariant())))
                {
                    AudioClip audio = AssetDatabase.LoadAssetAtPath<AudioClip>(path.Replace('\\', '/'));
                    if (audio != null) { Player.Soundtrack = audio; break; }
                }
            EditorUtility.SetDirty(Player);
            message = "已查找同目录配套素材。音乐为空时可手动指定已有音轨。";
            messageType = MessageType.Info;
        }
        void Run(Action action)
        {
            try { messageType = MessageType.Info; action(); serializedObject.Update(); }
            catch (Exception error) { message = error.Message; messageType = MessageType.Error; Debug.LogException(error); }
        }
    }
}
