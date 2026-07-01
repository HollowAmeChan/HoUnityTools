using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.BoneRendering;

namespace Hollow.HoUnityTools.Editor.BoneRendering
{
    /// <summary>
    /// 骨骼渲染器组件的 Inspector。
    /// 提供骨架根采集、显示参数,以及基于分组文件的逐集合可见性开关(按层级缩进)。
    /// </summary>
    [CustomEditor(typeof(HoBoneRenderer))]
    [CanEditMultipleObjects]
    internal sealed class HoBoneRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent DrawBonesLabel = new GUIContent("绘制骨骼");
        private static readonly GUIContent BoneShapeLabel = new GUIContent("形状");
        private static readonly GUIContent BoneColorLabel = new GUIContent("默认颜色");
        private static readonly GUIContent DrawTripodsLabel = new GUIContent("绘制本地轴");

        private SerializedProperty skeletonRoot;
        private SerializedProperty includeRoot;
        private SerializedProperty drawBones;
        private SerializedProperty boneShape;
        private SerializedProperty boneSize;
        private SerializedProperty boneColor;
        private SerializedProperty drawTripods;
        private SerializedProperty tripodSize;
        private SerializedProperty groupJson;
        private SerializedProperty hiddenCollections;

        private void OnEnable()
        {
            skeletonRoot = serializedObject.FindProperty("skeletonRoot");
            includeRoot = serializedObject.FindProperty("includeRoot");
            drawBones = serializedObject.FindProperty("drawBones");
            boneShape = serializedObject.FindProperty("boneShape");
            boneSize = serializedObject.FindProperty("boneSize");
            boneColor = serializedObject.FindProperty("boneColor");
            drawTripods = serializedObject.FindProperty("drawTripods");
            tripodSize = serializedObject.FindProperty("tripodSize");
            groupJson = serializedObject.FindProperty("groupJson");
            hiddenCollections = serializedObject.FindProperty("hiddenCollections");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSkeletonSection();
            EditorGUILayout.Space(4.0f);
            DrawDisplaySection();
            EditorGUILayout.Space(4.0f);
            DrawGroupingSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSkeletonSection()
        {
            EditorGUILayout.LabelField("骨架", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(skeletonRoot, new GUIContent("骨架根"));
            EditorGUILayout.PropertyField(includeRoot, new GUIContent("包含根节点"));
            bool rootChanged = EditorGUI.EndChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("采集骨骼"))
                {
                    rootChanged = true;
                }

                using (new EditorGUI.DisabledScope(skeletonRoot.objectReferenceValue == null))
                {
                    EditorGUILayout.LabelField(GetBoneCountSummary(), EditorStyles.miniLabel);
                }
            }

            if (rootChanged)
            {
                serializedObject.ApplyModifiedProperties();
                foreach (Object target in targets)
                {
                    if (target is HoBoneRenderer boneRenderer)
                    {
                        boneRenderer.CollectFromSkeletonRoot();
                        EditorUtility.SetDirty(boneRenderer);
                    }
                }

                serializedObject.Update();
                SceneView.RepaintAll();
            }
        }

        private string GetBoneCountSummary()
        {
            if (targets.Length != 1 || !(target is HoBoneRenderer boneRenderer))
            {
                return string.Empty;
            }

            int transformCount = boneRenderer.Transforms != null ? boneRenderer.Transforms.Length : 0;
            int boneCount = boneRenderer.Bones != null ? boneRenderer.Bones.Length : 0;
            return $"已采集 {transformCount} 个节点 / {boneCount} 根骨骼";
        }

