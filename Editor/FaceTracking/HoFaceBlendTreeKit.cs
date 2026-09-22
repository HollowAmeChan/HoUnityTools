using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>姿势里的一个目标：哪张网格的哪个形态键。</summary>
    public struct HoPoseKey
    {
        public SkinnedMeshRenderer mesh;
        public string shape;

        public HoPoseKey(SkinnedMeshRenderer mesh, string shape)
        {
            this.mesh = mesh;
            this.shape = shape;
        }
    }

    /// <summary>
    /// 混合树的**基础件**：机械到不值得手点的部分。
    ///
    /// **它不替用户做设计。** 按 19 节的定案，混合树由用户手摆姿势 —— 这里只负责
    /// "两条轴上 N×M 个格子就建 N×M 个片段、摆好坐标"这类纯体力活，
    /// 以及"双向轴的标准三姿势 1D 树"（值由 <c>HoFaceAxis</c> 出）。
    ///
    /// **规矩：每个格子都写满全部键**（没摆的写 0）。混合树对权重做归一化，某个键只在部分
    /// 格子里有曲线，等于让它在其余格子上失去权重基准 —— 表现是"参数走到某些角落这个键莫名消失"。
    /// 所以这里由 Kit 统一预写 0，调用方只覆盖要摆的那些。
    /// </summary>
    public static class HoFaceBlendTreeKit
    {
        /// <summary>双向轴的标准三档。</summary>
        public static readonly float[] ThreePointAxis = { -1f, 0f, 1f };

        /// <summary>
        /// N×M 的 2D FreeformCartesian 树（两条轴长度可以不同）。
        /// <paramref name="pose"/> 的参数是（片段, 第一条轴的下标, 第二条轴的下标）。
        /// </summary>
        public static BlendTree Grid2D(AnimatorController controller, Animator animator, string name,
            string parameterX, string parameterY,
            IList<float> axisX, IList<float> axisY,
            IList<HoPoseKey> keys, Action<AnimationClip, int, int> pose)
        {
            if (controller == null) throw new InvalidOperationException("先指定控制器。");
            if (string.IsNullOrWhiteSpace(parameterX) || string.IsNullOrWhiteSpace(parameterY))
                throw new InvalidOperationException("两个参数名都要给。");
            if (axisX == null || axisX.Count == 0 || axisY == null || axisY.Count == 0)
                throw new InvalidOperationException("两条轴都要至少一个位置。");

            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = parameterX,
                blendParameterY = parameterY
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            for (int i = 0; i < axisX.Count; i++)
            {
                for (int j = 0; j < axisY.Count; j++)
                {
                    var clip = NewPose(controller, name + " " + i + "x" + j);
                    Blank(clip, animator, keys);
                    pose?.Invoke(clip, i, j);
                    tree.AddChild(clip, new Vector2(axisX[i], axisY[j]));
                }
            }

            return tree;
        }

        /// <summary>一根轴上的 1D 树。双向轴就是 <see cref="ThreePointAxis"/>。</summary>
        public static BlendTree Axis1D(AnimatorController controller, Animator animator, string name, string parameter,
            IList<float> axis, IList<HoPoseKey> keys, Action<AnimationClip, int> pose)
        {
            if (controller == null) throw new InvalidOperationException("先指定控制器。");
            if (string.IsNullOrWhiteSpace(parameter)) throw new InvalidOperationException("参数名不能为空。");
            if (axis == null || axis.Count == 0) throw new InvalidOperationException("轴至少一个位置。");

            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                minThreshold = axis[0],
                maxThreshold = axis[axis.Count - 1],
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            for (int i = 0; i < axis.Count; i++)
            {
                var clip = NewPose(controller, name + " " + i);
                Blank(clip, animator, keys);
                pose?.Invoke(clip, i);
                tree.AddChild(clip, axis[i]);
            }

            return tree;
        }

        /// <summary>
        /// 删掉一棵树和它的姿势片段。**重跑前先清**，否则子资产会越堆越多
        /// （树本身和片段都是控制器的子资产，不会自己消失）。
        /// </summary>
        public static void Clear(BlendTree tree)
        {
            if (tree == null) return;
            var clips = new HashSet<AnimationClip>();
            foreach (var child in tree.children)
                if (child.motion is AnimationClip clip) clips.Add(clip);
            foreach (var clip in clips) UnityEngine.Object.DestroyImmediate(clip, true);
            UnityEngine.Object.DestroyImmediate(tree, true);
        }

        /// <summary>把一个姿势写进片段：这张网格的这个形态键 = 这个权重（0~100）。</summary>
        public static void Pose(AnimationClip clip, Animator animator, SkinnedMeshRenderer mesh, string shape, float weight)
        {
            if (clip == null) throw new InvalidOperationException("片段为空。");
            if (animator == null) throw new InvalidOperationException("需要 Animator 来算绑定路径。");
            if (mesh == null || mesh.sharedMesh == null) throw new InvalidOperationException("网格或 Mesh 为空。");
            if (mesh.GetComponentInParent<Animator>() != animator)
                throw new InvalidOperationException("网格不在这个 Animator 下，曲线绑不上：" + mesh.name);
            if (mesh.sharedMesh.GetBlendShapeIndex(shape) < 0)
                throw new InvalidOperationException(mesh.name + " 上没有形态键：" + shape);

            AnimationUtility.SetEditorCurve(clip, Binding(mesh, animator, shape),
                AnimationCurve.Constant(0f, 1f / 60f, Mathf.Clamp(weight, 0f, 100f)));
        }

        private static AnimationClip NewPose(AnimatorController controller, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        /// <summary>每个键先写 0：给归一化一个完整的权重基准，见类型注释。</summary>
        private static void Blank(AnimationClip clip, Animator animator, IList<HoPoseKey> keys)
        {
            if (keys == null) return;
            foreach (var key in keys)
            {
                if (key.mesh == null || key.mesh.sharedMesh == null) continue;
                if (key.mesh.sharedMesh.GetBlendShapeIndex(key.shape) < 0) continue;
                if (animator != null && key.mesh.GetComponentInParent<Animator>() != animator) continue;
                AnimationUtility.SetEditorCurve(clip,
                    Binding(key.mesh, animator, key.shape), AnimationCurve.Constant(0f, 1f / 60f, 0f));
            }
        }

        private static EditorCurveBinding Binding(SkinnedMeshRenderer mesh, Animator animator, string shape)
        {
            Transform root = animator != null ? animator.transform : mesh.transform.root;
            return EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(mesh.transform, root),
                typeof(SkinnedMeshRenderer), "blendShape." + shape);
        }
    }
}
