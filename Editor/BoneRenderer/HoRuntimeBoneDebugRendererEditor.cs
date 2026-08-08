#if UNITY_EDITOR
using System.Collections.Generic;
using Hollow.HoUnityTools.BoneRendering;
using Hollow.HoUnityTools.WarudoModUtils;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.BoneRendering
{
    [CustomEditor(typeof(HoRuntimeBoneDebugRenderer))]
    [CanEditMultipleObjects]
    internal sealed class HoRuntimeBoneDebugRendererEditor : UnityEditor.Editor
    {
        private SerializedProperty skeletonRoot;
        private SerializedProperty includeRoot;
        private SerializedProperty drawBones;
        private SerializedProperty drawAxes;
        private SerializedProperty lineWidth;
        private SerializedProperty lineWidthPixels;
        private SerializedProperty axisLength;
        private SerializedProperty boneColor;
        private SerializedProperty xAxisColor;
        private SerializedProperty yAxisColor;
        private SerializedProperty zAxisColor;
        private SerializedProperty groupJson;
        private SerializedProperty filterByCollections;
        private SerializedProperty useCollectionColors;
        private SerializedProperty viewCamera;
        private SerializedProperty debugMaterial;
        private SerializedProperty forceOverlay;
        private SerializedProperty drawAfterCamera;
        private SerializedProperty refreshOnEnable;

        private void OnEnable()
        {
            skeletonRoot = serializedObject.FindProperty("skeletonRoot");
            includeRoot = serializedObject.FindProperty("includeRoot");
            drawBones = serializedObject.FindProperty("drawBones");
            drawAxes = serializedObject.FindProperty("drawAxes");
            lineWidth = serializedObject.FindProperty("lineWidth");
            lineWidthPixels = serializedObject.FindProperty("lineWidthPixels");
            axisLength = serializedObject.FindProperty("axisLength");
            boneColor = serializedObject.FindProperty("boneColor");
            xAxisColor = serializedObject.FindProperty("xAxisColor");
            yAxisColor = serializedObject.FindProperty("yAxisColor");
            zAxisColor = serializedObject.FindProperty("zAxisColor");
            groupJson = serializedObject.FindProperty("groupJson");
            filterByCollections = serializedObject.FindProperty("filterByCollections");
            useCollectionColors = serializedObject.FindProperty("useCollectionColors");
            viewCamera = serializedObject.FindProperty("viewCamera");
            debugMaterial = serializedObject.FindProperty("debugMaterial");
            forceOverlay = serializedObject.FindProperty("forceOverlay");
            drawAfterCamera = serializedObject.FindProperty("drawAfterCamera");
            refreshOnEnable = serializedObject.FindProperty("refreshOnEnable");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSection("骨架来源");
            EditorGUILayout.PropertyField(skeletonRoot, new GUIContent("骨架根节点"));
            EditorGUILayout.PropertyField(includeRoot, new GUIContent("包含根节点"));

            EditorGUILayout.Space(5f);
            DrawSection("显示");
            EditorGUILayout.PropertyField(drawBones, new GUIContent("绘制骨链"));
            EditorGUILayout.PropertyField(drawAxes, new GUIContent("绘制轴向"));
            EditorGUILayout.PropertyField(lineWidth, new GUIContent("世界线宽"));
            EditorGUILayout.PropertyField(lineWidthPixels, new GUIContent("屏幕线宽"));
            EditorGUILayout.PropertyField(axisLength, new GUIContent("轴向长度"));
            EditorGUILayout.PropertyField(boneColor, new GUIContent("骨链颜色"));
            EditorGUILayout.PropertyField(xAxisColor, new GUIContent("X 轴颜色"));
            EditorGUILayout.PropertyField(yAxisColor, new GUIContent("Y 轴颜色"));
            EditorGUILayout.PropertyField(zAxisColor, new GUIContent("Z 轴颜色"));

            EditorGUILayout.Space(5f);
            DrawGroupingSection();

            EditorGUILayout.Space(5f);
            DrawSection("运行时绘制");
            EditorGUILayout.PropertyField(viewCamera, new GUIContent("观察相机"));
            EditorGUILayout.PropertyField(debugMaterial, new GUIContent("调试材质"));
            EditorGUILayout.PropertyField(forceOverlay, new GUIContent("强制最前显示"));
            EditorGUILayout.PropertyField(drawAfterCamera, new GUIContent("相机绘制后绘制"));
            EditorGUILayout.PropertyField(refreshOnEnable, new GUIContent("启用时刷新骨架"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGroupingSection()
        {
            DrawSection("骨骼集合");

            bool sourceChanged;
            bool refreshClicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(groupJson, new GUIContent("分组 JSON"));
                sourceChanged = EditorGUI.EndChangeCheck();

                using (new EditorGUI.DisabledScope(groupJson.objectReferenceValue == null))
                    refreshClicked = GUILayout.Button(new GUIContent("↻", "重新解析分组 JSON"), GUILayout.Width(24f));
            }

            EditorGUILayout.PropertyField(filterByCollections, new GUIContent("启用集合过滤"));
            EditorGUILayout.PropertyField(useCollectionColors, new GUIContent("使用集合颜色"));

            if (sourceChanged)
            {
                serializedObject.ApplyModifiedProperties();
                RefreshTargets();
                serializedObject.Update();
            }
            else if (refreshClicked)
            {
                RefreshTargets();
                serializedObject.Update();
            }

            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("多选时不显示逐集合开关。", MessageType.None);
                return;
            }

            var renderer = target as HoRuntimeBoneDebugRenderer;
            HoBoneGroupSet set = renderer == null ? null : renderer.GetActiveGroupSet();
            if (set == null)
            {
                EditorGUILayout.HelpBox("指定分组 JSON 后可逐集合控制显示。", MessageType.None);
                return;
            }

            if (set.collections == null || set.collections.Count == 0)
            {
                EditorGUILayout.HelpBox("分组 JSON 没有可用集合，请检查内容或点击刷新。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部显示", EditorStyles.miniButtonLeft))
                    SetAllCollectionsHidden(renderer, set, false);
                if (GUILayout.Button("全部隐藏", EditorStyles.miniButtonRight))
                    SetAllCollectionsHidden(renderer, set, true);
            }

            var depthCache = new Dictionary<string, int>();
            for (int i = 0; i < set.collections.Count; i++)
            {
                HoBoneCollection collection = set.collections[i];
                if (collection == null || string.IsNullOrEmpty(collection.name))
                    continue;

                bool visible = !renderer.hiddenCollections.Contains(collection.name);
                int depth = GetCollectionDepth(set, collection.name, depthCache);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 14f);
                    Rect colorRect = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
                    EditorGUI.DrawRect(colorRect, collection.color);

                    int boneCount = collection.bones == null ? 0 : collection.bones.Count;
                    string label = collection.isOther
                        ? collection.name
                        : collection.name + " (" + boneCount + ")";
                    EditorGUI.BeginChangeCheck();
                    bool nextVisible = EditorGUILayout.ToggleLeft(label, visible);
                    if (EditorGUI.EndChangeCheck())
                        SetCollectionHidden(renderer, collection.name, !nextVisible);
                }
            }
        }

        private void RefreshTargets()
        {
            foreach (Object value in targets)
            {
                var renderer = value as HoRuntimeBoneDebugRenderer;
                if (renderer == null)
                    continue;

                renderer.RefreshGroupJson();
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void SetCollectionHidden(
            HoRuntimeBoneDebugRenderer renderer,
            string collectionName,
            bool hidden)
        {
            Undo.RecordObject(renderer, "切换运行时骨骼集合可见性");
            renderer.filterByCollections = true;
            if (hidden)
            {
                if (!renderer.hiddenCollections.Contains(collectionName))
                    renderer.hiddenCollections.Add(collectionName);
            }
            else
            {
                renderer.hiddenCollections.Remove(collectionName);
            }

            renderer.RefreshGroupJson();
            EditorUtility.SetDirty(renderer);
        }

        private static void SetAllCollectionsHidden(
            HoRuntimeBoneDebugRenderer renderer,
            HoBoneGroupSet set,
            bool hidden)
        {
            Undo.RecordObject(renderer, hidden ? "隐藏全部运行时骨骼集合" : "显示全部运行时骨骼集合");
            renderer.filterByCollections = true;
            renderer.hiddenCollections.Clear();
            if (hidden)
            {
                for (int i = 0; i < set.collections.Count; i++)
                {
                    HoBoneCollection collection = set.collections[i];
                    if (collection != null && !string.IsNullOrEmpty(collection.name))
                        renderer.hiddenCollections.Add(collection.name);
                }
            }

            renderer.RefreshGroupJson();
            EditorUtility.SetDirty(renderer);
        }

        private static int GetCollectionDepth(
            HoBoneGroupSet set,
            string collectionName,
            Dictionary<string, int> cache)
        {
            int cached;
            if (cache.TryGetValue(collectionName, out cached))
                return cached;

            int depth = 0;
            string current = collectionName;
            int guard = 0;
            int max = set.collections.Count + 1;
            while (guard <= max)
            {
                HoBoneCollection collection = set.FindCollection(current);
                if (collection == null || string.IsNullOrEmpty(collection.parent))
                    break;

                depth++;
                current = collection.parent;
                guard++;
            }

            cache[collectionName] = depth;
            return depth;
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
#endif