        private void DrawDisplaySection()
        {
            EditorGUILayout.LabelField("显示", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(drawBones, DrawBonesLabel);
                using (new EditorGUI.DisabledScope(!drawBones.boolValue))
                {
                    EditorGUILayout.PropertyField(boneSize, GUIContent.none);
                }
            }

            using (new EditorGUI.DisabledScope(!drawBones.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(boneShape, BoneShapeLabel);
                EditorGUILayout.PropertyField(boneColor, BoneColorLabel);
                EditorGUI.indentLevel--;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(drawTripods, DrawTripodsLabel);
                using (new EditorGUI.DisabledScope(!drawTripods.boolValue))
                {
                    EditorGUILayout.PropertyField(tripodSize, GUIContent.none);
                }
            }
        }

        private void DrawGroupingSection()
        {
            EditorGUILayout.LabelField("分组", EditorStyles.boldLabel);

            bool refreshClicked = false;
            bool groupSourceChanged;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(groupJson, new GUIContent("分组 JSON"));
                groupSourceChanged = EditorGUI.EndChangeCheck();

                // 小刷新按钮:重新导出覆盖同一 .json 后,手动强制重解析(引用未变不会自动刷新)
                using (new EditorGUI.DisabledScope(!HasGroupJsonToRefresh()))
                {
                    refreshClicked = GUILayout.Button(new GUIContent("↻", "从文件重新读取分组 JSON"), GUILayout.Width(22.0f));
                }
            }

            if (groupSourceChanged)
            {
                serializedObject.ApplyModifiedProperties();
                RebuildAllTargets();
                serializedObject.Update();
            }
            else if (refreshClicked)
            {
                RefreshAllTargets();
                serializedObject.Update();
            }

            // 单选时用组件实际生效的分组模型;多选不显示逐组开关。
            HoBoneGroupSet set = null;
            if (targets.Length == 1 && target is HoBoneRenderer boneRenderer)
            {
                set = boneRenderer.GetActiveGroupSet();
            }

            if (set == null)
            {
                EditorGUILayout.HelpBox("未指定分组 JSON 时,全部骨骼以默认颜色显示。", MessageType.None);
                return;
            }

            if (set.collections == null || set.collections.Count == 0)
            {
                EditorGUILayout.HelpBox("分组 JSON 中没有骨骼集合。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部显示", EditorStyles.miniButtonLeft))
                {
                    SetAllCollectionsHidden(false);
                }

                if (GUILayout.Button("全部隐藏", EditorStyles.miniButtonRight))
                {
                    SetAllCollectionsHidden(true);
                }
            }

            EditorGUILayout.Space(2.0f);

            // 按层级缩进列出集合开关。多选编辑时,可见性状态以第一个对象为准显示。
            HashSet<string> hidden = ReadHiddenSet();
            var depthCache = new Dictionary<string, int>();

            for (int i = 0; i < set.collections.Count; i++)
            {
                HoBoneCollection collection = set.collections[i];
                if (collection == null || string.IsNullOrEmpty(collection.name))
                {
                    continue;
                }

                int depth = GetCollectionDepth(set, collection.name, depthCache);
                bool visible = !hidden.Contains(collection.name);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 14.0f);

                    Color swatch = collection.color;
                    Rect colorRect = GUILayoutUtility.GetRect(12.0f, 12.0f, GUILayout.Width(12.0f), GUILayout.Height(12.0f));
                    EditorGUI.DrawRect(colorRect, swatch);

