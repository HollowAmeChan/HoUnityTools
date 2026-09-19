namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 我们这一路（一个约束里的所有目标）的贡献怎么合并进形态键里。
    ///
    /// 背景：一个键上可能同时有"外部写者"（动画/面捕/VRChat 的表情）和我们自己的多路目标
    /// （眨眼的双眼输出 + 眼球高光 + 注视的眼动通道……）。写入器把它们求和，
    /// 而形态键的合法区间是 0..100 —— 求和超过 100 时怎么处理就是这里的选择。
    /// 老行为是直接夹断（<see cref="Saturate"/>），结果是"特别容易写死 100"：
    /// 果冻的超调、多路叠加的比例、基准之上的余量全部被抹平成一条直线。
    /// </summary>
    public enum HoShapeKeyMergeMode
    {
        /// <summary>求和后夹到 0..100。单个驱动到顶就是满值 —— 想要"眨眼就该闭到底"时用这个。</summary>
        Saturate,

        /// <summary>
        /// 80 以上做软压缩，永远达不到 100（请求正好 100 时约 93）。
        /// 果冻超调、多路叠加的差别都保留下来了，代价是满值不再是真的 100。
        /// </summary>
        SoftClip,

        /// <summary>
        /// 多路求和超过剩余空间时，各路按同一比例缩小（比例关系不变），不夹断任何一路。
        /// 适合"两路都在出力，谁也别把谁挤掉"的场合（比如眨眼 + 表情同时压同一个键）。
        /// </summary>
        Normalize
    }

    /// <summary>被写满 / 被削过的键（调试用，只在编辑器里采集）。</summary>
    public struct HoShapeKeySaturation
    {
        /// <summary>网格名。</summary>
        public string MeshName;

        /// <summary>键名。</summary>
        public string KeyName;

        /// <summary>外部基准（不是我们写的值）。</summary>
        public float Base;

        /// <summary>基准 + 我们请求的原始总和（合并之前）。</summary>
        public float Request;

        /// <summary>实际写出去的值。</summary>
        public float Final;

        /// <summary>我们这一路被合并策略削过（Request 与 Final 对不上）。</summary>
        public bool Clipped;
    }
}
