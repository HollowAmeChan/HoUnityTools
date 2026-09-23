using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
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

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            int total = 0, missing = 0;
            Audit(peek.controller, go.transform, ref total, ref missing);
            Debug.Log("[Ho 混合树观察台] controller = " + (peek.controller != null ? peek.controller.name : "（空，自己拖一个进来）")
                + "；曲线绑定 " + total + " 条，其中 " + missing + " 条在这个空物体上解析不到。"
                + " Play 后选中它、打开 Animator 窗口，并留意 Console 有没有因为缺绑定而报错。");
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