                    EditorGUI.BeginChangeCheck();
                    bool newVisible = EditorGUILayout.ToggleLeft(
                        $"{collection.name} ({GetCollectionBoneCount(set, collection)})",
                        visible);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetCollectionHidden(collection.name, !newVisible);
                    }
                }
            }
        }

        private HashSet<string> ReadHiddenSet()
        {
            var hidden = new HashSet<string>();
            if (targets.Length == 1 && target is HoBoneRenderer boneRenderer && boneRenderer.HiddenCollections != null)
            {
                foreach (string name in boneRenderer.HiddenCollections)
                {
                    hidden.Add(name);
                }
            }

            return hidden;
        }

        private bool HasGroupJsonToRefresh()
        {
            if (groupJson.objectReferenceValue != null)
            {
                return true;
            }

            if (!groupJson.hasMultipleDifferentValues)
            {
                return false;
            }

            foreach (Object target in targets)
            {
                if (target is HoBoneRenderer boneRenderer && boneRenderer.GroupJson != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取集合在开关列表里显示的骨骼数。显式集合直接用其 bones 长度;
        /// Other 兜底组统计当前骨架里未被任何显式集合收录的节点数(如自动生成的 end 骨)。
        /// </summary>
        private int GetCollectionBoneCount(HoBoneGroupSet set, HoBoneCollection collection)
        {
            if (!collection.isOther)
            {
                return collection.bones != null ? collection.bones.Count : 0;
            }

            if (targets.Length != 1 || !(target is HoBoneRenderer boneRenderer) || boneRenderer.Transforms == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Transform transform in boneRenderer.Transforms)
            {
                if (transform != null && !set.BelongsToAnyExplicitCollection(transform.name))
                {
                    count++;
                }
            }

            return count;
        }

        private static int GetCollectionDepth(HoBoneGroupSet set, string collectionName, Dictionary<string, int> cache)
        {
            if (cache.TryGetValue(collectionName, out int cached))
            {
                return cached;
            }

            int depth = 0;
            string current = collectionName;
            int guard = 0;
            int max = set.collections.Count + 1;
            while (guard <= max)
            {
                HoBoneCollection collection = set.FindCollection(current);
                if (collection == null || string.IsNullOrEmpty(collection.parent))
                {
                    break;
                }

                depth++;
                current = collection.parent;
                guard++;
            }

            cache[collectionName] = depth;
            return depth;
        }

        private void SetCollectionHidden(string collectionName, bool hidden)
        {
            foreach (Object target in targets)
            {
                if (!(target is HoBoneRenderer boneRenderer))
                {
                    continue;
                }

                Undo.RecordObject(boneRenderer, "切换骨骼集合可见性");
                List<string> list = boneRenderer.HiddenCollections;
                bool contains = list.Contains(collectionName);
                if (hidden && !contains)
                {
                    list.Add(collectionName);
                }
                else if (!hidden && contains)
                {
                    list.Remove(collectionName);
                }

                boneRenderer.Invalidate();
                EditorUtility.SetDirty(boneRenderer);
            }

            serializedObject.Update();
            SceneView.RepaintAll();
        }

        private void SetAllCollectionsHidden(bool hidden)
        {
            foreach (Object target in targets)
            {
                if (!(target is HoBoneRenderer boneRenderer))
                {
                    continue;
                }

                HoBoneGroupSet activeSet = boneRenderer.GetActiveGroupSet();
                if (activeSet == null)
                {
                    continue;
                }

                Undo.RecordObject(boneRenderer, "切换全部骨骼集合可见性");
                List<string> list = boneRenderer.HiddenCollections;
                list.Clear();
                if (hidden)
                {
                    foreach (HoBoneCollection collection in activeSet.collections)
                    {
                        if (collection != null && !string.IsNullOrEmpty(collection.name))
                        {
                            list.Add(collection.name);
                        }
                    }
                }

                boneRenderer.Invalidate();
                EditorUtility.SetDirty(boneRenderer);
            }

            serializedObject.Update();
            SceneView.RepaintAll();
        }

        private void RebuildAllTargets()
        {
            foreach (Object target in targets)
            {
                if (target is HoBoneRenderer boneRenderer)
                {
                    boneRenderer.Invalidate();
                    EditorUtility.SetDirty(boneRenderer);
                }
            }

            SceneView.RepaintAll();
        }

        private void RefreshAllTargets()
        {
            serializedObject.ApplyModifiedProperties();

            var importedPaths = new HashSet<string>();
            foreach (Object target in targets)
            {
                if (!(target is HoBoneRenderer boneRenderer) || boneRenderer.GroupJson == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(boneRenderer.GroupJson);
                if (!string.IsNullOrEmpty(assetPath) && importedPaths.Add(assetPath))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
            }

            foreach (Object target in targets)
            {
                if (target is HoBoneRenderer boneRenderer)
                {
                    boneRenderer.RefreshGroupJson();
                    EditorUtility.SetDirty(boneRenderer);
                }
            }

            SceneView.RepaintAll();
        }
    }
}
