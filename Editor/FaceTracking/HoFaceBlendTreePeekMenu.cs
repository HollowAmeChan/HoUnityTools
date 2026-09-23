using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 菜单入口：一键建出"混合树观察台"（道具本体与它要回答的四个问题见
    /// <see cref="HoFaceBlendTreePeek"/> 的注释）。
    ///
    /// 顺手做一次**曲线绑定审计**：本物体是个空物体，controller 里的曲线路径基本都解析不到 ——
    /// 审计把"有多少条缺绑定"报出来，正好对上第 3 条问题（缺绑定会不会让 Console 报错/警告）。
    /// </summary>
    internal static class HoFaceBlendTreePeekMenu
    {
        private const string MenuPath = "HoUnityTools/混合树观察台（调试）";

        [MenuItem(MenuPath, false, 42)]
        private static void Create()
        {
            var go = new GameObject("Ho 混合树观察台");
            Undo.RegisterCreatedObjectUndo(go, "创建混合树观察台");
            var animator = go.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var peek = go.AddComponent<HoFaceBlendTreePeek>();
            peek.controller = FindFaceController();

            // **不靠猜参数名**：直接从 controller 里找出那棵 2D 树，用它自己的两根混合参数。
            // 这样改过名（Ho/Drive/Lid/… ← Ho/LidLeft.X）也不会退化成"喂了别的通道、红点不动"。
            string treeName = null, x = null, y = null;
            if (peek.controller is AnimatorController asset && TryFind2DTree(asset, ref x, ref y, ref treeName))
            {
                peek.parameterX = x;
                peek.parameterY = y;
            }
            else
            {
                Debug.LogWarning("[Ho 混合树观察台] 没在这个 controller 里找到带两根参数的 2D 混合树，"
                    + "保留默认参数名（" + peek.parameterX + " / " + peek.parameterY + "），自己核对一下。");
            }

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            int total = 0, missing = 0;
            Audit(peek.controller, go.transform, ref total, ref missing);
            Debug.Log("[Ho 混合树观察台] controller = " + (peek.controller != null ? peek.controller.name : "（空）")
                + "；曲线绑定 " + total + " 条，其中 " + missing + " 条在这个空物体上解析不到。"
                + (treeName != null ? " 找到的 2D 树：" + treeName + "（" + x + " / " + y + "，只在正弦模式下用到）。" : string.Empty)
                + " 默认是「跟随正在生效的会话」模式：Play 并把面捕驱动起来，选中本物体看 Animator 窗口。");
        }

        /// <summary>在控制器里找一棵"两根混合参数都非空"的 2D 树（我们生成的 LidL / LidR 就是）。</summary>
        private static bool TryFind2DTree(AnimatorController controller, ref string x, ref string y, ref string treeName)
        {
            foreach (var layer in controller.layers)
                if (TryFind2DTree(layer.stateMachine, ref x, ref y, ref treeName))
                    return true;
            return false;
        }

        private static bool TryFind2DTree(AnimatorStateMachine machine, ref string x, ref string y, ref string treeName)
        {
            if (machine == null) return false;
            foreach (var child in machine.states)
                if (TryFind2DTree(child.state.motion as BlendTree, ref x, ref y, ref treeName))
                    return true;
            foreach (var sub in machine.stateMachines)
                if (TryFind2DTree(sub.stateMachine, ref x, ref y, ref treeName))
                    return true;
            return false;
        }

        private static bool TryFind2DTree(BlendTree tree, ref string x, ref string y, ref string treeName)
        {
            if (tree == null) return false;
            bool twoDimensional = tree.blendType == BlendTreeType.FreeformCartesian2D
                || tree.blendType == BlendTreeType.FreeformDirectional2D
                || tree.blendType == BlendTreeType.SimpleDirectional2D;
            if (twoDimensional && !string.IsNullOrEmpty(tree.blendParameter) && !string.IsNullOrEmpty(tree.blendParameterY))
            {
                x = tree.blendParameter;
                y = tree.blendParameterY;
                treeName = tree.name;
                return true;
            }

            foreach (var child in tree.children)
                if (TryFind2DTree(child.motion as BlendTree, ref x, ref y, ref treeName))
                    return true;
            return false;
        }

        /// <summary>优先用选中的面捕组件的控制器，其次是场景里任意一个面捕组件。</summary>
        private static RuntimeAnimatorController FindFaceController()
        {
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                var rig = selected.GetComponentInParent<HoFaceTrackingDebugger>();
                if (rig != null && rig.faceController != null) return rig.faceController;
            }

            foreach (var rig in Object.FindObjectsByType<HoFaceTrackingDebugger>(FindObjectsSortMode.None))
                if (rig.faceController != null) return rig.faceController;
            return null;
        }

        /// <summary>数一下 controller 里的曲线有多少条在给定根上解析不到（空物体上几乎全部）。</summary>
        private static void Audit(RuntimeAnimatorController controller, Transform root, ref int total, ref int missing)
        {
            if (controller == null) return;
            foreach (var clip in controller.animationClips)
            {
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    total++;
                    if (string.IsNullOrEmpty(binding.path)) continue;
                    if (root.Find(binding.path) == null) missing++;
                }
            }
        }
    }
}
