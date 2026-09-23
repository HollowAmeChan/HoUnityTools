using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 内置默认模板：**`ho-2d-test1`** —— 我们自己的实验品（对齐 Live2D 那种"把会重叠的语义放进
    /// 同一张 2D 表"的做法）。它现在就是生成器的产出，所以迁移期间**必须与迁移前逐字节一致**（用例守着）。
    ///
    /// ⚠️ **它是实验品，会改很多次** —— 这正是把模板做成数据（而不是写死在生成器里）的理由。
    /// 要改它，优先复制成模板资产再改；这份代码默认是"没选模板时的兜底"。
    /// </summary>
    public static class HoFaceTemplateDefaults
    {
        /// <summary>眼睑 A3 方阵：开合轴 ±1（−1 睁大 / +1 闭）× 眯眼轴 0~1，六格。</summary>
        public static HoFaceTemplateSpec TwoDTest1()
        {
            var spec = new HoFaceTemplateSpec
            {
                displayName = "ho-2d-test1（我们的 A3 方阵实验）",
                notes = "自研实验模板：眼睑用两轴 2D 表（开合 × 眯眼），中性在方阵中线。"
                    + "会反复改；参考血统的对照见 docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md §6.1。"
            };

            var trees = new HoFaceTreeSpec[2];
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "Left" : "Right";
                var x = new HoFaceAxisSpec
                {
                    parameter = HoFaceNaming.LidAxis(side, true),
                    offset = 0f,
                    terms = new[]
                    {
                        new HoFaceAxisTerm("eyeBlink" + suffix, 1f),
                        new HoFaceAxisTerm("eyeWide" + suffix, -1f)
                    }
                };
                var y = new HoFaceAxisSpec
                {
                    parameter = HoFaceNaming.LidAxis(side, false),
                    offset = 0f,
                    terms = new[] { new HoFaceAxisTerm("eyeSquint" + suffix, 1f) }
                };

                // 摆了的六格：LidPoses[X 刻度 0..2, 行 0..1]；行 0 = Y0（不眯）、行 1 = Y2（眯满）。
                // 中间那行（Y1 半眯）没摆 —— 空格交给树插值。
                var values = new (float Blink, float Wide, float Squint)[,]
                {
                    { (0f, 100f, 0f), (0f, 100f, 100f) },   // X0：睁大 / 睁大+眯
                    { (0f, 0f, 0f), (90f, 0f, 100f) },      // X1：中性 / 眯（眯自带 blink 90）
                    { (100f, 0f, 0f), (100f, 0f, 0f) }      // X2：闭 / 闭+眯
                };

                var poses = new HoFacePoseSpec[6];
                int index = 0;
                for (int row = 0; row < 2; row++)
                {
                    int step = row == 0 ? 0 : 2;             // 第 1 行是 Y2（名字必须说真话）
                    for (int x0 = 0; x0 < 3; x0++)
                    {
                        poses[index++] = new HoFacePoseSpec
                        {
                            clipName = HoFaceNaming.LidCell(side, x0, step),
                            position = HoFaceNaming.LidPosition(x0, step),
                            values = new[]
                            {
                                new HoFacePoseValue("eyeBlink" + suffix, values[x0, row].Blink),
                                new HoFacePoseValue("eyeWide" + suffix, values[x0, row].Wide),
                                new HoFacePoseValue("eyeSquint" + suffix, values[x0, row].Squint)
                            }
                        };
                    }
                }

                trees[side] = new HoFaceTreeSpec
                {
                    name = HoFaceNaming.LidTree(side),
                    kind = HoFaceTreeKind.FreeformCartesian2D,
                    x = x,
                    y = y,
                    poses = poses
                };
            }

            spec.trees = trees;
            return spec;
        }
    }
}
