using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;

namespace Hollow.HoUnityTools.Editor.TransformConstraints
{
    [CustomEditor(typeof(HoTransformConstraint),true)]
    public sealed class HoTransformConstraintEditor : UnityEditor.Editor
    {
        private ReorderableList sourceList;
        private bool offsetsExpanded;
        private HoTransformConstraint Constraint=>(HoTransformConstraint)target;
        private SerializedProperty P(string name)=>serializedObject.FindProperty(name);
        private void OnEnable()
        {
            sourceList=new ReorderableList(serializedObject,P("sources"),true,true,true,true);
            sourceList.drawHeaderCallback=rect=>EditorGUI.LabelField(rect,"Sources · 来源（正权重自动归一化）");
            sourceList.elementHeight=EditorGUIUtility.singleLineHeight+5;
            sourceList.drawElementCallback=(rect,index,active,focused)=>{
                var item=sourceList.serializedProperty.GetArrayElementAtIndex(index);rect.height=EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(new Rect(rect.x,rect.y,rect.width-75,rect.height),item.FindPropertyRelative("transform"),GUIContent.none);
                EditorGUI.PropertyField(new Rect(rect.xMax-70,rect.y,70,rect.height),item.FindPropertyRelative("weight"),GUIContent.none);
            };
            sourceList.onAddCallback=list=>{
                int index=list.serializedProperty.arraySize++;var item=list.serializedProperty.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("transform").objectReferenceValue=null;
                item.FindPropertyRelative("weight").floatValue=1;
                item.FindPropertyRelative("positionOffset").vector3Value=Vector3.zero;
                item.FindPropertyRelative("rotationOffset").vector3Value=Vector3.zero;
                item.FindPropertyRelative("scaleMultiplier").vector3Value=Vector3.one;
                item.FindPropertyRelative("offsetSource").objectReferenceValue=null;
            };
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Ho "+Constraint.Kind+" Constraint",EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(P("targetTransform"),new GUIContent("Target · 被驱动对象","组件可以放在任意宿主；只写这个 Transform，不会移动宿主"));
            sourceList.DoLayoutList();
            EditorGUILayout.PropertyField(P("constraintActive"),new GUIContent("启用约束"));
            EditorGUILayout.PropertyField(P("weight"),new GUIContent("总体权重"));
            EditorGUILayout.PropertyField(P("order"),new GUIContent("Order · 同目标顺序","同一 Target 同一通道的多个写者必须使用不同 Order"));
            EditorGUILayout.PropertyField(P("phase"),new GUIContent("更新阶段"));
            if(Constraint.Kind!=HoTransformConstraintKind.Aim)EditorGUILayout.PropertyField(P("space"),new GUIContent("求解空间"));
            EditorGUILayout.PropertyField(P("input"),new GUIContent("基础输入","Rest Pose：稳定绑定基线；Animated：本阶段开始时的动画输入，目标必须有每帧上游写者"));
            if((Constraint.WrittenChannels&HoTransformChannels.Position)!=0||Constraint.Kind==HoTransformConstraintKind.Position||Constraint.Kind==HoTransformConstraintKind.Parent)
                EditorGUILayout.PropertyField(P("positionAxes"),new GUIContent("位置轴"));
            if(Constraint.Kind==HoTransformConstraintKind.Rotation||Constraint.Kind==HoTransformConstraintKind.Parent||Constraint.Kind==HoTransformConstraintKind.Aim)
                EditorGUILayout.PropertyField(P("rotationAxes"),new GUIContent("旋转轴","部分旋转轴采用 Unity Euler 分解，存在 Euler／万向节锁限制"));
            if(Constraint.Kind==HoTransformConstraintKind.Scale)EditorGUILayout.PropertyField(P("scaleAxes"),new GUIContent("缩放轴"));
            if(Constraint is HoAimConstraint)
            {
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(P("aimAxis"),new GUIContent("瞄准轴"));
                EditorGUILayout.PropertyField(P("upAxis"),new GUIContent("本地 Up 轴"));
                EditorGUILayout.PropertyField(P("upMode"),new GUIContent("上方向模式"));
                if(P("upMode").enumValueIndex>=2)EditorGUILayout.PropertyField(P("upObject"),new GUIContent("Up Object"));
                if(P("upMode").enumValueIndex==1||P("upMode").enumValueIndex==3)EditorGUILayout.PropertyField(P("worldUp"),new GUIContent("Up 向量"));
                EditorGUILayout.PropertyField(P("rotationOffset"),new GUIContent("朝向偏移"));
            }
            offsetsExpanded=EditorGUILayout.Foldout(offsetsExpanded,"每源偏移 / 缩放比例",true);
            if(offsetsExpanded)
                for(int i=0;i<P("sources").arraySize;i++)
                {
                    var s=P("sources").GetArrayElementAtIndex(i);EditorGUILayout.LabelField("Source "+i,EditorStyles.miniBoldLabel);
                    if(Constraint.Kind==HoTransformConstraintKind.Position||Constraint.Kind==HoTransformConstraintKind.Parent||Constraint.Kind==HoTransformConstraintKind.Aim)
                        EditorGUILayout.PropertyField(s.FindPropertyRelative("positionOffset"),new GUIContent("来源本地位置偏移"));
                    if(Constraint.Kind==HoTransformConstraintKind.Rotation||Constraint.Kind==HoTransformConstraintKind.Parent)
                        EditorGUILayout.PropertyField(s.FindPropertyRelative("rotationOffset"),new GUIContent("来源相对旋转偏移"));
                    if(Constraint.Kind==HoTransformConstraintKind.Scale)
                        EditorGUILayout.PropertyField(s.FindPropertyRelative("scaleMultiplier"),new GUIContent("来源缩放乘数"));
                }
            EditorGUILayout.PropertyField(P("previewInEditMode"),new GUIContent("编辑预览（临时）","关闭、保存、进入 Play 或脚本重载前恢复编辑姿态；Prefab 持久资产不参与预览"));
            EditorGUILayout.PropertyField(P("drawGizmos"),new GUIContent("显示目标／来源连线"));
            serializedObject.ApplyModifiedProperties();
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("保持编辑姿态并启用"))Activate(true);
                if(GUILayout.Button("清零偏移并启用"))Activate(false);
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("求值一次（可撤销）"))
                {
                    HoTransformConstraintPreview.RestoreAll();
                    if(Constraint.TargetTransform!=null)Undo.RecordObject(Constraint.TargetTransform,"Ho Constraint 求值一次");
                    Constraint.EvaluateNow();RecordTarget();
                }
                if(GUILayout.Button("恢复绑定姿态"))
                {
                    HoTransformConstraintPreview.RestoreAll();
                    if(Constraint.TargetTransform!=null)Undo.RecordObject(Constraint.TargetTransform,"恢复 Ho Constraint 绑定姿态");
                    Constraint.RestoreRestPose();RecordTarget();
                }
            }
            if(!Constraint.ValidateConfiguration(out string error))EditorGUILayout.HelpBox(error,MessageType.Info);
            else if(!string.IsNullOrEmpty(Constraint.LastError))EditorGUILayout.HelpBox(Constraint.LastError,MessageType.Warning);
            else if(!Constraint.ConstraintActive)EditorGUILayout.HelpBox("配置已捕获，约束未启用。",MessageType.Info);
            else if(!Application.isPlaying&&!Constraint.PreviewInEditMode)EditorGUILayout.HelpBox("已启用；编辑预览关闭，播放时生效。",MessageType.None);
            EditorGUILayout.HelpBox("Parent 只驱动位置／旋转；来源本地位置偏移会随来源缩放。两源旋转使用 Slerp，多源按列表顺序递进加权。外部 IK／物理的最终写入时机需选择匹配阶段或手动调用。",MessageType.None);
        }
        private void Activate(bool keep)
        {
            HoTransformConstraintPreview.RestoreAll();Undo.RecordObject(Constraint,"捕获 Ho Constraint 配置");
            if(!Constraint.Activate(keep,out string error))Debug.LogWarning(error,Constraint);
            EditorUtility.SetDirty(Constraint);
            if(PrefabUtility.IsPartOfPrefabInstance(Constraint))PrefabUtility.RecordPrefabInstancePropertyModifications(Constraint);
            serializedObject.Update();
        }
        private void RecordTarget()
        {
            if(Constraint.TargetTransform!=null&&PrefabUtility.IsPartOfPrefabInstance(Constraint.TargetTransform))
                PrefabUtility.RecordPrefabInstancePropertyModifications(Constraint.TargetTransform);
        }
    }
}
