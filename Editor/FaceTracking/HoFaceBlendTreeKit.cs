using System;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 混合树的**基础件**：把模板里的一棵树（<see cref="HoFaceTreeSpec"/>）落成控制器里的真树 + 片段。
    ///
    /// **它不含任何模板数字** —— 树名、两根轴的参数名、每个格子的坐标/阈值、每格写哪些键与多少，
    /// 全部来自模板；这里只负责"创建（或复用）片段、摆坐标、接上树"这类纯体力活。
    /// 所以"加一个模板"就是"加一个文件"，不用改这里，也不用改生成器。
    ///
    /// **规矩（模板作者要守）**：每一格都写满这棵树用到的**全部**键。混合树对权重做归一化，
    /// 某个键只在部分格里有曲线，等于让它在其余格上失去权重基准 —— 表现是"参数走到某些角落
    /// 这个键莫名消失"。这里**不静默修补**（修补会掩盖模板的错误），由模板校验器去报。
    ///
    /// 历史：这里曾有两件为"手搓调用方"写的工具（`Grid2D` 铺满笛卡尔积、`Axis1D` 三姿势单轴）。
    /// 模板化之后它们被这个入口取代 —— `Grid2D` 表达不了参考模板那种**非积分布**的姿势表
    /// （例如眼睑 5 姿势里的 `(0.25,1)`），留两个入口只会分叉。
    /// </summary>
    public static class HoFaceBlendTreeKit
    {
        /// <summary>
        /// 按模板建一棵树。<paramref name="obtain"/> 用来复用已存在的片段（幂等：反复应用不该每次
        /// 新建一堆子资产），返回 null 就新建；复用的片段会先被清空曲线。<paramref name="fill"/>
        /// 由调用方负责把这一格的键值写进片段（顺便把片段收进自己的清理白名单）。
        /// </summary>
        public static BlendTree Tree(AnimatorController controller, Animator animator, HoFaceTreeSpec spec,
            Func<string, AnimationClip> obtain, Action<AnimationClip, HoFacePoseSpec> fill)
        {
            if (controller == null) throw new InvalidOperationException("先指定控制器。");
            if (spec == null) throw new InvalidOperationException("模板里这棵树是空的。");
            if (string.IsNullOrWhiteSpace(spec.name)) throw new InvalidOperationException("树名不能为空。");
            if (spec.poses == null || spec.poses.Length == 0)
                throw new InvalidOperationException("模板里这棵树一个格子都没有：" + spec.name);
            if (spec.x == null || string.IsNullOrWhiteSpace(spec.x.parameter))
                throw new InvalidOperationException("X 轴的参数名没给：" + spec.name);

            bool twoDimensional = spec.kind == HoFaceTreeKind.FreeformCartesian2D;
            if (twoDimensional && (spec.y == null || string.IsNullOrWhiteSpace(spec.y.parameter)))
                throw new InvalidOperationException("2D 树的 Y 轴参数名没给：" + spec.name);

            var tree = new BlendTree
            {
                name = spec.name,
                blendType = twoDimensional ? BlendTreeType.FreeformCartesian2D : BlendTreeType.Simple1D,
                blendParameter = spec.x.parameter
            };

            if (twoDimensional)
            {
                tree.blendParameterY = spec.y.parameter;
            }
            else
            {
                float min = spec.poses[0].threshold;
                float max = spec.poses[0].threshold;
                for (int i = 1; i < spec.poses.Length; i++)
                {
                    min = Mathf.Min(min, spec.poses[i].threshold);
                    max = Mathf.Max(max, spec.poses[i].threshold);
                }

                tree.useAutomaticThresholds = false;
                tree.minThreshold = min;
                tree.maxThreshold = max;
            }

            AssetDatabase.AddObjectToAsset(tree, controller);

            foreach (var pose in spec.poses)
            {
                var clip = obtain != null ? obtain(pose.clipName) : null;
                if (clip == null)
                {
                    clip = new AnimationClip { name = pose.clipName, frameRate = 60f };
                    AssetDatabase.AddObjectToAsset(clip, controller);
                }
                else
                {
                    ClearCurves(clip);
                }

                fill?.Invoke(clip, pose);
                if (twoDimensional) tree.AddChild(clip, pose.position);
                else tree.AddChild(clip, pose.threshold);
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
            var clips = new System.Collections.Generic.HashSet<AnimationClip>();
            foreach (var child in tree.children)
                if (child.motion is AnimationClip clip) clips.Add(clip);
            foreach (var clip in clips) UnityEngine.Object.DestroyImmediate(clip, true);
            UnityEngine.Object.DestroyImmediate(tree, true);
        }

        /// <summary>复用一个片段：先把曲线清空（重置成"什么都没摆"），再交给调用方重写。</summary>
        private static void ClearCurves(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                AnimationUtility.SetEditorCurve(clip, binding, null);
        }
    }
}
