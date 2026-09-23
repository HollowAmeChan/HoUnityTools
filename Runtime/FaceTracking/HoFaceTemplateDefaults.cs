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
                    + "会反复改；参考血统的对照见 docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md §6.1。",
                requiredKeysNote = "标准 ARKit 的六个眼睑键：eyeBlinkLeft/Right、eyeWideLeft/Right、eyeSquintLeft/Right。"
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

        /// <summary>
        /// 眼睑开合轴（两个 vrc 模板共用）：<c>EyeLid = 0.75 − 0.75·blink + 0.25·wide</c>，值域 0~1、**中性 0.75**。
        /// 这就是 VRCFT 上游 C# 的 `Openness*0.75 + EyeWide*0.25`（`Openness = 1 − blink`）——
        /// 值由**会话**按这个线性式生产（混合树自己算不出参数，见 19.2）。
        /// </summary>
        private static HoFaceAxisSpec EyeLidAxis(int side)
        {
            string suffix = side == 0 ? "Left" : "Right";
            return new HoFaceAxisSpec
            {
                parameter = "Ho/Drive/Lid/" + suffix + "/EyeLid",
                offset = 0.75f,
                terms = new[]
                {
                    new HoFaceAxisTerm("eyeBlink" + suffix, -0.75f),
                    new HoFaceAxisTerm("eyeWide" + suffix, 0.25f)
                }
            };
        }

        /// <summary>眯眼轴（两个 vrc 模板共用）：<c>Squint = squint</c>，0~1。</summary>
        private static HoFaceAxisSpec SquintAxis(int side, string parameterName)
        {
            string suffix = side == 0 ? "Left" : "Right";
            return new HoFaceAxisSpec
            {
                parameter = parameterName,
                offset = 0f,
                terms = new[] { new HoFaceAxisTerm("eyeSquint" + suffix, 1f) }
            };
        }

        /// <summary>
        /// **`vrc-jerry`** = VRCFT 官方模板 **ARKit 支**血统（一手取证，2026-09-23）：
        /// 每眼一根 `0..1` 眼睑轴（中性 0.75）+ 5 姿势 `FreeformCartesian2D`（含 squint）。
        /// 片段名沿用参考资产的（这样能和参考控制器逐格对照）；数值是实测的 0~100 绝对值。
        /// </summary>
        public static HoFaceTemplateSpec VrcJerry()
        {
            var spec = new HoFaceTemplateSpec
            {
                displayName = "vrc-jerry（VRCFT 官方模板 · ARKit 支）",
                notes = "出处：VRCFaceTracking-Templates/Packages/adjerry91.vrcft.templates/Animators/ARkit Blendshapes/"
                    + "FX - Face Tracking - ARKit Blendshapes.controller —— 树 `Left Eye Lid Blend` L4297、"
                    + "`Right Eye Lid Blend` L10898（m_BlendType 3 = FreeformCartesian2D，Min 0 / Max 0.625）。"
                    + "轴值由上游 C# 给：UnifiedExpressionsParameters.cs L57-58 `EyeLid = Openness*0.75 + EyeWide*0.25`。"
                    + "注意：参考的 5 个点里**没有** wide=100+squint=100 那个组合（y=1 上两点的 blink 是 90 与 0、"
                    + "wide 都是 0），那个组合只能由 Cartesian 插值得到。",
                requiredKeysNote = "仅标准 ARKit 的六个眼睑键：eyeBlinkLeft/Right、eyeWideLeft/Right、eyeSquintLeft/Right。"
            };

            var trees = new HoFaceTreeSpec[2];
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "Left" : "Right";
                var poses = new[]
                {
                    Pose("Eye_Lid_Blink_" + suffix, 0f, 0f, suffix, 100f, 0f, 0f),
                    Pose("Eye_Lid_Neutral_" + suffix, 0.75f, 0f, suffix, 0f, 0f, 0f),
                    Pose("Eye_Lid_Wide_" + suffix, 1f, 0f, suffix, 0f, 100f, 0f),
                    Pose("Eye_Lid_Squint_" + suffix, 0.25f, 1f, suffix, 90f, 0f, 100f),
                    Pose("Eye_Open_Squint_" + suffix, 0.75f, 1f, suffix, 0f, 0f, 100f)
                };

                trees[side] = new HoFaceTreeSpec
                {
                    name = HoFaceNaming.LidTree(side),
                    kind = HoFaceTreeKind.FreeformCartesian2D,
                    x = EyeLidAxis(side),
                    y = SquintAxis(side, "Ho/Drive/Lid/" + suffix + "/Squint"),
                    poses = poses
                };
            }

            spec.trees = trees;
            return spec;
        }

        /// <summary>
        /// **`vrc-common`** = 跨血统共同核：每眼一根 `0..1`（中性 0.7–0.8 平台）、**`Simple1D` 平台式**，
        /// squint 是**独立的一根 1D 轴**（JINGO 系与 kipfel 自有层都这么做）。
        ///
        /// 出处（一手）：JINGO 系 Shinano `FX_FT_added 2.controller` 树 `Left Eye Lid` L1750（阈值 0/0.7/0.8/1）、
        /// 旁轴 `Eye Squint Left` L2112；Mafuyu `FxLayer_Mafuyu_FT.controller` `Eye Lid Left` L19669；Manuka 同构。
        /// **JINGO 的姿势写的是模型专有键**，所以本模板的格子里**照实写上了它们** —— 于是"需要哪些键"是真话，
        /// 缺键时面板会报出来（而不是静默少一半）。
        /// </summary>
        public static HoFaceTemplateSpec VrcCommon()
        {
            var spec = new HoFaceTemplateSpec
            {
                displayName = "vrc-common（JINGO 系 Simple1D 平台 + kipfel 共同核）",
                notes = "出处（一手）：JINGO 系三套（Shinano / Mafuyu / Manuka，共享动画 GUID ⇒ 同一血统）"
                    + "把眼睑做成每眼一根 Simple1D `0..1`、中性片段同时钉在 0.7 与 0.8 两个阈值上（0.75 落在平台正中）；"
                    + "`闭`那一端在原血统里是嵌套 1D 子树（由苦脸/笑脸选 EyeClosed 还是 EyeClosedJoyful），"
                    + "单根轴上它的语义就是「闭」。旁轴眯眼是**独立一根 1D**（0.15 → 1）。"
                    + "⚠️ 我们的三键表达不了原血统的 EyeClosedJoyful*（笑闭）、EyeDilation*/EyeIrisSmall*/EyePupilSmall*"
                    + "（瞳孔虹膜）、BrowLowerer*（眉压低）—— 这些键照实写在格子里，网格上没有就会被跳过（面板会报缺键），"
                    + "**而「笑闭 / 瞳孔 / 眉压」这三件语义在我们这套键里没有落点**。",
                requiredKeysNote = "标准 ARKit 六个眼睑键 + JINGO 系专有键："
                    + "EyeClosedLeft/Right、EyeWideLeft/Right、EyeSquintLeft/Right、EyeClosedJoyfulLeft/Right、"
                    + "EyeDilationLeft/Right、EyeIrisSmallLeft/Right、BrowLowererLeft/Right。"
            };

            var trees = new HoFaceTreeSpec[4];
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "Left" : "Right";
                string lidParameter = "Ho/Drive/Lid/" + suffix + "/EyeLid";

                // 眨眼轴：Simple1D 平台式 —— 中性在 0.7 与 0.8 两个阈值上（同一档）。
                var lidPoses = new[]
                {
                    CommonPose("EyeClosed_" + suffix, 0f, suffix, 100f, 0f, 0f, eyeClosed: 100f, browLowerer: 100f),
                    CommonPose("EyeClosedNeutral_" + suffix, 0.7f, suffix, 0f, 0f, 0f, eyeClosed: 0f, eyeWide: 0f),
                    CommonPose("EyeClosedNeutral_" + suffix + " (platform)", 0.8f, suffix, 0f, 0f, 0f, eyeClosed: 0f, eyeWide: 0f),
                    CommonPose("EyeWide_" + suffix, 1f, suffix, 0f, 100f, 0f, eyeWide: 100f,
                        eyeDilation: 50f, eyeIrisSmall: 25f)
                };
                trees[side] = new HoFaceTreeSpec
                {
                    name = HoFaceNaming.LidTree(side) + "Common",
                    kind = HoFaceTreeKind.Simple1D,
                    x = EyeLidAxis(side),
                    poses = lidPoses
                };

                // 眯眼轴：独立一根 1D（原血统 0.15 → 1；1 端同时给 EyeClosed 5）。
                string squintParameter = "Ho/Drive/Lid/" + suffix + "/Squint";
                trees[2 + side] = new HoFaceTreeSpec
                {
                    name = HoFaceNaming.LidTree(side) + "CommonSquint",
                    kind = HoFaceTreeKind.Simple1D,
                    x = SquintAxis(side, squintParameter),
                    poses = new[]
                    {
                        CommonPose("EyeSquint_" + suffix, 0.15f, suffix, 0f, 0f, 0f, eyeSquint: 0f),
                        CommonPose("EyeSquint_" + suffix + " 1", 1f, suffix, 5f, 0f, 100f, eyeSquint: 100f, eyeClosed: 5f)
                    }
                };
            }

            spec.trees = trees;
            return spec;
        }

        /// <summary>一个格子：坐标（2D）或阈值（1D）+ 三个 ARKit 键的值 + 可选的参考血统专有键。</summary>
        private static HoFacePoseSpec Pose(string clipName, float x, float y, string suffix,
            float blink, float wide, float squint)
        {
            return new HoFacePoseSpec
            {
                clipName = clipName,
                position = new Vector2(x, y),
                values = new[]
                {
                    new HoFacePoseValue("eyeBlink" + suffix, blink),
                    new HoFacePoseValue("eyeWide" + suffix, wide),
                    new HoFacePoseValue("eyeSquint" + suffix, squint)
                }
            };
        }

        /// <summary>
        /// `vrc-common` 的格子：写 ARKit 三键，**同时照实写原血统那些专有键**（没有就跳过、面板会报）。
        /// 专有键只在语义需要时给值，其余留空以免无谓地要求网格存在它们。
        /// </summary>
        private static HoFacePoseSpec CommonPose(string clipName, float threshold, string suffix,
            float blink, float wide, float squint,
            float eyeClosed = -1f, float eyeWide = -1f, float eyeSquint = -1f,
            float eyeDilation = -1f, float eyeIrisSmall = -1f, float browLowerer = -1f)
        {
            var values = new System.Collections.Generic.List<HoFacePoseValue>
            {
                new HoFacePoseValue("eyeBlink" + suffix, blink),
                new HoFacePoseValue("eyeWide" + suffix, wide),
                new HoFacePoseValue("eyeSquint" + suffix, squint)
            };

            if (eyeClosed >= 0f) values.Add(new HoFacePoseValue("EyeClosed" + suffix, eyeClosed));
            if (eyeWide >= 0f) values.Add(new HoFacePoseValue("EyeWide" + suffix, eyeWide));
            if (eyeSquint >= 0f) values.Add(new HoFacePoseValue("EyeSquint" + suffix, eyeSquint));
            if (eyeDilation >= 0f) values.Add(new HoFacePoseValue("EyeDilation" + suffix, eyeDilation));
            if (eyeIrisSmall >= 0f) values.Add(new HoFacePoseValue("EyeIrisSmall" + suffix, eyeIrisSmall));
            if (browLowerer >= 0f) values.Add(new HoFacePoseValue("BrowLowerer" + suffix, browLowerer));

            return new HoFacePoseSpec { clipName = clipName, threshold = threshold, values = values.ToArray() };
        }
    }
}
