using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 一根轴怎么来：<c>值 = offset + Σ 权重 × 通道值</c>。
    ///
    /// **线性是有意的边界**：成熟实现里这几根轴（眼睑、下颌左右、嘴角欣悲）确实都是线性的，
    /// 而且"混合树自己算不出参数"这条（见文档 19.2）本来就要求值在树外面算。
    /// 非线性的一律**不进来**（`MouthClosed = min(MouthClosed, JawOpen)` 那种留在会话的 C# 里），
    /// 这条边界不许以后悄悄扩大 —— 一旦扩大，模板就不再是"数据"而是"一门语言"。
    /// </summary>
    [Serializable]
    public sealed class HoFaceAxisSpec
    {
        [Tooltip("写进控制器的参数名。")]
        public string parameter;
        [Tooltip("常数项。例如 VRCFT 的眼睑轴 EyeLid = 0.75 − 0.75·blink + 0.25·wide，常数就是 0.75。")]
        public float offset;
        public HoFaceAxisTerm[] terms = new HoFaceAxisTerm[0];

        /// <summary><paramref name="channel"/> 按通道名取当前值（0~1；取不到给 0）。</summary>
        public float Evaluate(Func<string, float> channel)
        {
            if (channel == null) return offset;
            float value = offset;
            for (int i = 0; i < terms.Length; i++)
                value += terms[i].weight * channel(terms[i].channel);
            return value;
        }
    }

    /// <summary>轴里的一项：某个通道乘多少权重。</summary>
    [Serializable]
    public struct HoFaceAxisTerm
    {
        public string channel;
        public float weight;

        public HoFaceAxisTerm(string channel, float weight)
        {
            this.channel = channel;
            this.weight = weight;
        }
    }

    public enum HoFaceTreeKind
    {
        [InspectorName("2D 自由笛卡尔（两轴）")] FreeformCartesian2D,
        [InspectorName("1D 单轴（阈值）")] Simple1D
    }

    /// <summary>一个姿势里"某个形态键 = 多少"。</summary>
    [Serializable]
    public struct HoFacePoseValue
    {
        public string shape;
        public float value;

        public HoFacePoseValue(string shape, float value)
        {
            this.shape = shape;
            this.value = value;
        }
    }

    /// <summary>
    /// 一个格子：**名字自带**（表里写什么就是什么，不发明命名模式语言）、坐标或阈值、
    /// 以及这一格写满哪些键。
    /// </summary>
    [Serializable]
    public sealed class HoFacePoseSpec
    {
        public string clipName;
        [Tooltip("2D 树用：这一格在两条轴上的坐标。")]
        public Vector2 position;
        [Tooltip("1D 树用：这一格的阈值。")]
        public float threshold;
        [Tooltip("这一格写满的全部键（没摆的也要写 0 —— 混合树对权重做归一化，只在部分格里有曲线的键会失去权重基准）。")]
        public HoFacePoseValue[] values = new HoFacePoseValue[0];
    }

    /// <summary>一棵树：名字、类型、两根轴、以及全部格子。</summary>
    [Serializable]
    public sealed class HoFaceTreeSpec
    {
        public string name;
        public HoFaceTreeKind kind = HoFaceTreeKind.FreeformCartesian2D;
        public HoFaceAxisSpec x = new HoFaceAxisSpec();
        [Tooltip("2D 树才用；1D 树留空。")]
        public HoFaceAxisSpec y = new HoFaceAxisSpec();
        public HoFacePoseSpec[] poses = new HoFacePoseSpec[0];
    }

    /// <summary>
    /// 一份模板：**把"通道怎么合成轴、轴上有哪些格子、每格写什么"整包描述出来**。
    /// 生成器只负责解释它（建参数 / 建树 / 建片段），自己不含任何模板数字。
    /// </summary>
    [Serializable]
    public sealed class HoFaceTemplateSpec
    {
        public string displayName;
        [Tooltip("这个模板的来历与出处（参考血统的资产路径 + 行号）—— 写在这里，下一个人能核对。")]
        public string notes;

        [Tooltip("这个模板需要网格上存在哪些键。**必须写明血统**，例如「标准 ARKit 52 键」或"
            + "「标准 ARKit + JINGO 系的 EyeClosedJoyful* / EyeDilation* / EyeIrisSmall* / BrowLowerer*」。"
            + "为什么非要写：缺键时那几格里的这些键**会被静默跳过**，模板看起来生效了、其实少了一半。")]
        public string requiredKeysNote;

        public HoFaceTreeSpec[] trees = new HoFaceTreeSpec[0];

        /// <summary>
        /// 这个模板实际用到的全部键（去重）—— 由格子表推出来，用来跟网格核对"缺哪些"。
        /// 与 <see cref="requiredKeysNote"/> 的分工：note 写给人看血统，这里给面板/校验做差集。
        /// </summary>
        public IEnumerable<string> UsedKeys()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                if (tree?.poses == null) continue;
                foreach (var pose in tree.poses)
                {
                    if (pose?.values == null) continue;
                    foreach (var value in pose.values)
                        if (!string.IsNullOrEmpty(value.shape) && seen.Add(value.shape)) yield return value.shape;
                }
            }
        }
    }

    /// <summary>
    /// 模板资产。**组件上存的是它的引用，不是枚举** —— 这样"加一个模板"就是"加一个文件"，
    /// 不用改代码（`ho-2d-test1` 这种要反复改的实验品尤其需要这一点）。
    /// </summary>
    [CreateAssetMenu(menuName = "HoUnityTools/Face Tracking/面捕混合树模板", fileName = "HoFaceTemplate")]
    public sealed class HoFaceTemplate : ScriptableObject
    {
        public HoFaceTemplateSpec spec = new HoFaceTemplateSpec();
    }
}
