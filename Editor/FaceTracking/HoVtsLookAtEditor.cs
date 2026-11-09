using System;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoVtsLookAt))]
    public sealed class HoVtsLookAtEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var lookAt = target as HoVtsLookAt;
            if (lookAt == null)
            {
                base.OnInspectorGUI();
                return;
            }

            Field("parameterReceiver", "参数接收器");
            Field("referenceFrame", "姿态参考物体", "使用这个物体的轴向解释 VTS 角度，通常是角色根物体。");
            Field("upperBodyChain", "上身骨骼链", "按 Spine → Chest → UpperChest 顺序填写。它是分布式姿态跟随链，不是单根骨骼 IK。通常优先使用 Chest/UpperChest，不要只填头骨。");
            Field("head", "头部骨骼");
            Field("leftEye", "左眼骨骼");
            Field("rightEye", "右眼骨骼");
            // 这里以前还有一个「参数来源」（raw / 中间层输出）下拉。那个字段在 2026-10-31
            // 随「LookAt 不再吃 raw」一起删掉了，编辑器没跟上 ⇒ FindProperty 返回 null，
            // OnInspectorGUI 第 20 行 NRE。现在 LookAt 只读中间层输出，不需要这个开关。

            var driverProperty = serializedObject.FindProperty("eyeDriver");
            HoVtsEyeDriver previousDriver = driverProperty != null ? (HoVtsEyeDriver)driverProperty.enumValueIndex : lookAt.EyeDriver;
            Field("eyeDriver", "模式", "骨骼：驱动眼骨，可用四点标定适配不同模型的初始朝向。形态键：直接写眼球方向形态键，不要求眼骨。");
            HoVtsEyeDriver driver = driverProperty != null ? (HoVtsEyeDriver)driverProperty.enumValueIndex : previousDriver;
            if (driver != previousDriver) lookAt.Stop();

            // 标定按钮要在 ApplyModifiedProperties 之后改对象，否则会被序列化快照覆盖回去。
            Action afterApply = null;
            if (driver == HoVtsEyeDriver.Bone)
            {
                Field("boneFullAngle", "骨骼满偏角度", "VTS 眼球角度达到该值时驱动到记录的满偏姿态。");
                EditorGUILayout.HelpBox("记录前先在场景里把眼骨手摆到对应方向；Play 模式下组件会持续覆盖眼骨，建议先取消「控制双眼」或停止运行。", MessageType.Info);
                afterApply = DrawBoneCalibration(lookAt.LeftEye, lookAt.LeftBoneCalibration, "左眼");
                var rightAction = DrawBoneCalibration(lookAt.RightEye, lookAt.RightBoneCalibration, "右眼");
                if (rightAction != null) afterApply = rightAction;
            }
            else
            {
                Field("eyeKeyMeshes", "形态键网格", "每只眼睛的四个方向键会写到列表里所有含该键的网格；不要求眼骨。");
                DrawKeyTargets("leftKeyTargets", "左眼形态键");
                DrawKeyTargets("rightKeyTargets", "右眼形态键");
                Field("keyFullAngle", "形态键满偏角度", "VTS 眼球角度达到该值时形态键到满权重。");
                Field("keyWeightScale", "权重倍率", "满偏时写入的权重（0-200，最终仍会 clamp 到 0-100）。");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Field("previewCalibration", "预览标定", "临时接管眼球，不读 VTS 的眼球参数；头/上身不受影响。");
            SliderField("previewYaw", "水平（VTS 度）", -90f, 90f);
            SliderField("previewPitch", "垂直（VTS 度）", -90f, 90f);
            bool previewChanged = EditorGUI.EndChangeCheck();
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("编辑模式只更新 Gizmo 里的眼球目标朝向；进入 Play 模式后才会真正驱动眼球。", MessageType.Info);

            Field("headEnabled", "控制头部");
            using (new EditorGUI.DisabledScope(!(serializedObject.FindProperty("headEnabled")?.boolValue ?? false)))
            {
                Field("headWeight", "头部转动强度", "0 = 头完全不跟 VTS 转，1 = 全量。");
                Field("headMaxAngle", "头部最大转角", "VTS 头部姿态的总转角超过该值时截断；180 ≈ 不限制。");
            }
            Field("upperBodyEnabled", "上身摆动");
            using (new EditorGUI.DisabledScope(!(serializedObject.FindProperty("upperBodyEnabled")?.boolValue ?? false)))
            {
                Field("upperBodyWeight", "上身摆动强度", "0 = 不动，1 = 全量；整条链的总量会按骨骼数平分。");
                Field("upperBodyMaxAngle", "上身最大转角", "上身姿态的总转角超过该值时截断；按骨骼链分发前先截断。180 ≈ 不限制。");
            }
            Field("eyesEnabled", "控制双眼");
            Field("smoothingSeconds", "姿态平滑（秒）", "0 为直通；丢追时使用同样的平滑回到中性姿态。");
            Field("drawGizmos", "场景姿态 Gizmo", "选中组件时显示参考轴、实际骨骼朝向和目标朝向。");
            using (new EditorGUI.DisabledScope(!(serializedObject.FindProperty("drawGizmos")?.boolValue ?? false)))
                Field("gizmoLength", "Gizmo 长度");
            Field("autoRun", "自动运行");
            serializedObject.ApplyModifiedProperties();

            afterApply?.Invoke();
            // 标定按钮直接改了标定对象；和 HoSpringConstraintEditor 一样，改完立刻 Update，
            // 让 SerializedObject 的缓存与真对象同步，避免下一帧 Apply 把旧四元数写回去。
            if (afterApply != null) serializedObject.Update();

            // 预览值已经由 SerializedProperty 写入；这里再走一次公开方法，保证运行时立即看到最新值。
            var previewOn = serializedObject.FindProperty("previewCalibration");
            var previewYaw = serializedObject.FindProperty("previewYaw");
            var previewPitch = serializedObject.FindProperty("previewPitch");
            if (previewOn != null && previewYaw != null && previewPitch != null)
                lookAt.SetPreview(previewOn.boolValue, previewYaw.floatValue, previewPitch.floatValue);
            if (previewChanged && !Application.isPlaying) SceneView.RepaintAll();

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("状态", lookAt.Status);
                HoVtsLookAtDebug debug = lookAt.GetDebug();
                EditorGUILayout.LabelField("输入", debug.hasFrame ? (debug.tracked ? "追踪中" : "未追踪") : "无当前帧");
                EditorGUILayout.LabelField("说明", "黄 = 实际骨骼前方，紫 = VTS 目标前方，红绿蓝 = 参考坐标轴");
            }
        }

        private void OnDisable()
        {
            var lookAt = target as HoVtsLookAt;
            if (lookAt != null) lookAt.SetPreview(false, lookAt.PreviewYaw, lookAt.PreviewPitch);
        }

        /// <summary>画一侧眼骨的四点标定。返回的 Action 必须在 ApplyModifiedProperties 之后执行。</summary>
        private Action DrawBoneCalibration(Transform bone, HoVtsEyeBoneCalibration calibration, string label)
        {
            if (calibration == null)
            {
                EditorGUILayout.HelpBox(label + "标定数据为空（组件序列化异常），请重置组件。", MessageType.Warning);
                return null;
            }

            EditorGUILayout.LabelField(label + "标定：" + (calibration.captured ? "已记录" : "未标定"), EditorStyles.boldLabel);
            if (bone == null)
                EditorGUILayout.HelpBox("未指定" + label + "骨骼，先在上方指定后才能记录标定。", MessageType.Warning);

            Action pending = null;
            using (new EditorGUI.DisabledScope(bone == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("记录静止")) pending = () => RecordCalibration(bone, calibration, 0);
                if (GUILayout.Button("记录左")) pending = () => RecordCalibration(bone, calibration, 1);
                if (GUILayout.Button("记录右")) pending = () => RecordCalibration(bone, calibration, 2);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("记录上")) pending = () => RecordCalibration(bone, calibration, 3);
                if (GUILayout.Button("记录下")) pending = () => RecordCalibration(bone, calibration, 4);
                if (GUILayout.Button("清除标定")) pending = () => ClearCalibration(calibration);
                EditorGUILayout.EndHorizontal();
            }
            return pending;
        }

        private void RecordCalibration(Transform bone, HoVtsEyeBoneCalibration calibration, int point)
        {
            var lookAt = (HoVtsLookAt)target;
            Undo.RecordObject(lookAt, "记录眼骨标定");
            Quaternion rotation = bone.localRotation;
            switch (point)
            {
                case 0: calibration.rest = rotation; break;
                case 1: calibration.left = rotation; break;
                case 2: calibration.right = rotation; break;
                case 3: calibration.up = rotation; break;
                default: calibration.down = rotation; break;
            }
            calibration.captured = true;
            EditorUtility.SetDirty(lookAt);
        }

        private void ClearCalibration(HoVtsEyeBoneCalibration calibration)
        {
            var lookAt = (HoVtsLookAt)target;
            Undo.RecordObject(lookAt, "清除眼骨标定");
            calibration.captured = false;
            calibration.rest = calibration.left = calibration.right = calibration.up = calibration.down = Quaternion.identity;
            EditorUtility.SetDirty(lookAt);
        }

        private void DrawKeyTargets(string property, string label)
        {
            var targets = serializedObject.FindProperty(property);
            if (targets == null)
            {
                Missing(property);
                return;
            }
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            KeyField(targets, "left", "左");
            KeyField(targets, "right", "右");
            KeyField(targets, "up", "上");
            KeyField(targets, "down", "下");
            EditorGUI.indentLevel--;
        }

        private void KeyField(SerializedProperty parent, string name, string label)
        {
            var found = parent.FindPropertyRelative(name);
            if (found == null)
            {
                Missing(parent.propertyPath + "." + name);
                return;
            }
            EditorGUILayout.PropertyField(found, new GUIContent(label));
        }

        /// <summary>
        /// 画一个序列化字段。字段被改名/删掉时 FindProperty 会返回 null —— 以前直接传进
        /// PropertyField 就是一条 NRE，整块 Inspector 报废；这里改成跳过并提示，别让编辑器炸。
        /// </summary>
        private void Field(string property, string label, string tooltip = null)
        {
            var found = serializedObject.FindProperty(property);
            if (found == null)
            {
                Missing(property);
                return;
            }
            EditorGUILayout.PropertyField(found, new GUIContent(label, tooltip));
        }

        private void SliderField(string property, string label, float minimum, float maximum)
        {
            var found = serializedObject.FindProperty(property);
            if (found == null)
            {
                Missing(property);
                return;
            }
            EditorGUILayout.Slider(found, minimum, maximum, new GUIContent(label));
        }

        private void Missing(string property)
        {
            EditorGUILayout.HelpBox("组件上找不到字段 " + property + "（编辑器与运行时代码不同步）。", MessageType.Warning);
        }
    }
}
