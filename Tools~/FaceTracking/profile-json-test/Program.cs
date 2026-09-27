// Offline round-trip test for the middle-layer profile JSON codec.
//
// Why this exists: Unity's JsonUtility silently drops the two List<inner-class> fields
// (inputs / outputs) in a player -- measured on the real thing, in Warudo's Player.log:
//   "配置文件里一行输出都没有。"  while the file on disk clearly had 56 output rows.
// The editor compiles it fine, so the Unity test suite could never catch this.
// This harness runs the *actual* package sources with a UnityEngine stub, so the codec is
// verified (write -> read -> write byte-stable) without waiting for a mod build.
//
// Run:  dotnet run --project D:\Unity_Fork\HoUnityTools\Tools~\FaceTracking\profile-json-test
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Hollow.HoUnityTools.FaceTracking;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        WriteContainsBothArrays();
        FixtureRoundTrip();
        RowContentIsFaithful();
        CurveRoundTrip();
        ModifierRoundTrip();
        WriteReadWriteIsStable();
        UnknownFieldIsSkipped();
        UnknownModifierKindIsDroppedAndReported();
        MissingOutputsIsFatal();
        WrongFormatIsFatal();
        EscapesAndUnicodeRoundTrip();
        BracketInStringDoesNotConfuseTheParser();
        BrokenJsonReportsPosition();

        // 包里那份发货中间层（必须真能读进来、而且 52 个规范名与 catalog 对得上）
        ShippedProfileMatchesTheCatalog();

        // 一份设备一份的调试配置（实测键清单 → 规范名，且只含一种方言）
        DeviceDebugProfilesCoverTheirObservedKeys();

        // 第一份**正式中间层**（会改名/算表达式/给曲线，不是纯直通）
        IphoneVtsMiddlewareProfileIsCoherent();

        // 两份 ho-debug-* 是"实测记录"而不是能跑的配置（通道解析是逐字的）
        DebugProfilesAreRecordsNotDrivers();

        // 规范文档与 catalog 不许漂移（这份表是"我们选什么"的唯一权威表）
        SpecTablesAgreeWithTheCatalog();

        // 控制器轴那一份（`Ho/Drive/*`）算出来到底是什么 —— 全是内联展开，抄错系数没人会发现
        ControllerAxesEvaluateAsDesigned();

        // `out("…")`：引用上面已算完的输出值（2026-09-27 用户定的机制：风格化形态要关掉张嘴笑那几棵树）
        ExpressionOutputReferences();

        // 倒V 的识别链（判定 + 迟滞 + 开关 + 手动增量 + 总门）—— 风格化形态的第一个实例
        StyleRecognitionChain();
        CheekRecognitionChain();

        // VTS 收包（这一套以前只能"连上手机试试看"，而它正是被 JsonUtility 丢掉 52 个形态键的地方）
        VtsRequestTextIsExact();
        VtsPayloadKeepsAllFiftyTwoShapes();
        VtsPayloadKeepsWireSpelling();
        VtsWireNamesMatchTheOfficialEnum();
        VtsPayloadToleratesUnknownFields();
        VtsPayloadFieldOrderDoesNotMatter();
        VtsPayloadWithoutBlendShapes();
        VtsPayloadExponentNumbers();
        VtsBrokenPayloadReportsPosition();

        Console.WriteLine();
        Console.WriteLine("profile-json-test: " + _passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    // ── 用例 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **调试配置（一台设备一份）必须是"原样直通"**：设备线名当参数名、出口键就是线名、
    /// 两层曲线都恒等。守的是三件真出过事的东西：
    ///  ① **改名** ⇒ 我们在发明一个标准（`browDown_L` -> `browDownLeft`），而标准还不存在。
    ///     参考预设的职责只有"原样记录"，标准以后按用途在副本上定。
    ///  ② `ARKit/` 前缀或 `Head/*` 保留名 ⇒ 那是"给某个下游用的标准"，预设不为任何下游服务。
    ///  ③ 曲线不够宽 ⇒ 值被**静默夹掉**。实测：只给输入行宽曲线时 `Position_x = 250` 到出口变 1.0；
    ///     而且没写 `curve` 的行会拿到 `Linear(0,0,1,1)`，链条**两层**都跑 `Transfer`。
    ///
    /// ⚠️ 这里**不**去核"配置的线名和实测表是否一致"：那份表是纯经验记录，不该让脚本互相对账。
    /// </summary>
    private static void DeviceDebugProfilesCoverTheirObservedKeys()
    {
        string root = RepoRoot();
        if (root == null) return;

        // 两份都过"原样直通"这几条。⚠️ iPhone 那份的**线名表还没做过实机统计**
        //    （`PARAMETER_DEVICE_VERIFICATION.md` §2 还没采），但它的线名是从一次真实 dump 抄的，
        //    配置本身是合法的纯直通 —— 这里守的是配置形状，不是"哪几条线在动"。
        foreach (string device in new[] { "androidVTS", "iphoneVTS" })
            CheckDeviceProfile(root, device);
    }

    /// <summary>一份参考预设：零改名 + 出口就是线名 + 两层曲线恒等。</summary>
    private static void CheckDeviceProfile(string root, string device)
    {
        string fileName = "ho-debug-" + device + ".hoface.json";
        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", fileName);
        if (!File.Exists(profilePath)) { Check("调试配置在包里：" + fileName, false); return; }

        HoFaceMiddleware profile;
        string error;
        bool ok = HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error);
        Check(fileName + " 能被 HoFaceProfile.TryParse 读进来", ok, error);
        if (!ok) return;

        Check(fileName + " 有输入行", profile.inputs.Count > 0, "inputs=" + profile.inputs.Count);

        // ① **零改名**：`parameter == expression == 设备线名`，一条都不许改名。
        int renamed = 0;
        var renamedText = new List<string>();
        foreach (var row in profile.inputs)
        {
            if (row.parameter != row.expression)
            {
                renamed++;
                if (renamedText.Count < 4) renamedText.Add(row.parameter + "!=" + row.expression);
            }
        }
        Check(fileName + " 一条都没改名（parameter 必须等于 expression，不符 " + renamed + "）"
            + (renamedText.Count > 0 ? "：" + string.Join(",", renamedText.ToArray()) : ""), renamed == 0);

        // ② 出口就是线名：不许有 `ARKit/` 前缀、不许有 `Head/*` 保留名、不许有重复。
        //    出口键集合还必须与输入行的线名逐一对应（缺一条/多一条都算错）。
        var rowNames = new HashSet<string>(StringComparer.Ordinal);
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        var exitNames = new HashSet<string>(StringComparer.Ordinal);
        int prefixed = 0, reserved = 0, duplicates = 0;
        foreach (var row in profile.inputs) inputNames.Add(row.expression);
        foreach (var row in profile.outputs)
        {
            if (!exitNames.Add(row.parameter)) duplicates++;
            if (row.parameter.StartsWith("ARKit/", StringComparison.Ordinal)) prefixed++;
            if (row.parameter.StartsWith("Head/", StringComparison.Ordinal)) reserved++;
            rowNames.Add(row.parameter);
        }
        Check(fileName + " 出口没有 ARKit/ 前缀、没有 Head/* 保留名、无重复（" + prefixed + "/" + reserved + "/重复 " + duplicates + "）",
            prefixed == 0 && reserved == 0 && duplicates == 0);

        var exitMissing = new List<string>();
        var exitExtra = new List<string>();
        foreach (string key in inputNames) if (!rowNames.Contains(key)) exitMissing.Add(key);
        foreach (string name in rowNames) if (!inputNames.Contains(name)) exitExtra.Add(name);
        Check(fileName + " 出口键与输入线名逐一对应（缺 " + exitMissing.Count + " · 多 " + exitExtra.Count + "）",
            exitMissing.Count == 0 && exitExtra.Count == 0);

        // ⑥ 每条线都必须是"值直通"：曲线在真实量纲上恒等。
        //    默认那条 Linear(0,0,1,1) 会把 ±45 度、±14 的读数**夹成 0/1**
        //    （HoFaceCurve.Transfer 只夹不外推），所以每条都要带宽曲线。
        int notTransparent = 0;
        var notTransparentText = new List<string>();
        foreach (var row in profile.inputs)
        {
            if (!Near(HoFaceCurve.Transfer(row.curve, -170f), -170f)
                || !Near(HoFaceCurve.Transfer(row.curve, 170f), 170f)
                || !Near(HoFaceCurve.Transfer(row.curve, 0.5f), 0.5f))
            {
                notTransparent++;
                if (notTransparentText.Count < 4) notTransparentText.Add(row.parameter);
            }
        }
        Check(fileName + " 每条线的曲线在 ±170 与 0.5 上恒等（不符 " + notTransparent + "）"
            + (notTransparentText.Count > 0 ? "：" + string.Join(",", notTransparentText.ToArray()) : ""),
            notTransparent == 0);

        // ⑦ **输出行也必须透明**。链条在**两层**都跑 Transfer()，而没写 curve 的行会拿到
        //    HoFaceOutput 的字段初值 Linear(0,0,1,1) —— 实测：输入行给了宽曲线、输出行没给，
        //    `Position_x = 250` 到出口就变成 **1.0**（值确实被改了，还是静默的）。
        int outNotTransparent = 0;
        var outNotTransparentText = new List<string>();
        foreach (var row in profile.outputs)
        {
            if (!Near(HoFaceCurve.Transfer(row.curve, -170f), -170f)
                || !Near(HoFaceCurve.Transfer(row.curve, 170f), 170f)
                || !Near(HoFaceCurve.Transfer(row.curve, 0.5f), 0.5f))
            {
                outNotTransparent++;
                if (outNotTransparentText.Count < 4) outNotTransparentText.Add(row.parameter);
            }
        }
        Check(fileName + " 输出行的曲线也恒等（不符 " + outNotTransparent + "）"
            + (outNotTransparentText.Count > 0 ? "：" + string.Join(",", outNotTransparentText.ToArray()) : ""),
            outNotTransparent == 0);
    }

    /// <summary>
    /// **catalog 与发货配置必须对得上**：catalog（`docs/VTS_HIGH_QUALITY_FACE_CATALOG.json`）是
    /// **唯一**记录 ARKit 52 个规范名及其各方言拼写的地方，而 `ho-iPhoneVTS.hoface.json` 的 52 条
    /// 形状输入行必须正好覆盖它 —— 名字错一个下划线，那一行就静默永不触发。
    ///
    /// 这条用例是"改了生成器 / 设备固件 / catalog 之后没重新生成"的唯一守门人。
    /// 它以前读的是已删的 `ho-vts-default.hoface.json`（那份同时有 `_L/_R` 与 PascalCase 两种方言、
    /// 出口还是 52 条 `ARKit/*` 直通），现在改读唯一那份发货中间层。
    ///
    /// ⚠️ 线名集合用**表驱动**地减掉那 15 个已知的非形状线（`Rotation_*` / `Position_*` /
    /// `EyeLeft_*` / `EyeRight_*` / `FaceFound` / `Hotkey` / `Timestamp`），而不是靠
    /// "大小写像不像"去猜 —— 后者正是这个文件之前出错的思路。
    /// </summary>
    private static void ShippedProfileMatchesTheCatalog()
    {
        string root = RepoRoot();
        if (root == null) { Check("找得到包根（仓库根）", false); return; }

        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        string catalogPath = Path.Combine(root, "docs", "VTS_HIGH_QUALITY_FACE_CATALOG.json");
        if (!File.Exists(profilePath)) { Check("发货中间层在包里 (" + profilePath + ")", false); return; }
        if (!File.Exists(catalogPath)) { Check("catalog 在 docs 里", false); return; }

        HoFaceMiddleware profile;
        string error;
        bool ok = HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error);
        Check("发货中间层能被 HoFaceProfile.TryParse 读进来", ok, error);
        if (!ok) return;

        // catalog：52 个 ARKit 规范名，以及它自己那三种方言拼写
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(catalogPath));
        var raw = catalog.RootElement.GetProperty("raw_arkit");
        Check("catalog 的 raw_arkit 是 52 条", raw.GetArrayLength() == 52);

        var canonical = new List<string>();
        var vbInternal = new List<string>();
        foreach (var row in raw.EnumerateArray())
        {
            canonical.Add(row.GetProperty("name").GetString());
            vbInternal.Add(row.GetProperty("vb_internal").GetString());
        }

        // 输入行：52 条形状行的 parameter（规范名）+ expression（设备线名）各覆盖一边
        var shapeParameters = new HashSet<string>(StringComparer.Ordinal);
        var shapeWires = new HashSet<string>(StringComparer.Ordinal);
        int nonShapeRows = 0;
        foreach (var row in profile.inputs)
        {
            if (row.parameter == null) continue;
            // ⚠️ 我们自己那几条**外部开关**（`HoAuto*` / `HoExternal*`，Warudo 在 VTS 接收器之后
            //    append 的线）不是设备线，别算进"52 形状 + 15 非形状"这份账里。
            if (IsInjectedWire(row.parameter)) continue;
            if (NonShapeRows.Contains(row.parameter)) { nonShapeRows++; continue; }
            shapeParameters.Add(row.parameter);
            shapeWires.Add(row.expression);
        }
        Check("除那 15 个已知非形状线外，输入行正好是 52 条（形状 " + shapeParameters.Count + " + 非形状 " + nonShapeRows + "）",
            shapeParameters.Count == 52 && nonShapeRows == 15);

        int missingCanon = 0;
        foreach (string name in canonical) if (!shapeParameters.Contains(name)) missingCanon++;
        Check("catalog 的 52 个规范名都在输入行的 parameter 里（缺 " + missingCanon + "）", missingCanon == 0);

        // expression 那一侧是**设备线名**（VTS 的 PascalCase `JawOpen`），规范名是 camelCase
        // （`jawOpen`）—— 两者本来就只有首字母之差（这正是 VBridger `vtsKeys` 表的关系），
        // 所以这里**忽略大小写**比。要精确核对"设备到底发哪个拼写"，权威是
        // docs/PARAMETER_DEVICE_VERIFICATION.md 的 wire 列，不是 catalog。
        var shapeWiresIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string wire in shapeWires) shapeWiresIgnoreCase.Add(wire);
        int missingWire = 0;
        var missingWireText = new List<string>();
        foreach (string name in canonical)
            if (!shapeWiresIgnoreCase.Contains(name))
            {
                missingWire++;
                if (missingWireText.Count < 4) missingWireText.Add(name);
            }
        Check("catalog 的 52 个规范名都在输入行的 expression 里（忽略大小写；缺 " + missingWire + "）"
            + (missingWireText.Count > 0 ? "：" + string.Join(",", missingWireText.ToArray()) : ""), missingWire == 0);

        // vb_internal（`_L/_R` 那一族）**只是参考**：发货配置现在只接 VTS（PascalCase）。
        // 所以这里只报告覆盖情况，不断言 —— 少了它不代表配置有问题。
        int vbCovered = 0;
        foreach (string name in vbInternal) if (shapeWires.Contains(name)) vbCovered++;
        Check("catalog 的规范名与设备线名覆盖完整（vb_internal 那族只作参考：覆盖 " + vbCovered + "/52）",
            shapeParameters.Count == 52);
    }

    /// <summary>已知的**非形状**输入线：头/眼姿态 12 个 + 包字段 3 个。表驱动，不靠大小写猜。</summary>
    private static readonly HashSet<string> NonShapeRows = new HashSet<string>(StringComparer.Ordinal)
    {
        "Rotation_x", "Rotation_y", "Rotation_z",
        "Position_x", "Position_y", "Position_z",
        "EyeLeft_x", "EyeLeft_y", "EyeLeft_z",
        "EyeRight_x", "EyeRight_y", "EyeRight_z",
        "FaceFound", "Hotkey", "Timestamp"
    };

    /// <summary>
    /// `ho-iPhoneVTS.hoface.json` —— 包里唯一的**正式中间层**。它跟两份 `ho-debug-*` 的区别是
    /// 它**真的在干活**：输出行是"从 ARKit 形态量合成 VTS 官方追踪参数"的表达式，不是恒等直通。
    ///
    /// 这条用例守四件真会静默出事的东西：
    ///  ① **输入行的 `parameter` 必须是规范名**。`HoFaceAnimationSession` 拿它去
    ///     `HoFaceTrackingChannels.IndexOf(...)` 解析通道 —— 写成设备线名（`JawOpen`）时
    ///     **一个通道都解析不到**，中间层静默地什么形状参数都不产出。真发生过。
    ///  ② **表达式要能被我们自己的解析器读**。这一层跑在每帧管线上，解析失败的行永远不出值，
    ///     而且**不报错**。
    ///  ③ **出口表达式引用的每个变量都要有来源**（规范名或原始线名）。写错一个名字那一段恒为 0
    ///     且毫无提示。
    ///  ④ **静息 0.5 的合成量要带够宽的曲线**。没写 `curve` 的行会拿到 `Linear(0,0,1,1)`，
    ///     于是 `Brows` 这种能过 1 的合成量被静默夹掉。
    /// </summary>
    private static void IphoneVtsMiddlewareProfileIsCoherent()
    {
        string root = RepoRoot();
        if (root == null) return;

        const string fileName = "ho-iPhoneVTS.hoface.json";
        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", fileName);
        if (!File.Exists(profilePath)) { Check("正式中间层在包里：" + fileName, false); return; }

        HoFaceMiddleware profile;
        string error;
        bool ok = HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error);
        Check(fileName + " 能被 HoFaceProfile.TryParse 读进来", ok, error);
        if (!ok) return;

        // 输入行：**52 个形状行的 parameter 必须是规范名（camelCase），expression 才是设备线名
        // （PascalCase）**；另外 15 行（头/眼姿态 + 三个包字段）不是通道名，所以不改名。
        //
        // ⚠️ 这条断言守的是一个**静默且致命**的 bug（真发生过）：`HoFaceAnimationSession` 是拿
        // **输入行的 `parameter`** 去 `HoFaceTrackingChannels.IndexOf(...)` 解析通道的。所以一份
        // `parameter = expression = JawOpen` 的配置会**一个通道都解析不到**，中间层静默地什么形状
        // 参数都不产出 —— 不报错、不警告，只是全都不动。
        int shapeRenamed = 0, otherRenamed = 0;
        var badCanonical = new List<string>();
        var badOther = new List<string>();
        var wireNames = new HashSet<string>(StringComparer.Ordinal);
        var canonicals = new HashSet<string>(HoFaceTrackingChannels.Names, StringComparer.Ordinal);
        foreach (var row in profile.inputs)
        {
            wireNames.Add(row.expression);            // 设备真发的线名
            bool isChannel = canonicals.Contains(row.parameter);
            if (isChannel)
            {
                if (row.parameter != row.expression) shapeRenamed++;
                else if (badCanonical.Count < 4) badCanonical.Add(row.parameter);   // 没改名 = 是线名，解析不到
                continue;
            }
            if (row.parameter == row.expression) continue;
            otherRenamed++;
            if (badOther.Count < 4) badOther.Add(row.parameter + "!=" + row.expression);
        }
        Check(fileName + " 输入行里 " + shapeRenamed + " 个形状行改名成规范名（" + HoFaceTrackingChannels.Names.Length + " 个通道名）",
            shapeRenamed == HoFaceTrackingChannels.Names.Length);
        Check(fileName + " 没有形状行拿设备线名当规范名（那样一个通道都解析不到）："
            + (badCanonical.Count > 0 ? string.Join(",", badCanonical.ToArray()) : "无"), badCanonical.Count == 0);
        Check(fileName + " 非通道行（头/眼姿态 + 包字段）不改名（不符 " + otherRenamed + "）"
            + (badOther.Count > 0 ? "：" + string.Join(",", badOther.ToArray()) : ""), otherRenamed == 0);
        // ⚠️ 我们自己那几条**外部开关**（Warudo 在 VTS 接收器之后 append 的线）不算设备线 ——
        //    设备线的条数是**协议事实**（67），掺进我们自己的名字会让那条断言失去意义。
        int deviceWires = 0;
        foreach (string wire in wireNames)
            if (!IsInjectedWire(wire)) deviceWires++;
        var injected = new List<string>(wireNames).FindAll(IsInjectedWire);
        Check(fileName + " 外部开关行（`HoAuto*` / `HoExternal*`）在输入区里声明过："
            + string.Join(",", injected.ToArray()),
            wireNames.Contains("HoAutoInvertedV") && wireNames.Contains("HoExternalInvertedV")
            && wireNames.Contains("HoAutoCatMouth") && wireNames.Contains("HoExternalCatMouth"));
        Check(fileName + " 输入行是 iPhone 的 67 条线 + 我们自己那七条外部开关（设备线 " + deviceWires + " / 全部 " + wireNames.Count + "）",
            deviceWires == 67 && wireNames.Count == 67 + 7);

        // 出口行：
        //  ① 每条表达式的**每个变量**都得有来源。出口吃的是**规范名**（输入行产出的那些），
        //     外加几个非通道的线名（`Rotation_x` / `FaceFound`…），所以两边都算数。
        //  ② 表达式必须解析得动。
        //  ③ 曲线不能是"没写"（那会拿到 0..1 默认值），且不许重复 parameter。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = 0, unparsed = 0, unknownVars = 0, notIdentity = 0, constantRows = 0;
        var problems = new List<string>();
        var synthesized = new List<HoFaceOutput>();   // 合成量：静息 0.5、能出 [0,1]
        var knownVars = new HashSet<string>(canonicals, StringComparer.Ordinal);
        knownVars.UnionWith(wireNames);               // 非通道的那些原始线名也允许

        foreach (var row in profile.outputs)
        {
            // ⚠️ `Ho/Style/*` 里**允许重名**：风格化门的「双重形态」链就是"同名行读自己写自己"
            //    （输出表缓存：后写覆盖先写）⇒ 那里的重名是有意的，别算进"名字敲重了"这个警报里。
            bool styleRow = row != null && HoFaceNaming.IsStyleRow(row.parameter);
            if (!styleRow && !seen.Add(row.parameter)) duplicates++;

            HoFaceExpression parsed;
            string why;
            // 常量行（`expression` 留空 + `defaultValue`）是**合法**的一类：中间层自己产出门控就是这么写的
            // （docs/FACE_TRACKING_MIDDLE_LAYER.md §5.3，"行侧 gate"）。空表达式不进解析器。
            if (row == null || string.IsNullOrWhiteSpace(row.expression))
            {
                constantRows++;
                continue;
            }
            if (!HoFaceExpression.TryParse(row.expression, out parsed, out why))
            {
                unparsed++;
                if (problems.Count < 4) problems.Add(row.parameter + " 解析失败:" + why);
                continue;
            }

            var used = new List<string>();
            parsed.CollectVariables(used);
            foreach (string name in used)
            {
                if (knownVars.Contains(name)) continue;
                unknownVars++;
                if (problems.Count < 4) problems.Add(row.parameter + " 引用了没来源的变量:" + name);
            }

            // 曲线必须是"直线且 v 跟随 t"（横轴区间就是定义域，超出按端点算）。
            // 这里不要求它跟 0..1 默认值不同 —— CheekPuff / TongueOut 本来就该是 0..1。
            if (row.curve == null || row.curve.length < 2) { notIdentity++; continue; }
            float lo = row.curve.keys[0].time, hi = row.curve.keys[row.curve.length - 1].time;
            if (!Near(row.curve.keys[0].value, lo) || !Near(row.curve.keys[row.curve.length - 1].value, hi))
                notIdentity++;

            // 静息 0.5 的合成量（VB 那套 `Brows` / `MouthOpen` / `EyeOpen*` / `Brow*Y`）在
            // 极端表情下会越过 [0,1]。这类行必须给宽曲线，否则默认曲线会**静默**把它们夹掉。
            if (row.expression.Contains(".5 +") || row.expression.Contains(".5-") || row.expression.Contains("(2 -"))
                synthesized.Add(row);
        }

        Check(fileName + " 出口表达式全部能解析（" + constantRows + " 条常量行、失败 " + unparsed + "）"
            + (problems.Count > 0 ? "：" + string.Join(" | ", problems.ToArray()) : ""), unparsed == 0);
        Check(fileName + " 出口引用的变量都有来源（规范名或原始线名；没来源 " + unknownVars + "）"
            + (problems.Count > 0 ? "：" + string.Join(" | ", problems.ToArray()) : ""), unknownVars == 0);
        Check(fileName + " 出口 parameter 无重复（重复 " + duplicates + "）", duplicates == 0);
        Check(fileName + " 出口曲线都是恒等直线（不符 " + notIdentity + "）", notIdentity == 0);

        // 合成量不许用 0..1 曲线（那正是"出口恒 0 或 1 而输入侧有真值"的成因）。
        int clipped = 0;
        var clippedText = new List<string>();
        foreach (var row in synthesized)
        {
            float lo = row.curve.keys[0].time, hi = row.curve.keys[row.curve.length - 1].time;
            if (Near(lo, 0f) && Near(hi, 1f))
            {
                clipped++;
                if (clippedText.Count < 4) clippedText.Add(row.parameter);
            }
        }
        Check(fileName + " 有 " + synthesized.Count + " 条静息 0.5 的合成量，都没有用 0..1 曲线（夹掉的 " + clipped + "）"
            + (clippedText.Count > 0 ? "：" + string.Join(",", clippedText.ToArray()) : ""), clipped == 0);

        // 出口是**两份并行**的：G1 原始 ARKit 52（裸名、无损）+ G2/G3 合成量。
        // 这是照 VBridger 的形状来的 —— 它的 VMC 发送循环遍历整个 `outputValues`（里面躺着
        // 全部原始形态键名），所以原始量就是**裸名**发出去的，合成行只是再往里加键
        // （`Assembly-CSharp.decompiled.cs` L13322-13329）。
        //
        // ⚠️ 曾经写成 `ARKit/<名>`，理由是"要个命名空间跟合成组分开"。那错了两层：
        // ① VB 不这么干；② 加了前缀就**谁都读不到** —— 控制器只写自己参数表里声明过的名字，
        // `ARKit/eyeBlinkLeft` 与任何声明都对不上，于是 52 行白算。
        //
        // 因此这里断言的是：**52 条裸名、恒等直通、名字都是规范名**。
        int rawRows = 0, rawBadExpr = 0, headRows = 0, faceNamespace = 0;
        var rawBad = new List<string>();
        var allParams = new List<string>();
        foreach (var row in profile.outputs)
        {
            allParams.Add(row.parameter);
            if (row.parameter.StartsWith("Head/", StringComparison.Ordinal)) { headRows++; continue; }
            if (row.parameter.StartsWith("Face/", StringComparison.Ordinal)
                || row.parameter.StartsWith("Body/", StringComparison.Ordinal)) { faceNamespace++; continue; }
            if (!canonicals.Contains(row.parameter)) continue;      // 规范名 = 原始那一组
            rawRows++;
            if (row.expression != row.parameter)
            {
                rawBadExpr++;
                if (rawBad.Count < 4) rawBad.Add(row.parameter + "=" + row.expression);
            }
        }
        Check(fileName + " 原始 ARKit 那一组是整齐 52 条、用裸名（实际 " + rawRows + "）", rawRows == 52);
        Check(fileName + " 原始那一组都是恒等直通、表达式就是自己的名字（不符 " + rawBadExpr + "）"
            + (rawBad.Count > 0 ? "：" + string.Join(",", rawBad.ToArray()) : ""), rawBadExpr == 0);
        Check(fileName + " 没有 Head/* 保留名（那是已删的 ho-vts-default 的）（" + headRows + "）", headRows == 0);
        Check(fileName + " 有姿态向量命名空间 Face/* · Body/*（" + faceNamespace + " 行）", faceNamespace == 12);
        // ⚠️ **有意的例外**：风格化门的「双重形态」链里，每个形态会再压一条**同名**行
        //    （输出表缓存：后写覆盖先写）⇒ `Ho/Style/*` 内部允许重名，**别的一律不许**。
        var outsideStyle = new List<string>();
        var styleNames = new List<string>();
        foreach (string name in allParams)
            (HoFaceNaming.IsStyleRow(name) ? styleNames : outsideStyle).Add(name);
        int styleDuplicates = styleNames.Count - new HashSet<string>(styleNames, StringComparer.Ordinal).Count;
        Check(fileName + " 出口 parameter 严格无重复（" + outsideStyle.Count + " 行；`Ho/Style/*` 里另有 "
            + styleDuplicates + " 条有意的同名链）",
            outsideStyle.Count == new HashSet<string>(outsideStyle, StringComparer.Ordinal).Count && styleDuplicates == 2);

        // 通道解析要**逐字**命中 `HoFaceTrackingChannels` 的索引表
        // （52 个规范名 + 36 个 `_L`/`_R` 别名 = 88 条，`StringComparer.Ordinal`）。
        // 所以"能解析到多少通道"是这份配置**能不能真跑**的唯一判据。这一条钉住它。
        // ⚠️ 别用 PowerShell 的 `-contains` 核这件事：它默认忽略大小写，会假报成 52/52。
        var resolvedChannels = new HashSet<int>();
        foreach (var row in profile.inputs)
            if (row != null)
            {
                int index = HoFaceTrackingChannels.IndexOf(row.parameter);
                if (index >= 0) resolvedChannels.Add(index);
            }
        Check(fileName + " 输入行解析到 " + resolvedChannels.Count + " / " + HoFaceTrackingChannels.Names.Length
            + " 个通道（能不能真跑的判据）", resolvedChannels.Count == HoFaceTrackingChannels.Names.Length);

        // 平滑：G1（原始那一组）**必须没有**修饰符 —— 它的全部价值就是"没被动过的值"。
        // 而且平滑的 `seconds` 必须 > 0，否则 `HoFaceModifier.Active` 为 false、那条修饰符是死的。
        int rawRowsWithModifiers = 0, liveSmooth = 0, deadSmooth = 0;
        var smoothTimes = new List<float>();
        // ⚠️ **风格化门（`Ho/Style/*`）单开一档**：它不是"给信号去抖"，是一次**形态切换的过渡**
        //    （用户定「开大 smooth 做过渡」）⇒ 允许到 0.3 秒。其余行仍是给信号整形，0.1 封顶 ——
        //    那一档是照 VB 同族换算出来的（0.007..0.042），开大了整张脸会拖。
        var styleSmoothTimes = new List<float>();
        foreach (var row in profile.outputs)
        {
            if (row == null) continue;
            bool isRaw = row.expression == row.parameter && canonicals.Contains(row.parameter);
            bool hasModifiers = row.modifiers != null && row.modifiers.Count > 0;
            if (isRaw && hasModifiers) rawRowsWithModifiers++;

            bool isStyleRow = HoFaceNaming.IsStyleRow(row.parameter);
            if (row.modifiers == null) continue;
            foreach (var modifier in row.modifiers)
            {
                if (modifier == null) continue;
                if (modifier.kind != HoFaceModifierKind.Smooth) continue;
                if (modifier.Active)
                {
                    liveSmooth++;
                    (isStyleRow ? styleSmoothTimes : smoothTimes).Add(modifier.seconds);
                }
                else
                {
                    deadSmooth++;
                }
            }
        }
        Check(fileName + " 原始那一组不带任何修饰符（带了 " + rawRowsWithModifiers + " 行）", rawRowsWithModifiers == 0);
        Check(fileName + " 平滑修饰符的 seconds 都 > 0（死的 " + deadSmooth + " 条）", deadSmooth == 0);
        Check(fileName + " 有 " + liveSmooth + " 行开了平滑（VB 的 V3.0 是 20/26 行）", liveSmooth > 0);
        if (smoothTimes.Count > 0)
        {
            float min = smoothTimes[0], max = smoothTimes[0];
            foreach (float t in smoothTimes) { if (t < min) min = t; if (t > max) max = t; }
            // VB 换算到秒是 0.007..0.042（见生成器的换算表）。给个宽容但有意义的区间。
            Check(fileName + " 平滑时间在 0.005..0.100 秒（实测 " + min.ToString("0.###") + ".." + max.ToString("0.###") + "）",
                min >= 0.005f && max <= 0.100f);
        }
        if (styleSmoothTimes.Count > 0)
        {
            float min = styleSmoothTimes[0], max = styleSmoothTimes[0];
            foreach (float t in styleSmoothTimes) { if (t < min) min = t; if (t > max) max = t; }
            Check(fileName + " 风格化门的过渡平滑在 0.05..0.300 秒（实测 " + min.ToString("0.###") + ".." + max.ToString("0.###") + "）",
                min >= 0.05f && max <= 0.3f);
        }
    }

    /// <summary>
    /// 两份 `ho-debug-*` 是"这台设备实际发了什么"的**实测记录**，不是能跑的配置 ——
    /// 它们的输入行 `parameter` 是**设备线名**，而通道解析要**逐字**命中索引表。
    ///
    /// ⚠️ 那个索引表**不只 52 个规范名**：`HoFaceTrackingChannels.BuildIndices` 还给每个
    /// `Left`/`Right` 结尾的规范名额外注册了 `_L`/`_R` 别名（`eyeBlinkLeft` ↔ `eyeBlink_L`），
    /// 共 88 条。所以安卓那份（两种拼写都发）能解析到 **40/52**，而不是只认规范名时的 12。
    /// 我一度拿"只比 52 个规范名"的脚本去核，得出 12 —— 那是脚本漏了别名表。
    ///
    /// 这条用例把三份的实测数字钉住，免得哪天有人看到"零改名、恒等曲线"就以为它们现成可用。
    /// </summary>
    private static void DebugProfilesAreRecordsNotDrivers()
    {
        string root = RepoRoot();
        if (root == null) return;

        // 实测值（走真的 `HoFaceTrackingChannels.IndexOf`，含 `_L/_R` 别名）。
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "ho-debug-iphoneVTS", 0 },    // 设备线名是 PascalCase，规范名/别名都是 camelCase
            { "ho-debug-androidVTS", 40 }   // 小驼峰 + `_L/_R` 命中别名表
        };

        foreach (var pair in expected)
        {
            string path = Path.Combine(root, "Editor", "FaceTracking", "Profiles", pair.Key + ".hoface.json");
            if (!File.Exists(path)) { Check("调试配置在包里：" + pair.Key, false); continue; }

            HoFaceMiddleware profile;
            string error;
            if (!HoFaceProfile.TryParse(File.ReadAllText(path), out profile, out error))
            {
                Check(pair.Key + " 能被 HoFaceProfile.TryParse 读进来", false, error);
                continue;
            }

            var channels = new HashSet<int>();
            foreach (var row in profile.inputs)
                if (row != null)
                {
                    int index = HoFaceTrackingChannels.IndexOf(row.parameter);
                    if (index >= 0) channels.Add(index);
                }

            Check(pair.Key + " 能解析到 " + channels.Count + " / " + HoFaceTrackingChannels.Names.Length
                + " 个通道（预期 " + pair.Value + "）—— 所以它不是能跑的配置，只是记录",
                channels.Count == pair.Value);
        }
    }

    /// <summary>
    /// `docs/PARAMETER_HO.md` 是"我们选什么"的唯一权威表，而它最容易过期的不是文字而是**分类**：
    /// 哪个名字是官方 VTS 的、哪个是 VB 自造的、哪个我们根本算不出来。
    ///
    /// 出口现在是**五组**（§3.1–§3.5），这条用例按组核对：
    ///  ① G1「原始 ARKit 52」必须是整齐 52 条，且每个名字都在 catalog 的 `raw_arkit` 里。
    ///  ② G2「官方 VTS 追踪参数」的每个名字都必须在 catalog 的 `native`（官方全量 98 条）里
    ///     —— 否则我们把自造名误标成了官方名（第一版就犯过：把 MouthFunnel 当成官方参数）。
    ///  ③ G3a「VB 自造」的每个名字都**不许**在 `native` 里；G3b 向量 / G3c 信号同理。
    ///  ④ §5「算不出来」不许与任何一组重叠。
    ///  ⑤ §0 声明的各组行数必须与实际抽到的一致（数字过期是这份文档最常见的腐烂方式）。
    /// </summary>
    private static void SpecTablesAgreeWithTheCatalog()
    {
        string root = RepoRoot();
        if (root == null) return;

        string docPath = Path.Combine(root, "docs", "PARAMETER_HO.md");
        string catalogPath = Path.Combine(root, "docs", "VTS_HIGH_QUALITY_FACE_CATALOG.json");
        if (!File.Exists(docPath)) { Check("规范文档在 docs 里 (PARAMETER_HO.md)", false); return; }
        if (!File.Exists(catalogPath)) { Check("catalog 在 docs 里", false); return; }

        string[] lines = File.ReadAllText(docPath).Split('\n');

        var raw52    = FirstColumnBackticks(lines, "### 3.1 ");
        var official = TableNames(lines, "### 3.2 ");
        var custom   = TableNames(lines, "### 3.3 ");
        var vectors  = TableNames(lines, "### 3.4 ");
        var signal   = TableNames(lines, "### 3.5 ");
        var impossible = TableNames(lines, "## 5. ");

        Check("规范 §3.1 抽到原始 ARKit 组（" + raw52.Count + "）", raw52.Count > 0);
        Check("规范 §3.2 抽到官方参数名（" + official.Count + "）", official.Count > 0);
        Check("规范 §3.3 抽到自造参数名（" + custom.Count + "）", custom.Count > 0);
        Check("规范 §3.4 抽到姿态向量（" + vectors.Count + "）", vectors.Count > 0);
        Check("规范 §3.5 抽到协议层信号（" + signal.Count + "）", signal.Count > 0);
        Check("规范 §5 抽到算不出来的名字（" + impossible.Count + "）", impossible.Count > 0);

        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(catalogPath));

        // catalog 的 native 段 = VTS 官方全量清单（98 条）
        var native = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in catalog.RootElement.GetProperty("native").EnumerateArray())
            native.Add(p.GetProperty("name").GetString());

        // catalog 的 raw_arkit 段 = ARKit 52 的规范名
        var arkitNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in catalog.RootElement.GetProperty("raw_arkit").EnumerateArray())
            arkitNames.Add(r.GetProperty("name").GetString());

        // ① G1：整齐 52 条，且每个都在 catalog 的 raw_arkit 里
        Check("规范 §3.1 是整齐 52 条（实际 " + raw52.Count + "）", raw52.Count == 52);
        var rawNotArk = new List<string>();
        foreach (string name in raw52) if (!arkitNames.Contains(name)) rawNotArk.Add(name);
        Check("规范 §3.1 的 " + raw52.Count + " 个名字都在 catalog 的 raw_arkit 里（不在 " + rawNotArk.Count + "）"
            + (rawNotArk.Count > 0 ? "：" + string.Join(",", rawNotArk.ToArray()) : ""), rawNotArk.Count == 0);

        // ② G2：官方表的每个名字都必须真的在官方清单里
        var notOfficial = new List<string>();
        foreach (string name in official) if (!native.Contains(name)) notOfficial.Add(name);
        Check("规范 §3.2 的 " + official.Count + " 个名字都在 VTS 官方清单里（不在 " + notOfficial.Count + "）"
            + (notOfficial.Count > 0 ? "：" + string.Join(",", notOfficial.ToArray()) : ""), notOfficial.Count == 0);

        // ③ G3a/G3b/G3c：都不许是官方命名参数
        //    （`FaceFound` 是报文里的布尔字段，不是 parameterValues[] 里的一个 id ——
        //     这一条正是修掉"把 FaceFound 当官方参数"的那个错。）
        foreach (var group in new[] {
            new { Text = "§3.3 自造", Names = custom },
            new { Text = "§3.4 向量", Names = vectors },
            new { Text = "§3.5 信号", Names = signal } })
        {
            var bad = new List<string>();
            foreach (string name in group.Names) if (native.Contains(name)) bad.Add(name);
            Check("规范 " + group.Text + " 的 " + group.Names.Count + " 个名字都不是 VTS 官方参数（其实是的 " + bad.Count + "）"
                + (bad.Count > 0 ? "：" + string.Join(",", bad.ToArray()) : ""), bad.Count == 0);
        }

        // ④ 六组互不相交
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int overlaps = 0;
        var overlapText = new List<string>();
        foreach (var group in new[] { raw52, official, custom, vectors, signal, impossible })
            foreach (string name in group)
                if (!seen.Add(name)) { overlaps++; overlapText.Add(name); }
        Check("规范六张表互不相交（重叠 " + overlaps + "）"
            + (overlapText.Count > 0 ? "：" + string.Join(",", overlapText.ToArray()) : ""), overlaps == 0);

        // ⑤ §0 的分类表里声明的数字必须与各表实际抽到的条数一致。
        //    这一条守的是"文档里的数字过期"——总数写错过两次（48、37）。
        var declared = DeclaredCounts(lines);
        AssertDeclared(declared, "原始 ARKit 52", raw52.Count);
        AssertDeclared(declared, "官方 VTS 追踪参数", official.Count);
        AssertDeclared(declared, "VB 自造参数", custom.Count);
        AssertDeclared(declared, "姿态向量", vectors.Count);
        AssertDeclared(declared, "协议层信号", signal.Count);

        // §0 的总数是那行**没有组名**的合计（`| | **90** | | **出口行总数** |`），
        // 所以按"行里出现 出口行总数"去找它那个数。
        int declaredTotal = -1;
        foreach (string line in lines)
        {
            if (line.IndexOf("出口行总数", StringComparison.Ordinal) < 0) continue;
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            string[] cells = line.Split('|');
            for (int c = 1; c < cells.Length; c++)
            {
                int at = cells[c].IndexOf("**", StringComparison.Ordinal);
                if (at < 0) continue;
                var digits = new StringBuilder();
                for (int i = at + 2; i < cells[c].Length && char.IsDigit(cells[c][i]); i++) digits.Append(cells[c][i]);
                if (digits.Length > 0) { declaredTotal = int.Parse(digits.ToString()); break; }
            }
            if (declaredTotal >= 0) break;
        }
        int actualTotal = raw52.Count + official.Count + custom.Count + vectors.Count + signal.Count;
        Check("规范 §0 声明的出口行总数与实际一致（声明 " + (declaredTotal >= 0 ? declaredTotal.ToString() : "缺") + " · 实际 " + actualTotal + "）",
            declaredTotal == actualTotal);
    }

    /// <summary>
    /// 读 §0 分类表里 `| &lt;名字&gt; | **&lt;数字&gt;** |` 形式的声明值。
    /// ⚠️ 键名里带反引号（`` 协议层信号 `FaceFound` ``），所以查的时候用 Contains 而不是相等。
    /// </summary>
    private static Dictionary<string, int> DeclaredCounts(string[] lines)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) continue;
            string[] cells = trimmed.Split('|');
            if (cells.Length < 3) continue;
            string key = cells[1].Trim();
            int start = cells[2].IndexOf("**", StringComparison.Ordinal);
            if (start < 0) continue;
            var digits = new StringBuilder();
            for (int i = start + 2; i < cells[2].Length && char.IsDigit(cells[2][i]); i++) digits.Append(cells[2][i]);
            if (digits.Length > 0 && key.Length > 0) found[key] = int.Parse(digits.ToString());
        }
        return found;
    }

    private static void AssertDeclared(Dictionary<string, int> declared, string keyFragment, int actual)
    {
        int value = -1;
        bool has = false;
        foreach (var pair in declared)
            if (pair.Key.Contains(keyFragment)) { value = pair.Value; has = true; break; }
        Check("规范 §0 声明的「" + keyFragment + "」与实际一致（声明 " + (has ? value.ToString() : "缺") + " · 实际 " + actual + "）",
            has && value == actual);
    }

    /// <summary>
    /// 抽某个标题下面那张表**最左一列**里的参数名。
    ///
    /// 规则：**每行取最左边那个反引号**。为什么是这个规则 ——
    ///  · 参数名列总是在最左（`| `MouthOpen` | …`），或者前面只有一个序号（`| 1 | `eyeBlink…` | …`）；
    ///  · 而**公式列与备注列在它右边**，里面的变量也叫 `mouthFunnel` 这种名字，一起抽会污染分组；
    ///  · `titleMarked = true` 用于**分组表**（§3.2–§3.5 带编号）：那种表的"组名"列是
    ///    加粗/带反引号的说明文字（`**G1 原始 ARKit 52**`、`姿态向量`），不能当参数名。
    ///    这类表里参数名是**第一个反引号**，所以默认规则本来就行 —— 这个开关留给未来收窄。
    /// </summary>
    private static List<string> TableNames(string[] lines, string headingPrefix, bool numbered = false)
    {
        var names = new List<string>();
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(headingPrefix, StringComparison.Ordinal)) { start = i + 1; break; }
        }
        if (start < 0) return names;

        // ⚠️ 不能写 StartsWith("##")：`###` 也以 `##` 开头，标题行自己就把循环断掉了。
        for (int i = start; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                int hashes = 0;
                while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
                if (hashes > 0 && hashes < trimmed.Length && trimmed[hashes] == ' ') break;
            }
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) continue;
            if (!trimmed.Contains("`")) continue;                       // 表头/分隔行没有反引号

            int open = trimmed.IndexOf('`');
            if (open < 0) continue;
            int close = trimmed.IndexOf('`', open + 1);
            if (close < 0) continue;
            string token = trimmed.Substring(open + 1, close - open - 1);
            if (token.Length == 0) continue;
            // 两列布局（§3.1 的 52 条）会把参数名放在第二列，所以再往后找一个"像名字的"。
            if (!IsPoseVectorPath(token) && !Regex.IsMatch(token, @"^[A-Za-z][A-Za-z0-9]*$"))
            {
                // 本行的第一个反引号不是参数名（比如是范围或组名），跳过整行。
                continue;
            }
            if (!names.Contains(token)) names.Add(token);
        }
        return names;
    }

    /// <summary>
    /// 抽某个标题下面那张表格里**每一格**的第一个反引号内容（§3.1 的 52 条是两列排的，
    /// `| 1 | name | 组 | 27 | name | 组 |`，只取一格会漏掉右半边）。
    /// </summary>
    private static List<string> FirstColumnBackticks(string[] lines, string headingPrefix)
    {
        var names = new List<string>();
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(headingPrefix, StringComparison.Ordinal)) { start = i + 1; break; }
        }
        if (start < 0) return names;
        // ⚠️ 也不能写 StartsWith("##")：`###` 也以 `##` 开头，那样标题行自己就把循环断掉了。
        //    这里只认"行首到第一个空格之间全是 '#'"。
        for (int i = start; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                int hashes = 0;
                while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
                if (hashes > 0 && hashes < trimmed.Length && trimmed[hashes] == ' ') break;
            }
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) continue;
            if (trimmed.Contains("---")) continue;                     // 分隔行
            // 取**每个单元格里的第一个**反引号内容，再按形状筛掉说明性文字。
            //
            // 为什么要全格取：§3.1 的 52 条是两列排的（`| 1 | name | 组 | 27 | name | 组 |`），
            // 只取一格会漏掉右半边 —— 那是 52 条只抽出 26 条的成因。
            // 为什么要筛：同一行里还有范围（`0..1`）、预设名（`VTS_Compatible`）、
            // 设备线名（`EyeLeft_x`）、通配写法（`Rotation_*`），它们不是出口参数名。
            string[] cells = trimmed.Split('|');
            for (int c = 1; c < cells.Length; c++)
            {
                int open = cells[c].IndexOf('`');
                if (open < 0) continue;
                int close = cells[c].IndexOf('`', open + 1);
                if (close < 0) continue;
                string token = cells[c].Substring(open + 1, close - open - 1);
                if (IsPoseVectorPath(token) || IsOutputName(token))
                    if (!names.Contains(token)) names.Add(token);
            }
        }
        return names;
    }

    /// <summary>姿态向量的出口名带斜杠命名空间：`Face/Angle/X`、`Body/Pos/Y`。</summary>
    private static bool IsPoseVectorPath(string token) =>
        Regex.IsMatch(token, @"^(Face|Body)/(Angle|Pos)/[XYZ]$");

    /// <summary>
    /// 出口参数名 = 单个标识符（有可能带大写）。挡掉的都是说明性文字：
    /// `VTS_Compatible` / `AdvancedARKit_V3.0`（带下划线或点）、`0..1` / `-1..1`（点或负号）、
    /// `Rotation_*`（星号）、`EyeLeft_x`（坐标后缀）、`HoFaceTrackingChannels.Names`（点）、
    /// `ARKit/`（斜杠）。
    /// </summary>
    private static bool IsOutputName(string token) =>
        Regex.IsMatch(token, @"^[A-Za-z][A-Za-z0-9]*$");

    /// <summary>从运行目录往上找包根（认 package.json + Runtime 这个组合）。</summary>
    /// <summary>
    /// **控制器轴那一份（`Ho/Drive/*`，41 行 = 31 轴 + 5 门 + 4 切片 + 1 形态权重）算出来到底是什么。**
    ///
    /// 为什么值得单独一条：这 41 行是"手搭控制器时每个参数该是什么"的唯一依据，而它们**全是内联展开**的
    /// （输出行之间不能互相引用 ⇒ 切片权重把轴公式又抄了一遍）。抄错一个系数，别处都不会响：
    /// 台架只看"能不能解析、变量有没有来源"。这里用几个人工输入把**数值**钉住：
    ///  ① **静息**（所有源键 0）⇒ 31 根轴全 0、4 个区域门 = 1、4 条切片权重和 = 1；
    ///
    /// ⚠️ 这里的求值器**只有 `Ho/Drive/*` 行**（没有 `Ho/Style/*`）⇒ 两条 2026-09-27 加的**契约行**
    /// （`Ho/Drive/Gate/MouthStyle` = `out("Ho/Style/MouthGate")`、`Ho/Drive/Style/InvertedV`）
    /// 在这一段里算出来是 0（`out` 找不到"上面"）。它们的真值在「风格化链 / 鼓嘴链」那两段用
    /// **整份输出行 + 真 <see cref="HoFaceOutputTable"/>** 验 —— 这里只数它们的存在与分类。
    ///  ② **微笑**（`mouthSmile* = 0.8`）⇒ `Mouth/Form ≈ 0.8`（= 2×0.9−1，验的是那条重映射）；
    ///  ③ **漏斗 + 展唇**（`mouthFunnel = .6`、上唇抬 = .5）⇒ `Funnel` 轴 = .6、切片权重和仍 = 1 且各条 ∈ [0,1]。
    /// </summary>
    private static void ControllerAxesEvaluateAsDesigned()
    {
        string root = RepoRoot();
        if (root == null) return;
        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        if (!File.Exists(profilePath)) { Check("控制器轴：发货配置在包里", false, profilePath); return; }

        HoFaceMiddleware profile;
        string error;
        if (!Check("控制器轴：发货配置能读进来", HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error), error)) return;

        var rows = new List<HoFaceOutput>();
        foreach (var row in profile.outputs)
            if (row != null && row.parameter != null && row.parameter.StartsWith("Ho/Drive/", StringComparison.Ordinal))
                rows.Add(row);
        // 2026-09-28 傍晚：嘴角那两根删掉、`MouthWidth` 以 1D 3 格回来（§5.7.24）⇒ 40 行 / 29 轴。
        Check("控制器轴：行数 = 40（29 轴 + 5 门 + 4 切片权重 + 2 形态权重）", rows.Count == 40, "实际 " + rows.Count);
        if (rows.Count != 40) return;

        var compiled = new Dictionary<string, HoFaceExpression>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.expression)) continue;
            HoFaceExpression parsed;
            string why;
            if (!HoFaceExpression.TryParse(row.expression, out parsed, out why))
            {
                Check("控制器轴：" + row.parameter + " 的表达式能解析", false, why);
                return;
            }
            compiled[row.parameter] = parsed;
        }

        Func<Dictionary<string, float>, Dictionary<string, float>> evalAll = (sources) =>
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                // 与 HoFaceAnimationSession 同一条路径：常量行取 defaultValue（**不过曲线**），
                // 其余 = 曲线(表达式(源键…))。修饰符（平滑/维持）不动，这里是静态数值检查。
                float value = string.IsNullOrWhiteSpace(row.expression)
                    ? row.defaultValue
                    : row.Transform(compiled[row.parameter].Evaluate(
                        name => sources.TryGetValue(name, out float x) ? x : 0f));
                values[row.parameter] = value;
            }
            return values;
        };

        var axes = new List<string>();
        int gateRows = 0, sliceRows = 0, styleRows = 0;
        foreach (var row in rows)
        {
            if (row.parameter == null) continue;
            if (row.parameter.StartsWith("Ho/Drive/Gate/", StringComparison.Ordinal)) { gateRows++; continue; }
            if (row.parameter.StartsWith("Ho/Drive/Slice/", StringComparison.Ordinal)) { sliceRows++; continue; }
            if (row.parameter.StartsWith("Ho/Drive/Style/", StringComparison.Ordinal)) { styleRows++; continue; }
            axes.Add(row.parameter);
        }
        Check("控制器轴：轴参数 = 29 根（2026-09-28 傍晚：平移 `Mouth/X` `Mouth/Y` 留着，嘴角那两根删掉、"
            + "嘴宽改用既有的 `Mouth/Pucker`）", axes.Count == 29, "实际 " + axes.Count);
        Check("控制器轴：门 = 5 条（4 个区域门 + 1 条形态门 MouthStyle）", gateRows == 5, "实际 " + gateRows);
        Check("控制器轴：切片权重 = 4 条（MouthCore 的 2×2）", sliceRows == 4, "实际 " + sliceRows);
        Check("控制器轴：形态权重 = 2 条（Ho/Drive/Style/InvertedV 喂那棵 Simple1D；Ho/Drive/Style/CatMouth 喂变体开关）",
            styleRows == 2, "实际 " + styleRows);

        // 2026-09-27：左右眼并成一个区域、颊 → 鼻（注视/颊那几棵树删了）
        var gates = new[] { "Mouth", "Eye", "Brow", "Nose" };
        var slices = new[] { "Funnel0Press0", "Funnel1Press0", "Funnel0Press1", "Funnel1Press1" };
        Func<Dictionary<string, float>, float> sliceSum = (v) =>
        {
            float sum = 0f;
            foreach (var s in slices) sum += v["Ho/Drive/Slice/MouthCore/" + s];
            return sum;
        };

        // ① 静息
        var rest = evalAll(new Dictionary<string, float>(StringComparer.Ordinal));
        int notZero = 0;
        var notZeroText = new List<string>();
        foreach (var name in axes)
            if (Math.Abs(rest[name]) > 0.0005f) { notZero++; if (notZeroText.Count < 5) notZeroText.Add(name + "=" + rest[name].ToString("F3")); }
        Check("控制器轴：静息时 31 根轴都是 0（不是 0 的 " + notZero + "）", notZero == 0, string.Join(",", notZeroText.ToArray()));

        int badGate = 0;
        foreach (var g in gates) if (Math.Abs(rest["Ho/Drive/Gate/" + g] - 1f) > 0.0005f) badGate++;
        Check("控制器轴：静息时 4 个区域门都是 1（不是 1 的 " + badGate + "）", badGate == 0);
        Check("控制器轴：静息时 MouthCore 四条切片权重和 = 1（实际 " + sliceSum(rest).ToString("F4") + "）",
            Math.Abs(sliceSum(rest) - 1f) < 0.0005f);

        // ①b **实测静息偏置**（2026-09-27 用户先点设备校准、再连录三次静息 5 秒；取每根的 max = 最坏情况）。        //     设备静息时**并不发 0**：`browDown*` 到 0.2083、`noseSneer*` 到 0.1514。
        //     没有死区时 `Brow/*/Y` 会停在 −0.42（两档 X 轴 ⇒ "压眉"那格占 71%）、`Nose/Up` 停在 0.15。
        var restTakes = new (string Label, float Down, float Sneer)[]
        {
            ("A 01:03:41", 0.1338f, 0.1067f),
            ("B 01:04:20", 0.2046f, 0.0920f),
            ("C 01:04:42", 0.2083f, 0.1514f),
        };
        foreach (var take in restTakes)
        {
            var v = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "browDownLeft", take.Down }, { "browDownRight", take.Down },
                { "noseSneerLeft", take.Sneer }, { "noseSneerRight", take.Sneer },
            });
            Check("控制器轴：实测静息 " + take.Label + "（browDown " + take.Down.ToString("F4")
                + " / noseSneer " + take.Sneer.ToString("F4") + "）⇒ Brow/*/Y 与 Nose/Up 全归 0（实际 "
                + v["Ho/Drive/Brow/Left/Y"].ToString("F4") + " / " + v["Ho/Drive/Brow/Right/Y"].ToString("F4")
                + " / " + v["Ho/Drive/Nose/Up"].ToString("F4") + "）",
                Math.Abs(v["Ho/Drive/Brow/Left/Y"]) < 0.0005f
                && Math.Abs(v["Ho/Drive/Brow/Right/Y"]) < 0.0005f
                && Math.Abs(v["Ho/Drive/Nose/Up"]) < 0.0005f);
        }

        // ①c 死区**只掐静息那一带**：真动作的读数必须一点没少（这两个值是实测：皱眉/皱鼻 0.83、鼻上顶 0.70）。
        var frownHeld = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "browDownLeft", 0.83f }, { "browDownRight", 0.83f } });
        Check("控制器轴：真皱眉（browDown 0.83）⇒ Brow/Y = −1.66，与加死区前一致（实际 "
            + frownHeld["Ho/Drive/Brow/Left/Y"].ToString("F4") + "）",
            Math.Abs(frownHeld["Ho/Drive/Brow/Left/Y"] + 1.66f) < 0.002f);
        var knee = evalAll(new Dictionary<string, float>(StringComparer.Ordinal) { { "browDownLeft", 0.30f } });
        Check("控制器轴：死区之上恒等（browDown 0.30 ⇒ −0.60，实际 " + knee["Ho/Drive/Brow/Left/Y"].ToString("F4") + "）",
            Math.Abs(knee["Ho/Drive/Brow/Left/Y"] + 0.60f) < 0.002f);
        var noseHeld = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "noseSneerLeft", 0.70f }, { "noseSneerRight", 0.70f } });
        Check("控制器轴：真鼻上顶（noseSneer 0.70）⇒ Nose/Up = 0.70（1D 表那个 0.7 档还是精确命中，实际 "
            + noseHeld["Ho/Drive/Nose/Up"].ToString("F4") + "）",
            Math.Abs(noseHeld["Ho/Drive/Nose/Up"] - 0.70f) < 0.002f);

        // ①d **下巴前伸这一行现在只是「辅助变量 / 出口」（2026-09-27 用户定）** ——
        //     用户原话：「把这个 arkit 输入贬成只用来辅助的变量，他还是参与 arkit 直通就行，
        //     我们直接不做这个轴了，中心放到 jaw 的下左右上」。所以：
        //       · 表达式就是**裸线 `jawForward`**（纯直通，不做任何抑制、不减噘嘴）；
        //       · 那条 `MouthForward` 1D 表连片段一起删了（控制器 26→25 棵、88→86 槽位）；
        //       · 留着它是为了面板 / Warudo / 将来的公式能取到值。
        //     为什么不做（§5.4.1）：要张嘴才有值 / 张嘴本身又给值 / 噘嘴给 0.05 / 满档要噘嘴+前顶
        //     一起（0.14，裸线满量程只有 0.13）/ 安卓不发 —— VBridger 七份预设里 `jawForward`
        //     出现 **0 次**（连直通都不给）。下面三条把「纯直通、一个系数都不改」钉死：
        //     哪天要拿它做公式（减污染之类），是**故意**改这里，而不是哪天悄悄改坏。
        var forwardFull = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "jawForward", 0.14f }, { "mouthPucker", 0.975f } });
        Check("控制器轴：辅助变量 Forward 是纯直通（前顶 0.14 + 噘嘴 0.975 ⇒ 还是 0.14，一点不动）（实际 "
            + forwardFull["Ho/Drive/Mouth/Forward"].ToString("F4") + "）",
            Math.Abs(forwardFull["Ho/Drive/Mouth/Forward"] - 0.14f) < 0.002f);
        var forwardPuckerOnly = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "jawForward", 0.05f }, { "mouthPucker", 0.975f } });
        Check("控制器轴：噘嘴单独 ⇒ Forward = 0.05 原样（串扰不再有树去消费它）（实际 "
            + forwardPuckerOnly["Ho/Drive/Mouth/Forward"].ToString("F4") + "）",
            Math.Abs(forwardPuckerOnly["Ho/Drive/Mouth/Forward"] - 0.05f) < 0.002f);
        var forwardRest = evalAll(new Dictionary<string, float>(StringComparer.Ordinal) { { "mouthPucker", 0.26f } });
        Check("控制器轴：静息 Forward = 0（噘嘴基线 0.26 不参与）（实际 "
            + forwardRest["Ho/Drive/Mouth/Forward"].ToString("F4") + "）",
            Math.Abs(forwardRest["Ho/Drive/Mouth/Forward"]) < 0.0005f);

        // ①e **下颌两根轴的死区曲线**（2026-09-27 用户定「两个轴的曲线都加一点去噪，±0.05 都钳到 0」）——
        //     形状照 `Mouth/Open` 那条现成的：`|v| ≤ 0.05 → 0`、`0.05…0.08` 斜坡、之上恒等。
        //     四条实测分别落在这条曲线上（§5.4.2）：静息 −0.013 ⇒ 0 · 咬紧时真动 0.02–0.04 ⇒ 0 ·
        //     挤嘴角伪影 0.05–0.2 ⇒ 只有最低那一小截被掐掉、0.08 以上原样 · 微张真动 0.52 ⇒ 原样。
        Func<float, float> jawAxis = (v) => evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "jawOpen", v }, { "mouthClose", 0f } })["Ho/Drive/Mouth/Jaw"];
        Func<float, float> jawSideAxis = (v) => evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "jawRight", v }, { "jawLeft", 0f } })["Ho/Drive/Mouth/JawSide"];
        Check("控制器轴：下颌死区 —— ±0.05 以内全归 0（静息 −0.013 / 咬紧真动 0.03 / 咬合 −0.04 ⇒ 0；实际 "
            + jawSideAxis(-0.013f).ToString("F4") + " / " + jawSideAxis(0.03f).ToString("F4") + " / "
            + jawAxis(-0.04f).ToString("F4") + "）",
            Math.Abs(jawSideAxis(-0.013f)) < 0.0005f && Math.Abs(jawSideAxis(0.03f)) < 0.0005f
            && Math.Abs(jawAxis(-0.04f)) < 0.0005f);
        Check("控制器轴：下颌死区之外恒等 —— 微张真动 0.52 ⇒ 0.52、挤嘴角伪影 0.1 ⇒ 0.1、张满 0.75 ⇒ 0.75（实际 "
            + jawSideAxis(0.52f).ToString("F4") + " / " + jawSideAxis(0.1f).ToString("F4") + " / "
            + jawAxis(0.75f).ToString("F4") + "）",
            Math.Abs(jawSideAxis(0.52f) - 0.52f) < 0.002f && Math.Abs(jawSideAxis(0.1f) - 0.1f) < 0.002f
            && Math.Abs(jawAxis(0.75f) - 0.75f) < 0.002f);
        Check("控制器轴：下颌死区斜坡段只降不升（0.06 ⇒ 0 < v < 0.06，实际 " + jawSideAxis(0.06f).ToString("F4") + "）",
            jawSideAxis(0.06f) > 0f && jawSideAxis(0.06f) < 0.06f);
        // ①e2 **2026-09-28 实测修正**（18 段，`.research/takes-kubite.txt`）：用户报「闭嘴时下颌下拉、
        //      只表现下巴下移的动画**完全无法触发**」。实测那段：`jawOpen` **0.274~0.293** /
        //      `mouthClose` **0.279~0.296** ⇒ 差值 ≈0 ⇒ 死区 ⇒ 轴恒 0（信号明明有 0.28！）。
        //      ⇒ 正侧改用 `jawOpen` 本身，负侧保留差值（"嘴唇比下颌更用力压"才为负）。
        Func<float, float, float> jawPair = (jaw, close) => evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "jawOpen", jaw }, { "mouthClose", close } })["Ho/Drive/Mouth/Jaw"];
        Check("控制器轴：闭嘴下颌下拉（jawOpen 0.28 / mouthClose 0.28）⇒ " + jawPair(0.28f, 0.28f).ToString("F4")
            + "（旧口径是 0 ⇒ 那个动作永远触发不了）", Math.Abs(jawPair(0.28f, 0.28f) - 0.28f) < 0.002f);
        Check("控制器轴：张嘴类不再被嘴唇压紧抵消 —— 半张（0.43 / 0.067）⇒ " + jawPair(0.43f, 0.067f).ToString("F4")
            + "（旧口径 0.363）", Math.Abs(jawPair(0.43f, 0.067f) - 0.43f) < 0.002f);
        Check("控制器轴：负侧保留下来（咬合：jawOpen 0.03 / mouthClose 0.07 ⇒ 差值 −0.04 ⇒ 死区 0）（实际 "
            + jawPair(0.03f, 0.07f).ToString("F4") + "）", Math.Abs(jawPair(0.03f, 0.07f)) < 0.0005f);
        Check("控制器轴：挤嘴角对 `jawOpen` 没有伪影（实测 0.017 / mouthClose 0.011 ⇒ 死区 0）（实际 "
            + jawPair(0.017f, 0.011f).ToString("F4") + "）", Math.Abs(jawPair(0.017f, 0.011f)) < 0.0005f);
        // ⚠️ 代价钉一条：**咀嚼**（闭唇时 `jawOpen`/`mouthClose` 的振荡）**振幅已测 = 0.021~0.027**
        //     ⇒ 远在死区（0.05）之内 ⇒ **无论改不改都整个抹掉**（§5.4.2 第 3 项结案）。
        Check("控制器轴：下颌死区会吃掉咀嚼振幅（实测 jawOpen 0.021~0.027 ⇒ 0）—— **已知代价**（实际 "
            + jawPair(0.024f, 0.023f).ToString("F4") + "）", Math.Abs(jawPair(0.024f, 0.023f)) < 0.0005f);

        // ② 微笑：Form 必须落在正侧，且量级等于 2×MouthSmile−1
        var smileSources = new Dictionary<string, float>(StringComparer.Ordinal) { { "mouthSmileLeft", 0.8f }, { "mouthSmileRight", 0.8f } };
        var smile = evalAll(smileSources);
        float form = smile["Ho/Drive/Mouth/Form"];
        Check("控制器轴：微笑（两嘴角 0.8）时 Form ≈ 0.8（实际 " + form.ToString("F4") + "）", Math.Abs(form - 0.8f) < 0.002f);
        // ②a2 **整嘴平移 X**（2026-09-28 下午按新判据重建；41 段实测见 §5.7.23）：
        //     `(mouthLeft − mouthRight) + (smileL − smileR)` + 曲线 ±0.20 死区。
        //     实测：整嘴右移 `mouthLeft` 0.963~0.967 / 整嘴左移 `mouthRight` 0.967~0.976（≈0.96 满档，刻度 ±0.95），
        //     而同一个人的「嘴角往左/右撇」只有 0.047~0.136 ⇒ 缝 +0.594（旧版只有 +0.046）。
        Func<float, float, float, float, float, float, float> shiftXOf = (mL, mR, sL, sR, jaw, close) =>
            evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "mouthLeft", mL }, { "mouthRight", mR },
                { "mouthSmileLeft", sL }, { "mouthSmileRight", sR },
                { "jawOpen", jaw }, { "mouthClose", close }
            })["Ho/Drive/Mouth/X"];
        float shiftRight = shiftXOf(0.963f, 0.03f, 0.01f, 0.01f, 0.09f, 0.10f);   // 整嘴右移
        float shiftLeft = shiftXOf(0.01f, 0.970f, 0.01f, 0.01f, 0.15f, 0.16f);    // 整嘴左移
        float shiftPout = shiftXOf(0.005f, 0.10f, 0.001f, 0.001f, 0.016f, 0.011f); // 嘴角往左撇
        Check("控制器轴：整嘴平移 X（有符号）—— 整嘴右移 ⇒ " + shiftRight.ToString("F3") + "、整嘴左移 ⇒ "
            + shiftLeft.ToString("F3") + "（实测 0.96 满档 ⇒ ±0.9 以上）",
            shiftRight > 0.9f && shiftLeft < -0.9f);
        Check("控制器轴：整嘴平移 X 的死区 —— 嘴角往左撇（实测 0.047~0.136）⇒ " + shiftPout.ToString("F3") + " = 0",
            Near(shiftPout, 0f));
        // ②a3 **整嘴平移 Y**：正侧 = 噘嘴判据（膝 0.45 + 鼻门 0.54）、负侧 = 闭唇下颌下拉（膝 0.18 + 相对唇门）。
        //     ⚠️ 负侧的门是「嘴唇相对下颌是闭的」：说话那个瞬态（jawOpen 0.65 / mouthClose 0.28）必须归 0 ——
        //        这正是旧版「固定闭唇地板」被打穿的地方。
        // ⚠️ 2026-09-28 晚：正侧加了 funnel 门（只让"真撅"抬嘴）⇒ 这里按真撅给（实测 0.178~0.210）
        Func<float, float, float, float, float> shiftYOf = (pucker, nose, jaw, close) =>
            evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "mouthPucker", pucker }, { "noseSneerLeft", nose }, { "noseSneerRight", nose },
                { "jawOpen", jaw }, { "mouthClose", close }, { "mouthFunnel", 0.20f }
            })["Ho/Drive/Mouth/Y"];
        float yKiss = shiftYOf(0.98f, 0.10f, 0.08f, 0.09f);      // 噘嘴
        // ⚠️ 2026-09-28 晚：负侧膝 0.18 → **0.35**（用户实机报数：不张嘴咀嚼 `jawOpen` 一上来就到 0.2 左右，
        //    而真做那个动作要张到接近下颌下拉的极限）⇒ 负样本改用咀嚼峰值 0.20、正样本用 0.60。
        float yJawDrop = shiftYOf(0.28f, 0.13f, 0.60f, 0.60f);   // 闭嘴下颌下拉（张到接近极限）
        float yChewPeak = shiftYOf(0.20f, 0.12f, 0.20f, 0.19f);  // 咀嚼（不张嘴）的峰值 ⇒ 必须 0
        float ySpeech = shiftYOf(0.19f, 0.17f, 0.65f, 0.28f);    // 用力说话那一瞬（嘴唇没闭住）
        float yChew = shiftYOf(0.20f, 0.12f, 0.087f, 0.086f);    // 咀嚼（唇闭着但幅度小）
        float ySquint = shiftYOf(0.98f, 0.85f, 0.08f, 0.09f);    // 挤眼/皱鼻（pucker 高但鼻子在皱）
        Check("控制器轴：整嘴平移 Y —— 噘嘴 ⇒ " + yKiss.ToString("F3") + "（上移满档）、闭嘴下颌下拉 ⇒ "
            + yJawDrop.ToString("F3") + "（下移）", yKiss > 0.9f && yJawDrop < -0.8f);
        Check("控制器轴：整嘴平移 Y 的负侧门 —— 用力说话 ⇒ " + ySpeech.ToString("F3") + " = 0（嘴唇没闭住）、"
            + "咀嚼均值 ⇒ " + yChew.ToString("F3") + " = 0、**咀嚼峰值 0.20 ⇒ " + yChewPeak.ToString("F3")
            + " = 0**（膝 0.35：真做那个动作要张到接近极限才过门）",
            Near(ySpeech, 0f) && Near(yChew, 0f) && Near(yChewPeak, 0f));

        Check("控制器轴：整嘴平移 Y 的正侧鼻门 —— 挤眼/皱鼻（pucker 0.98 但 noseSneer 0.85）⇒ "
            + ySquint.ToString("F3") + " = 0", Near(ySquint, 0f));
        // ⭐ **2026-09-28 晚：正侧的 funnel 门**（用户报「收紧嘴不撅总是被识别为嘴向上移动」）——
        //    收嘴不撅（pucker 0.97 / funnel 0.081~0.096）与真撅（funnel 0.178~0.210）**只有 funnel 能分** ⇒
        //    **只有真撅抬嘴**、收嘴不撅不抬（它的上移量交给倒V 形态自己的美术）。
        //    ⚠️ 倒V 那边**不加**这道门 —— 加了它用户日常的噘嘴就永远不触发（实机报过）。
        Func<float, float, float> yUpOf = (pucker, funnel) =>
            evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "mouthPucker", pucker }, { "noseSneerLeft", 0.10f }, { "noseSneerRight", 0.10f },
                { "mouthFunnel", funnel }
            })["Ho/Drive/Mouth/Y"];
        // ⛔ **2026-09-28 深夜：正侧的 funnel 门也撤了**（用户实机：「嘴上移现在完全没有了」）——
        //    用户日常的噘嘴与「收紧嘴不撅」在设备上读数相同（`pucker` 0.97、funnel 都低）⇒ 门把两者一起挡死。
        //    ⇒ 正侧回到 pucker 判据：**只要噘就抬**（含"收紧嘴不撅"）。要分开得先补录找信号。
        Check("控制器轴：整嘴平移 Y 的正侧 —— 噘（pucker 0.976）⇒ " + yUpOf(0.976f, 0.088f).ToString("F3")
            + "（抬；funnel 0.088 也照样抬 —— funnel 门已撤）、且 funnel 高低不影响",
            yUpOf(0.976f, 0.088f) >= 0.8f && Near(yUpOf(0.976f, 0.19f), yUpOf(0.976f, 0.088f)));




        // ②b **负半轴 = sad**（2026-09-28 用户定：「sad 加入进 smile 的另一半轴」；"抿嘴/抿嘴下 = sad" 也认了）
        //     判据两段式：闭嘴走 `frown`、张嘴走 `stretch` 残差（张嘴时 `frown` 实测归零）。
        Func<float, float, float, float, float, float> sadForm = (frown, stretch, jaw, smile, dimple) =>
            evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "mouthFrownLeft", frown }, { "mouthFrownRight", frown },
                { "mouthStretchLeft", stretch }, { "mouthStretchRight", stretch },
                { "jawOpen", jaw },
                { "mouthSmileLeft", smile }, { "mouthSmileRight", smile },
                { "mouthDimpleLeft", dimple }, { "mouthDimpleRight", dimple }
            })["Ho/Drive/Mouth/Form"];
        float sadClosed = sadForm(0.53f, 0.12f, 0.017f, 0f, 0.04f);      // 苦闭嘴
        float sadHalf = sadForm(0f, 0.38f, 0.12f, 0.09f, 0.06f);          // 苦半张
        float sadWide = sadForm(0f, 0.83f, 0.55f, 0.10f, 0.12f);          // 苦大张
        float sadPress = sadForm(0.72f, 0.32f, 0.013f, 0f, 0.19f);        // 抿嘴下（**按语义就是 sad**）
        float sadNeutral = sadForm(0.02f, 0.03f, 0.011f, 0f, 0.02f);      // 静置
        float sadSpeak = sadForm(0.10f, 0.26f, 0.28f, 0.10f, 0.16f);      // 用力说话（干扰项）
        Check("控制器轴：负半轴 = sad —— 闭嘴苦（frown 0.53）⇒ Form " + sadClosed.ToString("F3")
            + "；**张嘴苦**（frown ≈0、stretch 0.38）⇒ " + sadHalf.ToString("F3") + "（旧口径这里是 0）",
            sadClosed < -0.35f && sadHalf < -0.25f);
        Check("控制器轴：苦大张（stretch 0.83 / 下颌 0.55）⇒ Form " + sadWide.ToString("F3") + " ≤ −0.6（负侧靠 stretch 残差撑住）",
            sadWide <= -0.6f);
        Check("控制器轴：抿嘴下（frown 0.72）⇒ Form " + sadPress.ToString("F3") + "（**按语义归 sad**，用户 2026-09-28 拍板）",
            sadPress < -0.6f);
        Check("控制器轴：静息 ⇒ Form " + sadNeutral.ToString("F3") + " ≈ 0、用力说话 ⇒ " + sadSpeak.ToString("F3")
            + "（干扰项不进负侧）", Math.Abs(sadNeutral) < 0.05f && sadSpeak > -0.35f);
        Check("控制器轴：微笑时 Open 不受影响（实际 " + smile["Ho/Drive/Mouth/Open"].ToString("F4") + "）",
            Math.Abs(smile["Ho/Drive/Mouth/Open"]) < 0.002f);


        // ②a4 **嘴宽（`Mouth/Pucker` 那根 1D 轴）**（2026-09-28 傍晚重建；9 段补录实测）：
        //     轴 = `2×dimple − pucker`，三档 = **窄 收嘴不撅 / 中 静态 / 宽 抿嘴嘴宽**。
        //     ⚠️ 另一根候选 `(press + shrug) / 2` 把「收嘴不撅」排在静态**之下**（0.09 < 0.17）⇒ 是反的、弃用。
        //     ⚠️ 树是 **1D 3 格**（`MouthWidth__Pucker__A3X{0,1,2}`）：左右那一维交给 `MouthShift`，不再拆两半。
        Func<float, float, float> puckerAxisOf = (dimple, pucker) =>
            evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "mouthDimpleLeft", dimple }, { "mouthDimpleRight", dimple }, { "mouthPucker", pucker }
            })["Ho/Drive/Mouth/Pucker"];
        float wNarrow = puckerAxisOf(0.011f, 0.976f);    // 收嘴不撅
        float wRest = puckerAxisOf(0.033f, 0.221f);      // 静态
        float wWide = puckerAxisOf(0.525f, 0.104f);      // 抿嘴嘴宽
        // ⚠️ 轴是 `2×(dimpleL + dimpleR) − pucker`（**2×和**）⇒ 两个输入相同 d 时 = `4d − pucker`，
        //    宽端量纲是 2（抿嘴嘴宽 1.996）⇒ 刻度 **−0.93 / −0.09 / +2.0**。
        Check("控制器轴：嘴宽轴（2×(dimpleL+dimpleR) − pucker）—— 收嘴不撅 ⇒ " + wNarrow.ToString("F3")
            + "（窄）、静态 ⇒ " + wRest.ToString("F3") + "（中）、抿嘴嘴宽 ⇒ " + wWide.ToString("F3")
            + "（宽）⇒ 刻度 −0.93 / −0.09 / +2.0",
            wNarrow < -0.85f && Math.Abs(wRest + 0.09f) < 0.05f && wWide > 1.8f);
        Check("控制器轴：嘴宽轴不误触 —— 笑（dimple 0.212 / pucker 0.066）⇒ " + puckerAxisOf(0.212f, 0.066f).ToString("F3")
            + "（中偏宽，与 `MouthCore` 的笑那两格叠着看）、苦（0.067 / 0.150）⇒ " + puckerAxisOf(0.067f, 0.150f).ToString("F3")
            + "（≈中）",
            puckerAxisOf(0.212f, 0.066f) > -0.3f && puckerAxisOf(0.212f, 0.066f) < 1.0f
            && puckerAxisOf(0.067f, 0.150f) > -0.3f && puckerAxisOf(0.067f, 0.150f) < 0.5f);


        // ②c 卷唇那根（MouthCoreRollSwitch 的输入）：**2026-09-28 判据整个换掉了**。

        //     36 段 5 秒实测（A 批 24 + B 批 12）证明旧判据 `√(上×下) × 死区 × (1+4·jawOpen²) × 噘嘴门`
        //     **不是这个动作的物理量**：
        //       · 用户的猫嘴 = **下唇内卷**（上唇几乎不卷 0.026~0.032）**+ 嘴角往后拉**；
        //       · 而"抿嘴（压紧）"的下唇卷是 0.362~0.396 —— 跟猫嘴（0.434~0.460）只差 1.1 倍；
        //       · "用力说话"的下唇卷 0.151~0.211 —— 单看唇卷会直接误触发。
        //     ⇒ 真正的分界线是**嘴角方向** `= (酒窝左+右)/2 − (苦左+右)/2`：猫嘴 0.358~0.503，
        //     而抿嘴 0.042~0.075、嘴角下扬的抿嘴 **−0.50**、用力说话 0.013~0.096、说话 0.064、
        //     张嘴咀嚼 0.031、张满 0.028、噘嘴 0.042、挤眼 0.070、闭唇咀嚼 ≈0。
        //     新判据 = `下唇卷 × clamp((嘴角方向 − 0.12) / 0.10, 0, 1)`（**两个条件都得成立**）。
        //     ⚠️ 膝 **0.12**（不是 0.20）：2026-09-28 用户报实机数字，做住 / 张嘴 / 大张三档的嘴角方向
        //     是 0.50 / 0.25 / 0.17。膝 0.20 时后两档只拿到门 0.5 / 0，读数 0.05 / 0 —— 掉到释放线
        //     0.02 附近甚至下面 ⇒「张嘴立刻就掉回去」。膝 0.12 后三档读数 0.40 / 0.10 / 0.044，
        //     分别是释放线的 20 / 5 / 2.2 倍，而最坏的非目标（用力说话 0.096、抿嘴 0.075）仍在膝下。
        // ⭐ **2026-09-28 改名 + 增量开关**：判据现在住在**风格行** `Ho/Style/CatMouth`
        //    （= 老判据 × HoAutoCatMouth + HoExternalCatMouth），发布给控制器的是契约行
        //    `Ho/Drive/Style/CatMouth` = `out("Ho/Style/CatMouth")` —— 与倒V / 鼓嘴同一个机制。
        //    所以这里**直接算风格行**（跟这台架里倒V / 鼓嘴两段的做法一样：自己给输入映射）。
        HoFaceOutput catStyleRow = null, catContractRow = null;
        foreach (var row in profile.outputs)
        {
            if (row == null || row.parameter == null) continue;
            if (row.parameter == "Ho/Style/CatMouth") catStyleRow = row;
            if (row.parameter == "Ho/Drive/Style/CatMouth") catContractRow = row;
        }
        Check("控制器轴：风格行 Ho/Style/CatMouth 在（判据住在这里）", catStyleRow != null);
        Check("控制器轴：契约行 Ho/Drive/Style/CatMouth 在（喂控制器那个变体开关）", catContractRow != null);
        if (catStyleRow == null || catContractRow == null) return;
        HoFaceExpression catStyleExpr = null;
        string catWhy = null;
        Check("控制器轴：Ho/Style/CatMouth 的表达式能解析",
            HoFaceExpression.TryParse(catStyleRow.expression, out catStyleExpr, out catWhy), catWhy);
        if (catStyleExpr == null) return;
        Check("控制器轴：契约行是 out(\"Ho/Style/CatMouth\")（与倒V 的契约行同形）",
            catContractRow.expression == "out(\"Ho/Style/CatMouth\")", catContractRow.expression);
        Check("控制器轴：契约行不挂修饰符（維持/平滑在风格行上，跟倒V / 鼓嘴一样）",
            catContractRow.modifiers == null || catContractRow.modifiers.Count == 0);
        // ⚠️ 输出表是"就近前置写者" ⇒ 风格行必须在契约行**之前**（不然 out() 读到 0）
        int catStyleIndex = -1, catContractIndex = -1;
        for (int i = 0; i < profile.outputs.Count; i++)
        {
            var row = profile.outputs[i];
            if (row == null) continue;
            if (row.parameter == "Ho/Style/CatMouth" && catStyleIndex < 0) catStyleIndex = i;
            if (row.parameter == "Ho/Drive/Style/CatMouth" && catContractIndex < 0) catContractIndex = i;
        }
        Check("控制器轴：风格行排在契约行之前（输出表就近前置写者，实际 " + catStyleIndex + " < " + catContractIndex + "）",
            catStyleIndex >= 0 && catStyleIndex < catContractIndex);

        // 自动 / 外部两个增量开关（默认 1 / 0 ⇒ 默认行为与本轮之前一样）
        Func<float, float, float, float, float, float, float> rollSwitch = (low, dimple, frown, upper, auto, external) =>
            catStyleExpr.Evaluate(name =>
                name == "mouthRollLower" ? low
                : name == "mouthDimpleLeft" || name == "mouthDimpleRight" ? dimple
                : name == "mouthFrownLeft" || name == "mouthFrownRight" ? frown
                : name == "mouthRollUpper" ? upper
                : name == "HoAutoCatMouth" ? auto
                : name == "HoExternalCatMouth" ? external : 0f);
        Func<float, float, float, float, float> rollOf = (low, dimple, frown, upper) =>
            rollSwitch(low, dimple, frown, upper, 1f, 0f);

        // ⭐ **常开**（`HoExternalCatMouth = 1`）：中性脸也算满档 —— 这就是"全量猫嘴"那条路
        //    （自动路径到不了「中性形张嘴」「大笑张嘴」那几格，常开之后 8 格全可达）。
        float forcedNeutral = rollSwitch(0f, 0f, 0f, 0f, 0f, 1f);
        float forcedAutoOff = rollSwitch(0.4595f, 0.7774f, 0.3390f, 0f, 0f, 1f);
        Check("控制器轴：常开（HoExternalCatMouth = 1）⇒ 中性脸也是 " + forcedNeutral.ToString("F3")
            + " ≥ 满档 0.30（" + "自动关掉也一样：" + forcedAutoOff.ToString("F3") + "）",
            forcedNeutral >= 0.30f && forcedAutoOff >= 0.30f);
        // ⭐ **关自动**（`HoAutoCatMouth = 0`、外部也是 0）：真猫嘴读数也点不亮
        float autoOff = rollSwitch(0.4595f, 0.7774f, 0.3390f, 0f, 0f, 0f);
        Check("控制器轴：关掉自动（HoAutoCatMouth = 0）+ 不常开 ⇒ 真猫嘴读数也是 " + autoOff.ToString("F3") + " = 0",
            Near(autoOff, 0f));
        // ⭐ 默认那一份（auto = 1 / external = 0）= 本轮之前的行为：判据说了算
        Check("控制器轴：默认（自动开 / 不常开）⇒ 与老口径一致（猫嘴 " + rollOf(0.4595f, 0.7774f, 0.3390f, 0.0257f).ToString("F4")
            + " / 静息 " + rollOf(0.0196f, 0.0304f, 0.0231f, 0f).ToString("F4") + "）",
            Near(rollOf(0.4595f, 0.7774f, 0.3390f, 0.0257f), 0.4595f) && Near(rollOf(0.0196f, 0.0304f, 0.0231f, 0f), 0f));

        // ⭐ 目标侧：猫嘴三段（段内均值）⇒ 门全开（嘴角方向 0.4384 / 0.5027 / 0.3583）⇒ 就等于下唇卷
        float cat1 = rollOf(0.4595f, 0.7774f, 0.3390f, 0.0257f);
        float cat2 = rollOf(0.4460f, 0.8414f, 0.3387f, 0.0319f);
        float cat3 = rollOf(0.4338f, 0.8037f, 0.4454f, 0.0297f);
        Check("控制器轴：猫嘴三段（实测均值）⇒ Roll = 0.4595 / 0.4460 / 0.4338（实际 "
            + cat1.ToString("F4") + " / " + cat2.ToString("F4") + " / " + cat3.ToString("F4") + "）",
            Near(cat1, 0.4595f) && Near(cat2, 0.4460f) && Near(cat3, 0.4338f));
        Check("控制器轴：猫嘴三段都过开关满档 0.30（这才是「干脆地进」）",
            cat1 >= 0.30f && cat2 >= 0.30f && cat3 >= 0.30f);

        // ⚠️ 最坏的非目标：**常态笑** —— 笑会把嘴角门打开（酒窝 0.374、苦 0），但下唇几乎不卷（0.075）
        //    ⇒ 乘积 0.075，离开关起点 0.15 还有 2 倍。这就是"两个条件都得成立"买到的东西。
        float smileAxis = rollOf(0.0750f, 0.3740f, 0f, 0f);
        Check("控制器轴：常态笑（下唇 0.075 / 酒窝 0.374 / 苦 0）⇒ " + smileAxis.ToString("F4") + " < 起点 0.15",
            smileAxis < 0.15f);

        // ⚠️ 抿嘴（双唇压紧）：下唇卷 0.362~0.396（≈猫嘴！）但**苦与酒窝一起高**（0.59~0.62 / 0.66）
        //    ⇒ 嘴角方向只有 0.042~0.075 ⇒ 门关 ⇒ 0。**旧判据就是被它骗的。**
        float pressA = rollOf(0.3622f, 0.6676f, 0.5919f, 0.0180f);
        float pressB = rollOf(0.3958f, 0.6699f, 0.6082f, 0.0239f);
        Check("控制器轴：抿嘴（压紧）下唇卷 0.36~0.40 但嘴角方向 ≤0.075 ⇒ 0（实际 "
            + pressA.ToString("F4") + " / " + pressB.ToString("F4") + "）",
            Near(pressA, 0f) && Near(pressB, 0f));
        // ⚠️ 抿嘴（嘴角下扬，B 批）：嘴角方向 **−0.50**，下唇卷还有 0.226 ⇒ 仍然 0（负方向被 clamp 到 0）
        float pressDown = rollOf(0.2260f, 0.1920f, 0.6900f, 0f);
        Check("控制器轴：嘴角下扬的抿嘴（嘴角方向 −0.498、下唇卷 0.226）⇒ " + pressDown.ToString("F4") + " = 0",
            Near(pressDown, 0f));
        // ⚠️ 用力说话（B 批）：下唇卷 **0.211**（比猫嘴的一半还多！）但嘴角方向只有 0.091 ⇒ 门关 ⇒ 0
        float speakHard = rollOf(0.2110f, 0.1770f, 0.0860f, 0.1680f);
        Check("控制器轴：用力说话（下唇卷 0.211 —— **单看唇卷会误触发**）⇒ " + speakHard.ToString("F4") + " = 0",
            Near(speakHard, 0f));

        // 其余非目标（说话 / 张嘴咀嚼 / 张满 / 噘嘴 / 挤眼 / 闭唇咀嚼）全部 = 0
        float speak = rollOf(0.0536f, 0.0646f, 0.0005f, 0.0511f);
        float chewOpen = rollOf(0.0180f, 0.0309f, 0f, 0.0139f);
        float wideOpen = rollOf(0.0198f, 0.0349f, 0.0069f, 0.0128f);
        float puckerOnly = rollOf(0.1192f, 0.0818f, 0.0397f, 0f);
        float squintOnly = rollOf(0.0988f, 0.0697f, 0f, 0.0278f);
        float chewClosed = rollOf(0.0234f, 0.0252f, 0.0270f, 0.0105f);
        Check("控制器轴：说话 / 张嘴咀嚼 / 张满 / 噘嘴 / 挤眼 / 闭唇咀嚼 ⇒ 全 0（实际 "
            + speak.ToString("F4") + " / " + chewOpen.ToString("F4") + " / " + wideOpen.ToString("F4") + " / "
            + puckerOnly.ToString("F4") + " / " + squintOnly.ToString("F4") + " / " + chewClosed.ToString("F4") + "）",
            Near(speak, 0f) && Near(chewOpen, 0f) && Near(wideOpen, 0f)
            && Near(puckerOnly, 0f) && Near(squintOnly, 0f) && Near(chewClosed, 0f));

        // 上唇卷**不参与**（旧判据要求两根一起卷，而用户的上唇几乎不动 0.026~0.032 ⇒ 旧判据把它砍掉了一半）
        float upperOnly = rollOf(0f, 0.80f, 0.40f, 0.90f);
        Check("控制器轴：上唇卷 0.90 完全不影响（只看下唇卷）⇒ " + upperOnly.ToString("F4") + " = 0",
            Near(upperOnly, 0f));

        // 嘴角门的边界（膝 0.12 / 宽 0.10）：方向 0.12 ⇒ 门 0；0.22 ⇒ 门全开（0.40 × 1 = 0.40）
        float gateLow = rollOf(0.40f, 0.52f, 0.40f, 0f);    // 0.52 − 0.40 = 0.12
        float gateHigh = rollOf(0.40f, 0.62f, 0.40f, 0f);   // 0.62 − 0.40 = 0.22
        Check("控制器轴：嘴角门边界 —— 方向 0.12 ⇒ 0、0.22 ⇒ 全开（实际 "
            + gateLow.ToString("F4") + " / " + gateHigh.ToString("F4") + "）",
            Near(gateLow, 0f) && Near(gateHigh, 0.40f));
        // ⚠️ 膝降到 0.12 之后**最薄的那条余量**就是它：用力说话的最坏嘴角方向 0.096（单段 max 口径）
        //    ⇒ 离开膝还有 20%。真被顶到就再抬膝（0.14）或把 trigger 抬到 0.20。
        float hardSpeechCorner = rollOf(0.211f, 0.177f, 0.081f, 0.168f);   // 嘴角方向 0.096
        Check("控制器轴：用力说话最坏嘴角方向 0.096 < 膝 0.12（余量 "
            + ((0.12f - 0.096f) / 0.12f * 100f).ToString("F0") + "%）⇒ " + hardSpeechCorner.ToString("F4") + " = 0",
            Near(hardSpeechCorner, 0f));
        // 下颌**完全不参与**（旧判据那个 (1+4·jawOpen²) 会把说话/张满一起放大：实测把说话顶到 0.35）
        float jawFree = rollOf(0.45f, 0.80f, 0.40f, 0f);
        Check("控制器轴：下颌不再参与（同一猫嘴在闭嘴下 = " + jawFree.ToString("F4") + " = 0.45）",
            Near(jawFree, 0.45f));

        // ④ **时域**（2026-09-28 用户实机反馈「上下卷嘴唇张口时会一直闪」）：只挂 0.009s 平滑时，
        //    读数会在开关起点 0.15 附近来回穿 ⇒ 两张整嘴表每帧来回淡入淡出。⇒ 按倒V / 鼓嘴
        //    **同一套**给这根轴挂「维持」（Schmitt：trigger 0.15 / threshold 0.10 ⇒ 释放 0.05 /
        //    hold 0.1）+「平滑 0.08s」：输出只有 0 或 1，开关不再半开半关。
        // ⚠️ 2026-09-28 改名之后**維持/平滑住在风格行** `Ho/Style/CatMouth` 上（跟倒V / 鼓嘴同形）：
        //    契约行只做 out(...) 转发、不挂修饰符（下面另有一条断言钉它）。
        HoFaceOutput rollRow = catStyleRow;
        Check("控制器轴：Ho/Style/CatMouth 这一行在（时域检查用）", rollRow != null);
        int rollSteps = 0, rollSmooth = 0, rollStepsIndex = -1, rollSmoothIndex = -1;
        float rollTrigger = 0f, rollThreshold = 0f, rollHold = 0f, rollTarget = 0f, rollSmoothSeconds = 0f;
        if (rollRow != null && rollRow.modifiers != null)
            for (int i = 0; i < rollRow.modifiers.Count; i++)
            {
                var modifier = rollRow.modifiers[i];
                if (modifier == null) continue;
                if (modifier.kind == HoFaceModifierKind.Steps)
                {
                    rollStepsIndex = i;
                    if (modifier.steps != null && modifier.steps.Count > 0)
                    {
                        rollSteps += modifier.steps.Count;
                        rollTrigger = modifier.steps[0].trigger;
                        rollThreshold = modifier.steps[0].threshold;
                        rollHold = modifier.steps[0].hold;
                        rollTarget = modifier.steps[0].target;
                    }
                }
                if (modifier.kind == HoFaceModifierKind.Smooth && modifier.Active) { rollSmoothIndex = i; rollSmooth++; rollSmoothSeconds = modifier.seconds; }
            }
        // ⭐ 触发线 **0.15 → 0.28**（2026-09-28 深夜）：`维持` 的输出是二值 ⇒ 这一行实际是「0/1 + 迟滞」，
        //    「切不切」只看 trigger；释放带跟着抬到 0.26 ⇒ 释放点仍是 0.02（用户要的锁存没动）。
        Check("控制器轴：Mouth/Roll 挂了一个「维持」= trigger 0.28 / threshold 0.26（释放 0.02）/ hold 0.2 / target 1（实际 "
            + rollTrigger.ToString("F2") + " / " + rollThreshold.ToString("F2") + " / " + rollHold.ToString("F2") + " / " + rollTarget.ToString("F1") + "）",
            rollSteps == 1 && Near(rollTrigger, 0.28f) && Near(rollThreshold, 0.26f)
            && Near(rollHold, 0.2f) && Near(rollTarget, 1f));
        // ⭐ 释放线 **0.02**（= trigger − threshold）：2026-09-28 用户「很难维持表演这个猫嘴」⇒
        //    触发线不放松（仍然 0.15，对非目标的约束不变），但点亮之后只有读数掉到 0.02 以下才松
        //    （静息实测是精确 0）⇒ 表演全程挂得住、松手立刻关。
        Check("控制器轴：Mouth/Roll 的释放线 = trigger − threshold = 0.02（贴地锁存，实际 "
            + (rollTrigger - rollThreshold).ToString("F3") + "）",
            Near(rollTrigger - rollThreshold, 0.02f));
        Check("控制器轴：Mouth/Roll 的平滑 = 0.08s（实际 " + rollSmoothSeconds.ToString("F3") + "s，手感参、与两条形态同口径）",
            rollSmooth == 1 && Near(rollSmoothSeconds, 0.08f));
        Check("控制器轴：Mouth/Roll 是「先维持、后平滑」（维持 #" + rollStepsIndex + " / 平滑 #" + rollSmoothIndex + "）",
            rollStepsIndex >= 0 && rollSmoothIndex == rollStepsIndex + 1);

        // ④b **下颌增益又加回来了（k = 3）**（2026-09-28 用户：「咬唇下张大嘴他又会回弹到没猫嘴 …… 很难维持表演」）：
        //     设备在张嘴时会把两根唇线压低 ⇒ 读数随下颌开度掉，而原来的 `下唇卷 × 嘴角门` 一掉就释放。
        //     ⚠️ 它现在**只对过了嘴角门的读数**起作用（所以 36 段里"下颌动得大"的动作全不受影响）。
        Func<float, float, float, float, float, float> rollJaw = (low, dimple, frown, jaw, upper) =>
            catStyleExpr.Evaluate(name =>
                name == "mouthRollLower" ? low
                : name == "mouthDimpleLeft" || name == "mouthDimpleRight" ? dimple
                : name == "mouthFrownLeft" || name == "mouthFrownRight" ? frown
                : name == "jawOpen" ? jaw
                : name == "mouthRollUpper" ? upper
                : name == "HoAutoCatMouth" ? 1f
                : name == "HoExternalCatMouth" ? 0f : 0f);
        // 张嘴 0.7 ⇒ 增益 ×2.47：一个"张嘴把读数压到 0.08 的猫嘴"从够不着触发线（0.08 < 0.15）
        // 变成够得着（0.198 ≥ 0.15）—— 这就是这次要修的那件事。
        float openMouthCat = rollJaw(0.08f, 0.80f, 0.40f, 0.7f, 0f);
        Check("控制器轴：张嘴把读数压到 0.08 的猫嘴（下颌 0.7）⇒ 补回 " + openMouthCat.ToString("F4") + " ≥ 触发线 0.15",
            openMouthCat >= 0.15f);
        // 而"下颌动得大"的干扰项**门就是 0** ⇒ 增益乘不进去（用力说话下颌 0.30、张满下颌 0.79）
        float hardSpeech = rollJaw(0.211f, 0.177f, 0.086f, 0.30f, 0.168f);
        float wideJaw = rollJaw(0.020f, 0.035f, 0.007f, 0.79f, 0.011f);
        Check("控制器轴：增益救不活被门挡掉的 —— 用力说话（下颌 0.30）= " + hardSpeech.ToString("F4")
            + "、张满（下颌 0.79）= " + wideJaw.ToString("F4") + "，都还是 0",
            Near(hardSpeech, 0f) && Near(wideJaw, 0f));
        // 实测的笑：下颌只有 0.014~0.022 ⇒ 增益 ≈1.00，等于没加（仍然 < 触发线 0.15）
        float smileGain = rollJaw(0.075f, 0.374f, 0f, 0.02f, 0f);
        Check("控制器轴：实测常态笑（下颌 0.02）⇒ 增益 ≈1 ⇒ " + smileGain.ToString("F4") + " 仍 < 0.15",
            smileGain < 0.15f);
        // ⚠️ **已知代价**（故意钉住，别当成回归）：万一出现「张大嘴的笑」（下颌 0.7），增益会把它顶过触发线。
        //    真出现：k 降到 1、或 trigger 抬到 0.20。台架把它写成"已知代价"而不是"通过"。
        float wideSmile = rollJaw(0.075f, 0.374f, 0f, 0.7f, 0f);
        Check("控制器轴：⚠️ 已知代价 —— 张大嘴的笑（下颌 0.7）会被顶到 " + wideSmile.ToString("F4")
            + " ≥ 0.15（目前实测的笑下颌只有 0.014~0.022，余量很大）", wideSmile >= 0.15f);

        // ④c **实机三档（2026-09-28 用户报数）**：做住 rollLower 0.40 / 酒窝 0.50 / 苦 0；张嘴 0.10 / 0.25 / 0；
        //     大张 0.05 / 0.17 / 0（下颌没报，大张按 0.7 算增益、另外单查一次不带增益的最坏情况）。
        //     ⭐ 判据：这三档**全部要 ≥ 释放线 0.02**，否则门一松就弹回 —— 用户原话「张嘴立刻就掉回去，直接 0」。
        float liveHold = rollJaw(0.40f, 0.50f, 0f, 0f, 0f);
        float liveOpen = rollJaw(0.10f, 0.25f, 0f, 0.3f, 0f);
        float liveWide = rollJaw(0.05f, 0.17f, 0f, 0.7f, 0f);
        Check("控制器轴：实机三档（做住 / 张嘴 / 大张）⇒ " + liveHold.ToString("F3") + " / " + liveOpen.ToString("F3")
            + " / " + liveWide.ToString("F3") + "，全部 ≥ 释放线 0.02（做住还得 ≥ 触发线 0.15）",
            liveHold >= 0.15f && liveOpen > 0.02f && liveWide > 0.02f);
        float liveWideNoJaw = rollJaw(0.05f, 0.17f, 0f, 0f, 0f);
        Check("控制器轴：大张**最坏情况（没有下颌增益）**仍有 " + liveWideNoJaw.ToString("F3") + " > 0.02",
            liveWideNoJaw > 0.02f);

        // 反向耦合**是**存在的、而且是故意的：卷唇会压低 `Mouth/Open`（公式里那个 −0.2×roll）。
        // 注意它吃的是**原始通道值**（不是这根补偿过的轴）⇒ 增益不会形成回路、也不会二次计入。
        // 两根线各 0.18 ⇒ 0.2×(0.18+0.18) = 0.072：闭嘴时 Open = −0.072、张满时 Open = 1 − 0.072 = 0.928。
        var openNoJaw = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "mouthRollUpper", 0.18f }, { "mouthRollLower", 0.18f } });
        var openWithJaw = evalAll(new Dictionary<string, float>(StringComparer.Ordinal)
            { { "mouthRollUpper", 0.18f }, { "mouthRollLower", 0.18f }, { "jawOpen", 1f } });
        Check("控制器轴：反过来是有的 —— 卷唇各 0.18 把 Open 压低 0.072（闭嘴时 Open = "
            + openNoJaw["Ho/Drive/Mouth/Open"].ToString("F4") + "）",
            Math.Abs(openNoJaw["Ho/Drive/Mouth/Open"] + 0.072f) < 0.002f);
        Check("控制器轴：同一卷唇量在张满时只是把 Open 从 1 拉到 0.928（实际 "
            + openWithJaw["Ho/Drive/Mouth/Open"].ToString("F4") + "）",
            Math.Abs(openWithJaw["Ho/Drive/Mouth/Open"] - 0.928f) < 0.002f);

        // ③ 漏斗 + 上唇抬起：Funnel 轴 = 声明值，切片权重仍是分区（和 = 1、每条 ∈ [0,1]）
        var posedSources = new Dictionary<string, float>(StringComparer.Ordinal)
            { { "mouthFunnel", 0.6f }, { "mouthUpperUpLeft", 0.5f }, { "mouthUpperUpRight", 0.5f } };
        var posed = evalAll(posedSources);
        Check("控制器轴：漏斗 0.6 时 Funnel = 0.6（实际 " + posed["Ho/Drive/Mouth/Funnel"].ToString("F4") + "）",
            Math.Abs(posed["Ho/Drive/Mouth/Funnel"] - 0.6f) < 0.002f);
        Check("控制器轴：有姿势时切片权重和仍 = 1（实际 " + sliceSum(posed).ToString("F4") + "）",
            Math.Abs(sliceSum(posed) - 1f) < 0.0005f);
        int outOfRange = 0;
        var outOfRangeText = new List<string>();
        foreach (var s in slices)
        {
            float v = posed["Ho/Drive/Slice/MouthCore/" + s];
            if (v < -0.0005f || v > 1.0005f) { outOfRange++; outOfRangeText.Add(s + "=" + v.ToString("F3")); }
        }
        Check("控制器轴：每条切片权重都在 [0,1]（越界的 " + outOfRange + "）", outOfRange == 0, string.Join(",", outOfRangeText.ToArray()));

        // 面板/报告里能一眼看到静息与"笑"那两组数
        Console.WriteLine("      静息 → Form " + rest["Ho/Drive/Mouth/Form"].ToString("F3")
            + " · Open " + rest["Ho/Drive/Mouth/Open"].ToString("F3")
            + " · Funnel " + rest["Ho/Drive/Mouth/Funnel"].ToString("F3")
            + " · Press " + rest["Ho/Drive/Mouth/Press"].ToString("F3")
            + " · BrowL/Y " + rest["Ho/Drive/Brow/Left/Y"].ToString("F3")
            + " · 切片和 " + sliceSum(rest).ToString("F3"));
        Console.WriteLine("      微笑 → Form " + form.ToString("F3")
            + " · 切片和 " + sliceSum(smile).ToString("F3"));
        Console.WriteLine("      漏斗+展唇 → Funnel " + posed["Ho/Drive/Mouth/Funnel"].ToString("F3")
            + " · 切片和 " + sliceSum(posed).ToString("F3")
            + " · F1P1 " + posed["Ho/Drive/Slice/MouthCore/Funnel1Press1"].ToString("F3"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "Runtime"))) return dir.FullName;
        }
        return null;
    }

    /// <summary>
    /// 用例自己的小夹具。**仓库里没有"内置默认表"了**（2026-09-26 删掉 `HoFaceMiddlewareDefaults`，
    /// 理由见 `docs/FACE_TRACKING_MIDDLE_LAYER.md` §6.1：默认配置按别的设备的量纲写死了换算，
    /// 用错了不报错），所以凡是需要"一份有内容的配置"的断言都用这个 —— 它长在用例里，不参与发货。
    ///
    /// 形状故意覆盖三样最容易在往返里丢的东西：两种线名写法、一根 −1..1 的双向曲线、一条修饰符链。
    /// </summary>
    private static HoFaceMiddleware Fixture()
    {
        var middleware = new HoFaceMiddleware
        {
            displayName = "test-fixture",
            notes = "用例夹具（不是发货配置）",
        };

        middleware.inputs.Add(new HoFaceOutput { parameter = "jawOpen", expression = "jawOpen * 0.01", notes = "iFacialMocap" });
        middleware.inputs.Add(new HoFaceOutput { parameter = "jawOpen", expression = "JawOpen", notes = "VTS 手机" });
        middleware.inputs.Add(new HoFaceOutput { parameter = "headRotX", expression = "Rotation_x", notes = "VTS 手机" });

        middleware.outputs.Add(new HoFaceOutput { parameter = "ARKit/tongueOut", expression = "tongueOut" });
        var lid = new HoFaceOutput
        {
            parameter = "Ho/Drive/Lid/Left/BlinkWide",
            expression = "eyeBlinkLeft - eyeWideLeft",
            curve = HoFaceCurve.SignedIdentity(),
        };
        middleware.outputs.Add(lid);
        var shaped = new HoFaceOutput { parameter = "MouthOpen", expression = "jawOpen" };
        shaped.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = 0.03f });
        middleware.outputs.Add(shaped);

        return middleware;
    }

    private static void WriteContainsBothArrays()
    {
        var text = HoFaceProfile.Write(Fixture());
        Check("写出来的文本带 \"inputs\"", text.Contains("\"inputs\""));
        Check("写出来的文本带 \"outputs\"", text.Contains("\"outputs\""));
        Check("写出来的文本不是只有头部", text.Contains("\"modifiers\"") && text.Contains("\"curve\""), "长度=" + text.Length);
    }

    private static void FixtureRoundTrip()
    {
        var text = HoFaceProfile.Write(Fixture());
        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse(text, out parsed, out error);

        Check("夹具能解析回来", ok, error);
        if (!ok) return;

        Check("输入行 = 3", parsed.inputs.Count == 3, "实际 " + parsed.inputs.Count);
        Check("输出行 = 3", parsed.outputs.Count == 3, "实际 " + parsed.outputs.Count);
        Check("属性名与行数都照原样（displayName / notes）",
            parsed.displayName == "test-fixture" && parsed.notes == "用例夹具（不是发货配置）",
            parsed.displayName + " / " + parsed.notes);
        Check("无致命错误", error == null, error);
    }

    private static void RowContentIsFaithful()
    {
        HoFaceMiddleware parsed;
        string error;
        if (!HoFaceProfile.TryParse(HoFaceProfile.Write(Fixture()), out parsed, out error)) return;

        var ifacial = Find(parsed.inputs, "jawOpen", "iFacialMocap");
        Check("有 iFacialMocap 的 jawOpen 输入行", ifacial != null);
        if (ifacial != null) Check("它的表达式是 'jawOpen * 0.01'", ifacial.expression == "jawOpen * 0.01", ifacial.expression);

        var vts = Find(parsed.inputs, "jawOpen", "VTS 手机");
        Check("有 VTS 手机的 jawOpen 输入行", vts != null);
        if (vts != null) Check("它的表达式是 'JawOpen'", vts.expression == "JawOpen", vts.expression);

        var head = Find(parsed.inputs, "headRotX", "VTS 手机");
        Check("VTS 的 Rotation_x 映射到 headRotX", head != null && head.expression == "Rotation_x",
            head != null ? head.expression : "（没找到）");

        var arkit = Find(parsed.outputs, "ARKit/tongueOut", null);
        Check("输出行有 ARKit/tongueOut", arkit != null);
        if (arkit != null) Check("它是直通表达式", arkit.expression == "tongueOut", arkit.expression);

        var lid = Find(parsed.outputs, "Ho/Drive/Lid/Left/BlinkWide", null);
        Check("输出行有眼睑开合轴", lid != null);
        if (lid != null)
        {
            Check("它的表达式是 blink - wide", lid.expression == "eyeBlinkLeft - eyeWideLeft", lid.expression);
            Check("它的曲线是 −1..1 端点（Transfer(-1) == -1）", Near(HoFaceCurve.Transfer(lid.curve, -1f), -1f));
            Check("它的曲线是 −1..1 端点（Transfer(1) == 1）", Near(HoFaceCurve.Transfer(lid.curve, 1f), 1f));
            Check("范围外按端点算（Transfer(5) == 1）", Near(HoFaceCurve.Transfer(lid.curve, 5f), 1f));
        }

        var shaped = Find(parsed.outputs, "MouthOpen", null);
        Check("修饰符链还在（1 条）", shaped != null && shaped.modifiers.Count == 1
            && shaped.modifiers[0].kind == HoFaceModifierKind.Smooth, shaped != null ? shaped.modifiers.Count.ToString() : "（没找到）");
        if (shaped != null && shaped.modifiers.Count == 1)
            Check("时长按秒存", Near(shaped.modifiers[0].seconds, 0.03f), shaped.modifiers[0].seconds.ToString());
    }

    private static void CurveRoundTrip()
    {
        var middleware = new HoFaceMiddleware { displayName = "curve-test" };
        var row = new HoFaceOutput { parameter = "X", expression = "1" };
        row.curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.5f),
            new Keyframe(0.5f, 0.25f, 0.5f, 2f),
            new Keyframe(1f, 1f, 2f, 0f));
        middleware.outputs.Add(row);

        HoFaceMiddleware parsed;
        string error;
        if (!Check("曲线用例能解析回来", HoFaceProfile.TryParse(HoFaceProfile.Write(middleware), out parsed, out error), error)) return;

        var back = Find(parsed.outputs, "X", null);
        Check("曲线行还在", back != null);
        if (back == null) return;

        Check("关键点个数 = 3", back.curve != null && back.curve.length == 3, back.curve != null ? back.curve.length.ToString() : "curve=null");
        if (back.curve == null || back.curve.length != 3) return;

        var keys = back.curve.keys;
        Check("key[1].time ≈ 0.5", Near(keys[1].time, 0.5f), keys[1].time.ToString());
        Check("key[1].value ≈ 0.25", Near(keys[1].value, 0.25f), keys[1].value.ToString());
        Check("key[1].inT ≈ 0.5", Near(keys[1].inTangent, 0.5f), keys[1].inTangent.ToString());
        Check("key[2].outT ≈ 0", Near(keys[2].outTangent, 0f), keys[2].outTangent.ToString());
        Check("中点求值 ≈ 0.25", Near(HoFaceCurve.Transfer(back.curve, 0.5f), 0.25f),
            HoFaceCurve.Transfer(back.curve, 0.5f).ToString());
    }

    private static void ModifierRoundTrip()
    {
        var middleware = new HoFaceMiddleware { displayName = "modifier-test" };

        var smooth = new HoFaceOutput { parameter = "S", expression = "1" };
        smooth.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = 0.03f });

        var steps = new HoFaceOutput { parameter = "T", expression = "1" };
        var stepModifier = new HoFaceModifier { kind = HoFaceModifierKind.Steps };
        stepModifier.steps.Add(new HoFaceStep { trigger = 0.3f, target = 0.4f, hold = 0.15f, threshold = 0.1f });
        stepModifier.steps.Add(new HoFaceStep { trigger = 0.7f, target = 1f, hold = 0f, threshold = 0.05f });
        steps.modifiers.Add(stepModifier);

        var ordered = new HoFaceOutput { parameter = "U", expression = "1" };
        ordered.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = 0.02f });
        ordered.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Steps });
        ordered.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Delay, seconds = 0.1f });

        middleware.outputs.Add(smooth);
        middleware.outputs.Add(steps);
        middleware.outputs.Add(ordered);

        HoFaceMiddleware parsed;
        string error;
        if (!Check("修饰符用例能解析回来", HoFaceProfile.TryParse(HoFaceProfile.Write(middleware), out parsed, out error), error)) return;

        var smoothBack = Find(parsed.outputs, "S", null);
        Check("smooth 还在且时长对", smoothBack != null && smoothBack.modifiers.Count == 1
            && smoothBack.modifiers[0].kind == HoFaceModifierKind.Smooth
            && Near(smoothBack.modifiers[0].seconds, 0.03f));

        var stepsBack = Find(parsed.outputs, "T", null);
        Check("steps 的两档都在", stepsBack != null && stepsBack.modifiers.Count == 1
            && stepsBack.modifiers[0].steps.Count == 2,
            stepsBack != null ? stepsBack.modifiers[0].steps.Count.ToString() : "（没找到）");
        if (stepsBack != null && stepsBack.modifiers[0].steps.Count == 2)
        {
            var s0 = stepsBack.modifiers[0].steps[0];
            Check("第一档数值对", Near(s0.trigger, 0.3f) && Near(s0.target, 0.4f) && Near(s0.hold, 0.15f) && Near(s0.threshold, 0.1f));
        }

        var orderedBack = Find(parsed.outputs, "U", null);
        Check("修饰符**顺序**被保住", orderedBack != null && orderedBack.modifiers.Count == 3
            && orderedBack.modifiers[0].kind == HoFaceModifierKind.Smooth
            && orderedBack.modifiers[1].kind == HoFaceModifierKind.Steps
            && orderedBack.modifiers[2].kind == HoFaceModifierKind.Delay,
            orderedBack != null ? orderedBack.modifiers.Count.ToString() : "（没找到）");
    }

    /// <summary>
    /// 写→读→写 的稳定性。两段：
    /// ① 夹具（小、覆盖曲线与修饰符）必须**逐字节**稳定；
    /// ② **发货那份真配置**（145 KB / 90 行输出）读进来再写一遍之后必须稳定 ——
    ///    它未必是写出来的形状（生成器产的），所以只断言"归一化一次之后稳定"。
    /// ⚠️ 这段以前拿"内置默认表"当输入，那张表 2026-09-26 已删（它按别的设备的量纲写死了换算）。
    /// </summary>
    private static void WriteReadWriteIsStable()
    {
        var first = HoFaceProfile.Write(Fixture());
        HoFaceMiddleware parsed;
        string error;
        if (!HoFaceProfile.TryParse(first, out parsed, out error)) { Check("往返稳定性用例：第一次解析", false, error); return; }

        var second = HoFaceProfile.Write(parsed);
        Check("夹具：写→读→写 文本完全一致（字节稳定）", first == second,
            first == second ? null : "第一次 " + first.Length + " 字符 / 第二次 " + second.Length + " 字符");

        string root = RepoRoot();
        string shippedPath = root == null ? null
            : Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        if (shippedPath == null || !File.Exists(shippedPath)) { Check("发货配置在包里（往返稳定性）", false); return; }

        HoFaceMiddleware shipped;
        if (!Check("发货配置能读进来", HoFaceProfile.TryParse(File.ReadAllText(shippedPath), out shipped, out error), error)) return;
        string normalizedOnce = HoFaceProfile.Write(shipped);
        HoFaceMiddleware again;
        if (!Check("归一化之后还能读回来", HoFaceProfile.TryParse(normalizedOnce, out again, out error), error)) return;
        string normalizedTwice = HoFaceProfile.Write(again);
        Check("发货配置：归一化一次之后字节稳定（" + normalizedOnce.Length + " 字符）", normalizedOnce == normalizedTwice,
            normalizedOnce == normalizedTwice ? null : "第一次 " + normalizedOnce.Length + " / 第二次 " + normalizedTwice.Length);
    }

    private static void UnknownFieldIsSkipped()
    {
        var text = HoFaceProfile.Write(Fixture());
        // 顶层塞一个未来字段，行里也塞一个 —— 都不该影响解析。
        int at = text.IndexOf("\"inputs\"", StringComparison.Ordinal);
        text = text.Insert(at, "\"futureSection\": { \"a\": [1, 2, {\"b\": \"c\"}], \"d\": null },\n  ");
        text = text.Replace("\"parameter\": \"jawOpen\"", "\"futureRowField\": [true, false], \"parameter\": \"jawOpen\"");

        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse(text, out parsed, out error);
        Check("未知字段被跳过（顶层 + 行内）", ok, error);
        if (ok) Check("跳过后输出行仍然是 3", parsed.outputs.Count == 3, parsed.outputs.Count.ToString());
    }

    private static void UnknownModifierKindIsDroppedAndReported()
    {
        var text = "{\"format\":\"ho-face-middleware\",\"outputs\":[{\"parameter\":\"A\",\"expression\":\"1\"," +
                   "\"modifiers\":[{\"kind\":\"smooth\",\"seconds\":0.01},{\"kind\":\"teleport\",\"seconds\":9}]}," +
                   "{\"parameter\":\"B\",\"expression\":\"1\"}]}";

        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse(text, out parsed, out error);
        Check("认不出的 kind 不致命", ok, error);
        Check("认不出的 kind 被点名", error != null && error.Contains("teleport"), error ?? "（没有报错）");
        if (ok)
        {
            var row = Find(parsed.outputs, "A", null);
            Check("那条修饰符被丢掉、剩下的一条还在", row != null && row.modifiers.Count == 1
                && row.modifiers[0].kind == HoFaceModifierKind.Smooth,
                row != null ? row.modifiers.Count.ToString() : "（没找到）");
        }
    }

    private static void MissingOutputsIsFatal()
    {
        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse("{\"format\":\"ho-face-middleware\",\"inputs\":[]}", out parsed, out error);
        Check("没有输出行 -> 解析失败", !ok);
        Check("并且报的是那句话", error == "配置文件里一行输出都没有。", error);
    }

    private static void WrongFormatIsFatal()
    {
        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse("{\"format\":\"something-else\",\"outputs\":[{\"parameter\":\"A\"}]}", out parsed, out error);
        Check("format 不对 -> 解析失败", !ok);
        Check("报错里点名了 format", error != null && error.Contains("something-else"), error);
    }

    private static void EscapesAndUnicodeRoundTrip()
    {
        var middleware = new HoFaceMiddleware { displayName = "转义\"测试\"", notes = "换行\n制表\t反斜杠\\ 引号\" 结束" };
        var row = new HoFaceOutput { parameter = "引号\"与\\反斜杠", expression = "1", notes = "中文备注" };
        middleware.outputs.Add(row);

        HoFaceMiddleware parsed;
        string error;
        if (!Check("转义用例能解析回来", HoFaceProfile.TryParse(HoFaceProfile.Write(middleware), out parsed, out error), error)) return;

        Check("displayName 原样", parsed.displayName == middleware.displayName, parsed.displayName);
        Check("notes 原样（含换行/制表/反斜杠/引号）", parsed.notes == middleware.notes,
            parsed.notes == null ? "null" : parsed.notes.Replace("\n", "\\n").Replace("\t", "\\t"));
        var back = Find(parsed.outputs, row.parameter, null);
        Check("参数名里的引号与反斜杠原样", back != null, "（没找到）");
    }

    private static void BracketInStringDoesNotConfuseTheParser()
    {
        // 参数名/表达式里带 { } [ ] , : 不该影响解析（朴素实现最容易在这里翻车）。
        var tricky = "a{b}c[d],e:f\"g";
        var text = "{\"format\":\"ho-face-middleware\",\"outputs\":[{\"parameter\":\"" + tricky.Replace("\\", "\\\\").Replace("\"", "\\\"")
            + "\",\"expression\":\"min(1, 2)\",\"notes\":\"[0,1]\"}]}";

        HoFaceMiddleware parsed;
        string error;
        if (!Check("带括号/逗号/冒号的字符串能解析", HoFaceProfile.TryParse(text, out parsed, out error), error)) return;
        Check("参数名一字不差", parsed.outputs[0].parameter == tricky, parsed.outputs[0].parameter);
        Check("表达式一字不差", parsed.outputs[0].expression == "min(1, 2)", parsed.outputs[0].expression);
    }

    private static void BrokenJsonReportsPosition()
    {
        HoFaceMiddleware parsed;
        string error;
        bool ok = HoFaceProfile.TryParse("{\"format\":\"ho-face-middleware\",\"outputs\":[{\"parameter\":}]}", out parsed, out error);
        Check("坏 JSON -> 解析失败", !ok);
        Check("报错带字符位置", error != null && error.Contains("个字符处"), error);
    }

    // ── VTS 收包 ────────────────────────────────────────────────────────────────

    /// <summary>造一包像手机真发出来的载荷（字段顺序/嵌套照 Unity 的 JsonUtility 写法）。</summary>
    private static string BuildVtsPayload(bool withShapes, string extraTopLevel = null, bool swapKv = false)
    {
        var text = new StringBuilder();
        text.Append("{\"Timestamp\":1790237610000,\"Hotkey\":-1,\"FaceFound\":true,");
        text.Append("\"Rotation\":{\"x\":4.839396,\"y\":-24.9542236,\"z\":-3.69211578},");
        text.Append("\"Position\":{\"x\":5.41047,\"y\":-6.31096745,\"z\":-5.556528},");
        if (extraTopLevel != null) text.Append(extraTopLevel).Append(',');
        text.Append("\"BlendShapes\":[");
        if (withShapes)
        {
            var names = HoFaceTrackingChannels.Names;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) text.Append(',');
                // 名字用 iFacialMocap 那种线名写法（`EyeBlinkLeft` → `EyeBlink_L`），
                // 因为要验的是"线名原样保留、不规范化"。
                string wire = names[i];
                text.Append(swapKv
                    ? "{\"v\":" + (i / 52f).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"k\":\"" + wire + "\"}"
                    : "{\"k\":\"" + wire + "\",\"v\":" + (i / 52f).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
        }
        text.Append("],");
        text.Append("\"EyeLeft\":{\"x\":-3.63850021,\"y\":1.22631359,\"z\":0.0},");
        text.Append("\"EyeRight\":{\"x\":-3.63202572,\"y\":0.7962266,\"z\":0.0}}");
        return text.ToString();
    }

    private static void VtsRequestTextIsExact()
    {
        string text = HoVtsPacket.BuildRequest(49985);
        Check("请求包原文正确",
            text == "{\"messageType\":\"iOSTrackingDataRequest\",\"time\":5,\"sentBy\":\"HoFaceTracking\",\"ports\":[49985]}",
            text);
        Check("sentBy 长度在官方要求的 1–64 内",
            HoVtsPacket.SenderName.Length >= 1 && HoVtsPacket.SenderName.Length <= 64,
            HoVtsPacket.SenderName.Length.ToString());
        Check("time 在官方允许的 0.5–10 内",
            HoVtsPacket.RequestSeconds >= 0.5f && HoVtsPacket.RequestSeconds <= 10f,
            HoVtsPacket.RequestSeconds.ToString());
    }

    private static void VtsPayloadKeepsAllFiftyTwoShapes()
    {
        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(BuildVtsPayload(true), out values, out faceFound, out error);

        Check("VTS 包能解析", ok, error);
        if (!ok) return;

        Check("FaceFound 为真", faceFound);
        Check("键数 = 15 + 52 = 67", values.Count == 67, values.Count.ToString());
        Check("Rotation_x = 4.839396", Near(values["Rotation_x"], 4.839396f), values["Rotation_x"].ToString());
        Check("Position_z = -5.556528", Near(values["Position_z"], -5.556528f), values["Position_z"].ToString());
        Check("EyeRight_y = 0.7962266", Near(values["EyeRight_y"], 0.7962266f), values["EyeRight_y"].ToString());
        Check("FaceFound 键 = 1", Near(values["FaceFound"], 1f));
        Check("Hotkey 键 = -1", Near(values["Hotkey"], -1f));

        int present = 0;
        foreach (string name in HoFaceTrackingChannels.Names)
            if (values.ContainsKey(name)) present++;
        Check("52 个规范名一个不少", present == 52, present.ToString());
        Check("第 1 个形态键的值对", Near(values[HoFaceTrackingChannels.Names[1]], 1f / 52f),
            values[HoFaceTrackingChannels.Names[1]].ToString());
    }

    private static void VtsPayloadKeepsWireSpelling()
    {
        // 接收端**不做任何规范化**：手机发什么线名就存什么线名。
        // 改名与量纲是中间层配置里"输入行"的事 —— 这条规矩坏了，整套设计的边界就没了。
        var text = "{\"FaceFound\":true,\"BlendShapes\":[" +
                   "{\"k\":\"EyeBlink_L\",\"v\":0.25},{\"k\":\"JawOpen\",\"v\":0.5},{\"k\":\"其它写法\",\"v\":0.75}]}";

        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(text, out values, out faceFound, out error);

        Check("线名原样保留的用例能解析", ok, error);
        if (!ok) return;

        Check("线名原样保留（EyeBlink_L）", values.ContainsKey("EyeBlink_L"));
        Check("线名原样保留（JawOpen）", values.ContainsKey("JawOpen"));
        Check("非 ASCII 键名原样保留", values.ContainsKey("其它写法"));
        Check("没有自作主张加规范名别名（eyeBlinkLeft）", !values.ContainsKey("eyeBlinkLeft"));
        Check("值也对", Near(values["JawOpen"], 0.5f), values["JawOpen"].ToString());
    }

    /// <summary>
    /// 把默认配置里"VTS 输入行"的线名与**官方枚举**对起来。
    ///
    /// 官方 `VTSARKitBlendshape.cs`（DenchiSoft 那份示例）里 52 个名字是 PascalCase：
    /// `EyeBlinkLeft` / `JawOpen` / `MouthSmileLeft` / `TongueOut`…
    ///
    /// 这条断言核的是**发货配置里那 52 行输入行的 `expression` 与 catalog 的 `vts_wire` 是否逐字相同**
    /// （catalog 是独立记录，不是拿代码算出来的）—— 差一个字母，那一行就静默永不触发
    /// （缺键 → 保持上一帧 → 永远是 0），而且**不报错**。
    ///
    /// ⚠️ 它以前核的是"内置默认表里的 VTS 输入行"，用的期望值来自 `HoFaceMiddlewareDefaults.VtsWire`
    /// （规范名首字母大写）—— 那个类 2026-09-26 已删，理由见 `docs/FACE_TRACKING_MIDDLE_LAYER.md` §6.1。
    /// 换到发货配置 + catalog 之后，这条断言**反而更硬**：期望值不再来自我们自己的规则。
    /// </summary>
    private static void VtsWireNamesMatchTheOfficialEnum()
    {
        string root = RepoRoot();
        if (root == null) { Check("找得到包根（VTS 线名）", false); return; }

        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        string catalogPath = Path.Combine(root, "docs", "VTS_HIGH_QUALITY_FACE_CATALOG.json");
        if (!File.Exists(profilePath) || !File.Exists(catalogPath)) { Check("发货配置与 catalog 都在", false); return; }

        HoFaceMiddleware parsed;
        string error;
        if (!HoFaceProfile.TryParse(File.ReadAllText(profilePath), out parsed, out error)) return;

        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(catalogPath));
        var raw = catalog.RootElement.GetProperty("raw_arkit");

        int matched = 0;
        var missing = new List<string>();
        var expected = new List<string>();      // 顺带抽查几个写死的官方拼写
        foreach (var entry in raw.EnumerateArray())
        {
            string canonical = entry.GetProperty("name").GetString();
            string wire = entry.GetProperty("vts_wire").GetString();
            expected.Add(canonical + "=" + wire);

            var row = Find(parsed.inputs, canonical, null);
            if (row != null && row.expression == wire) matched++;
            else missing.Add(canonical + "→" + (row == null ? "（没有这一行）" : row.expression) + "（期望 " + wire + "）");
        }

        Check("catalog 的 raw_arkit 是 52 条", raw.GetArrayLength() == 52, raw.GetArrayLength().ToString());
        Check("发货配置的 52 个 VTS 输入行与 catalog 的 vts_wire 逐字一致", matched == 52,
            matched == 52 ? null : "对不上 " + (52 - matched) + " 个：" + string.Join("、", missing.ToArray()));
        Check("抽查：eyeBlinkLeft 的线名是 EyeBlinkLeft", expected.Contains("eyeBlinkLeft=EyeBlinkLeft"));
        Check("抽查：jawOpen 的线名是 JawOpen", expected.Contains("jawOpen=JawOpen"));
        Check("抽查：tongueOut 的线名是 TongueOut", expected.Contains("tongueOut=TongueOut"));
    }

    private static void VtsPayloadToleratesUnknownFields()
    {
        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(
            BuildVtsPayload(true, "\"FutureScalar\":42,\"FutureObject\":{\"a\":[1,2,{\"b\":null}]}"),
            out values, out faceFound, out error);

        Check("官方说以后会加字段 —— 多了字段照样能解析", ok, error);
        if (ok) Check("未知字段不影响键数", values.Count == 67, values.Count.ToString());
    }

    private static void VtsPayloadFieldOrderDoesNotMatter()
    {
        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(BuildVtsPayload(true, null, true), out values, out faceFound, out error);
        Check("`v` 写在 `k` 前面也能解析", ok, error);
        if (ok) Check("交换顺序后形态键仍然齐", values.Count == 67, values.Count.ToString());
    }

    private static void VtsPayloadWithoutBlendShapes()
    {
        // 这一条正是修复前的症状：12 个头眼分量全在、形态键全丢（本帧键数=15）。
        // 现在它应该被如实报成"只有 15 个键"，而不是假装成功。
        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(BuildVtsPayload(false), out values, out faceFound, out error);

        Check("没有 BlendShapes 的包仍然解析", ok, error);
        if (ok) Check("这时就只有 15 个键（不含形态键）", values.Count == 15, values.Count.ToString());
    }

    private static void VtsPayloadExponentNumbers()
    {
        var text = "{\"FaceFound\":false,\"Hotkey\":-1,\"Timestamp\":1.79023761E+12," +
                   "\"Rotation\":{\"x\":0,\"y\":0,\"z\":0},\"Position\":{\"x\":0,\"y\":0,\"z\":0}," +
                   "\"EyeLeft\":{\"x\":0,\"y\":0,\"z\":0},\"EyeRight\":{\"x\":0,\"y\":0,\"z\":0}," +
                   "\"BlendShapes\":[{\"k\":\"A\",\"v\":1.25e-2}]}";

        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse(text, out values, out faceFound, out error);

        Check("科学计数法能解析", ok, error);
        if (ok)
        {
            Check("FaceFound 为 false", !faceFound);
            Check("1.25e-2 = 0.0125", Near(values["A"], 0.0125f), values["A"].ToString());
            Check("Timestamp 这么大的数不炸", values.ContainsKey("Timestamp"));
        }
    }

    private static void VtsBrokenPayloadReportsPosition()
    {
        Dictionary<string, float> values;
        bool faceFound;
        string error;
        bool ok = HoVtsPacket.TryParse("{\"FaceFound\":true,\"BlendShapes\":[{\"k\":}]}", out values, out faceFound, out error);
        Check("坏包 -> 解析失败", !ok);
        Check("坏包报错带字符位置", error != null && error.Contains("个字符处"), error);
    }

    // ── 工具 ────────────────────────────────────────────────────────────────────

    private static HoFaceOutput Find(List<HoFaceOutput> rows, string parameter, string notes)
    {
        foreach (var row in rows)
        {
            if (row == null || row.parameter != parameter) continue;
            if (notes == null || row.notes == notes) return row;
        }
        return null;
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }

    /// <summary>
    /// **`out("输出行的参数名")`：引用上面已算完的输出值**（2026-09-27 用户定的机制）。
    ///
    /// 为什么要这个机制：风格化特殊形态（鼓嘴 / 倒V / 苦嘴）要**关掉整块"张嘴 × 笑"的混合树**，
    /// 于是中间层需要"由别的输出行算出来的门"。规则刻意做得简单：
    /// 按行序求值 + `out("…")` 读上面那一行**过完曲线与修饰符**的值 +
    /// 引用下面的行（或引用不存在的行）⇒ 那一行无效、**始终输出 defaultValue**、面板爆红。
    /// 只在"上面"找 ⇒ 依赖图按构造是 DAG，不可能有环。
    /// </summary>
    private static void ExpressionOutputReferences()
    {
        HoFaceExpression expression;
        string why;

        // ① 解析 + 语义：取的是**输出**那一份值，而且不算变量
        Check("out(...)：能解析（out(\"a\") * 10）",
            HoFaceExpression.TryParse("out(\"a\") * 10", out expression, out why), why);
        if (expression == null) return;

        var refs = new List<string>();
        expression.CollectOutputRefs(refs);
        Check("out(...)：CollectOutputRefs 收到那个名字", refs.Count == 1 && refs[0] == "a", string.Join(",", refs.ToArray()));
        var vars = new List<string>();
        expression.CollectVariables(vars);
        Check("out(...)：名字**不算变量**（否则会被当成输入线名去查）", vars.Count == 0, string.Join(",", vars.ToArray()));
        Check("out(...)：用的是**输出**回调（a=2 ⇒ 2×10=20）",
            Near(expression.Evaluate(n => 0f, n => n == "a" ? 2f : 0f), 20f));
        Check("out(...)：没有输出回调时得 0（旧的两参数调用不受影响）",
            Near(expression.Evaluate(n => 0f), 0f));
        Check("out(...)：输出回调抛异常也不许炸（折 0）",
            Near(expression.Evaluate(n => 0f, n => throw new Exception("boom")), 0f));

        // ② 语法错误（都要在**解析期**报出来，不是逐帧悄悄变 0）
        Check("out(...)：字符串写在别处要报错（多半想写单引号）",
            !HoFaceExpression.TryParse("1 + \"a\"", out _, out why), why);
        Check("out(...)：out(0.5) 要报错（参数必须是双引号字符串）",
            !HoFaceExpression.TryParse("out(0.5)", out _, out why), why);
        Check("out(...)：out('a') 要报错（单引号是延迟求值，不是名字）",
            !HoFaceExpression.TryParse("out('a')", out _, out why), why);
        Check("out(...)：out(\"a\",1) 参数个数不对要报错",
            !HoFaceExpression.TryParse("out(\"a\",1)", out _, out why), why);
        Check("out(...)：双引号没闭合要报错",
            !HoFaceExpression.TryParse("out(\"a)", out _, out why), why);
        Check("out(...)：out(\"\") 空名字要报错",
            !HoFaceExpression.TryParse("out(\"\")", out _, out why), why);
        Check("out(...)：少括号（当变量用）要报错",
            !HoFaceExpression.TryParse("out + 1", out _, out why), why);
        Check("out(...)：单引号里也能用（if('out(\"a\") > 0.5', 1, 0) 能解析）",
            HoFaceExpression.TryParse("if('out(\"a\") > 0.5', 1, 0)", out var inQuotes, out why), why);
        if (inQuotes != null)
        {
            var nested = new List<string>();
            inQuotes.CollectOutputRefs(nested);
            Check("out(...)：单引号里的引用也收得到（漏了它 = 顺序校验漏一行）",
                nested.Count == 1 && nested[0] == "a", string.Join(",", nested.ToArray()));
        }

        // ③ 顺序校验（只能引用上面的行）
        Func<string, string, HoFaceOutput> row = (parameter, text) => new HoFaceOutput { parameter = parameter, expression = text };

        var ok = new List<HoFaceOutput> { row("A", "1"), row("B", "out(\"A\") * 2") };
        Check("顺序：引用**上面**的行 = 通过", HoFaceOutputOrder.Validate(ok)[1] == null, HoFaceOutputOrder.Validate(ok)[1]);

        var forward = new List<HoFaceOutput> { row("A", "out(\"B\")"), row("B", "1") };
        string forwardWhy = HoFaceOutputOrder.Validate(forward)[0];
        Check("顺序：引用**下面**的行 = 无效，并且报出第几行",
            forwardWhy != null && forwardWhy.Contains("第 2 行"), forwardWhy);
        Check("顺序：被引用的那一行本身没问题", HoFaceOutputOrder.Validate(forward)[1] == null);

        var self = new List<HoFaceOutput> { row("A", "out(\"A\")") };
        Check("顺序：自引用 = 无效（自己不在「上面」）", HoFaceOutputOrder.Validate(self)[0] != null);

        var missing = new List<HoFaceOutput> { row("A", "out(\"Nope\")") };
        string missingWhy = HoFaceOutputOrder.Validate(missing)[0];
        Check("顺序：引用不存在的行 = 无效", missingWhy != null && missingWhy.Contains("不存在"), missingWhy);

        var duplicate = new List<HoFaceOutput> { row("A", "1"), row("A", "1"), row("B", "out(\"A\")") };
        Check("顺序：同名多行时 `out` 读**上面最近**的那一条（第 3 行引用 A 通过 —— 它读到第 2 行）",
            HoFaceOutputOrder.Validate(duplicate)[2] == null,
            HoFaceOutputOrder.Validate(duplicate)[2]);

        var withConstant = new List<HoFaceOutput> { row("A", ""), row("B", "out(\"A\")") };
        Check("顺序：常量行不引用任何人（不报错）", HoFaceOutputOrder.Validate(withConstant)[0] == null);

        var noExpression = new List<HoFaceOutput> { row("A", "jawOpen"), row("B", "out(\"A\")") };
        Check("顺序：普通表达式行的引用通过", HoFaceOutputOrder.Validate(noExpression)[1] == null);

        // ④ 输入行不许用（那边求值在输出行之前，没有"上面"可言）
        Check("顺序：**输入行**用 out(...) 要报错",
            HoFaceOutputOrder.InputRowError(row("jawOpen", "out(\"A\")")) != null);
        Check("顺序：普通输入行不报错", HoFaceOutputOrder.InputRowError(row("jawOpen", "JawOpen")) == null);
        Check("顺序：空表达式的输入行不报错", HoFaceOutputOrder.InputRowError(row("jawOpen", "")) == null);
    }

    /// <summary>
    /// **倒V 的风格化链**（2026-09-27，用户当天改了三回口径 ⇒ 这一段钉的是「最终形状」）：
    /// 用户定的**两条规则** + **双重形态**：
    /// ① **写自己**：连续读数 × 自动开关 + 外部开关 → `维持` → `平滑`（一条行管完一个形态）；
    /// ② **关其他**：一条**基准行**（常量 1，只写一次）+ **每个形态再压一条同名行**
    ///    `out("同名") * (1 - clamp(out("本形态"), 0, 1))` —— 读自己、写自己。
    /// 靠的是**输出表缓存**：行按顺序读写同一张 `名字 → 值` 的表，后写覆盖先写 ⇒
    /// **加形态不用动任何已有行**（"无限拓展"），发布出去的是最后写的那一份。
    /// 两个开关都是**外部开关**（Warudo 在 VTS 接收器之后 append 的线，profile 里各一行输入行声明）。
    ///
    /// ⚠️ 台架**跑不了会话的修饰符状态**（逐帧状态在会话里）⇒ 分两步验：
    /// ① **配置**：三行都在、行序（基准 → 写自己 → 关其他）、「维持」的参数与它在「平滑」之前；
    /// ② **语义**：用真的 <see cref="HoFaceOutputTable"/> + 真的表达式求值手推一遍链
    ///    （`out` 回调读表）—— 与运行时是同一份代码路径，只差会话的修饰符状态。
    /// </summary>
    private static void StyleRecognitionChain()
    {
        string root = RepoRoot();
        if (root == null) return;
        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        if (!File.Exists(profilePath)) { Check("风格化链：发货配置在包里", false, profilePath); return; }

        HoFaceMiddleware profile;
        string error;
        if (!Check("风格化链：发货配置能读进来", HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error), error)) return;

        var rows = new List<HoFaceOutput>(profile.outputs);
        Func<string, HoFaceOutput> find = name =>
        {
            foreach (var row in rows) if (row != null && row.parameter == name) return row;
            return null;
        };

        // ⓪ **双重形态**：每个形态两条行 —— 一条「写自己」+ 一条「关其他」（同名链）。
        //    现在只有一个形态（倒V）⇒ `Ho/Style/*` 三行：关其他的**基准行** + 形态行 + 这条链的行。
        var styleRows = new List<string>();
        var styleIndices = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || row.parameter == null) continue;
            if (!HoFaceNaming.IsStyleRow(row.parameter)) continue;
            styleRows.Add(row.parameter);
            styleIndices.Add(i);
        }
        // ⭐ 2026-09-28 加**猫嘴**：它是**写自己**那一类，但**没有「关其他」**（它不关整块"张嘴 × 笑"，
        //    它是靠变体开关在张嘴笑那块**内部**换整张嘴）⇒ 六行 = 基准行 + 倒V(写自己/关其他) + 鼓嘴(写自己/关其他) + 猫嘴(写自己)。
        Check("风格化链：`Ho/Style/*` 正好六行 = 基准行 + 倒V(写自己/关其他) + 鼓嘴(写自己/关其他) + 猫嘴(写自己，**不关其他**)"
            + "（没有固定槽/中控行/占位行）：" + string.Join(",", styleRows.ToArray()), styleRows.Count == 6);

        var gateIndices = new List<int>();
        foreach (int i in styleIndices)
            if (rows[i].parameter == "Ho/Style/MouthGate") gateIndices.Add(i);
        Check("风格化链：`Ho/Style/MouthGate` 正好三条同名行（= 双重形态；基准 + 倒V 链行 + 鼓嘴链行）—— 实际 "
            + gateIndices.Count + " 条", gateIndices.Count == 3);
        if (styleRows.Count != 6 || gateIndices.Count != 3) return;
        // ⭐ **2026-09-28 深夜改口径：风格优先级链 = 鼓嘴 > 猫嘴 > 倒V**（用户：「三个风格键，鼓嘴压掉猫嘴压掉V嘴」）。
        //    ⇒ 门链里**确实**会出现 `Ho/Style/CatMouth`（有一条门行乘 `(1 − 鼓嘴) × (1 − 猫嘴)`），
        //      但那是**优先级抑制**，不是"猫嘴关整块张嘴笑"（猫嘴本体仍是块内变体、靠开关换整嘴）。
        int catGateRefs = 0;
        foreach (int i in gateIndices)
            if (rows[i].expression != null && rows[i].expression.Contains("Ho/Style/CatMouth")) catGateRefs++;
        Check("风格化链：门链里恰好有 1 条行引用猫嘴（= 优先级抑制 (1−鼓嘴)×(1−猫嘴)）—— 实际 " + catGateRefs,
            catGateRefs == 1, "实际 " + catGateRefs);

        HoFaceOutput gateBase = rows[gateIndices[0]];
        HoFaceOutput gateChain = rows[gateIndices[1]];
        HoFaceOutput gateChainInv = rows[gateIndices[2]];
        HoFaceOutput form = find("Ho/Style/InvertedV");
        // ⭐ 新款行序：三条形态行（鼓嘴 → 猫嘴 → 倒V）都在门链**之前** —— 门与契约行必须用**同一个已仲裁的值**
        //    （否则会出现"倒V 子树灭了但嘴形仍被让位"这种洞）；`out()` 只能读上面的行，所以顺序是硬要求。
        Check("风格化链：行序 = 鼓嘴 → 猫嘴 → 倒V → 门链（门/契约行读的都是已仲裁的值）",
            rows.IndexOf(find("Ho/Style/Cheek")) < rows.IndexOf(find("Ho/Style/CatMouth"))
            && rows.IndexOf(find("Ho/Style/CatMouth")) < rows.IndexOf(form)
            && rows.IndexOf(form) < gateIndices[0]);
        Check("风格化链：基准行是**常量 1**（表达式留空 + defaultValue 1 = 门开着）",
            string.IsNullOrWhiteSpace(gateBase.expression) && Near(gateBase.defaultValue, 1f),
            "expr=" + gateBase.expression + " def=" + gateBase.defaultValue);

        // ① 行序：`out` 只能引上面的行 —— 链上每一行都得合法（**同名多行不再被判死**）
        var orderErrors = HoFaceOutputOrder.Validate(rows);
        int orderBad = 0;
        var orderText = new List<string>();
        foreach (int i in styleIndices)
        {
            if (orderErrors[i] == null) continue;
            orderBad++;
            if (orderText.Count < 3) orderText.Add(rows[i].parameter + ":" + orderErrors[i]);
        }
        Check("风格化链：三行的引用顺序都对（不对 " + orderBad + "）"
            + (orderText.Count > 0 ? "：" + string.Join(" | ", orderText.ToArray()) : ""), orderBad == 0);

        // ② 写自己那行：读数 = mouthPucker × 死区 × 鼻门，再 × 自动开关 + 外部开关
        // ⭐ **2026-09-28 晚：funnel 门试过又撤了**。加它是想"只认真撅"（把「收嘴不撅」留给嘴宽 1D 表的窄端），
        //    但**用户日常做的那一下噘嘴就是不撅的那种**（`mouthFunnel` 0.081~0.096 ⇒ 门 = 0）⇒ 倒V 直接死了
        //    （实机报「倒V 现在永远不触发了」）。判据回到 `pucker × 死区 × 鼻门`（+ 两个开关）。
        Check("风格化链：写自己 = mouthPucker × 死区(0.41/0.02) × 鼻门(0.54/0.05) × (1−鼓嘴) × (1−猫嘴) × HoAutoInvertedV + HoExternalInvertedV",
            form.expression.Trim() == "(mouthPucker * clamp((mouthPucker - 0.41) / 0.02, 0, 1)"
                + " * (1 - clamp((max(noseSneerLeft, noseSneerRight) - 0.54) / 0.05, 0, 1))"
                + " * (1 - clamp(out(\"Ho/Style/Cheek\"), 0, 1))"
                + " * (1 - clamp(out(\"Ho/Style/CatMouth\"), 0, 1))"
                + " * HoAutoInvertedV) + HoExternalInvertedV", form.expression);

        // ③ 修饰符：一个「维持」（迟滞）在前、一个「平滑」在后 —— 反了过渡会变形
        int stepsCount = 0, stepsIndex = -1, smoothIndex = -1;
        var stepValues = new List<HoFaceStep>();
        if (form.modifiers != null)
            for (int i = 0; i < form.modifiers.Count; i++)
            {
                var modifier = form.modifiers[i];
                if (modifier == null) continue;
                if (modifier.kind == HoFaceModifierKind.Steps)
                {
                    stepsIndex = i;
                    if (modifier.steps != null) { stepsCount += modifier.steps.Count; stepValues.AddRange(modifier.steps); }
                }
                if (modifier.kind == HoFaceModifierKind.Smooth && modifier.Active) smoothIndex = i;
            }
        Check("风格化链：写自己那行挂了一个「维持」（迟滞）—— 实际 " + stepsCount + " 级", stepsCount == 1);
        if (stepsCount == 1)
        {
            Check("风格化链：维持参数 = trigger 0.8 / threshold 0.05 / target 1 / hold 0.1（实际 "
                + stepValues[0].trigger.ToString("F2") + " / " + stepValues[0].threshold.ToString("F2") + " / "
                + stepValues[0].target.ToString("F1") + " / " + stepValues[0].hold.ToString("F2") + "）",
                Near(stepValues[0].trigger, 0.8f) && Near(stepValues[0].threshold, 0.05f)
                && Near(stepValues[0].target, 1f) && Near(stepValues[0].hold, 0.1f));
        }
        else
        {
            Check("风格化链：维持参数", false);
        }
        Check("风格化链：写自己那行是「先维持、后平滑」（维持 #" + stepsIndex + " / 平滑 #" + smoothIndex + "）",
            stepsIndex >= 0 && smoothIndex == stepsIndex + 1);

        // ④b **过渡时长（手感参）**：2026-09-27 实机反馈「太粘滞、不干脆，就是平滑太厉害了」⇒ 0.2 → 0.08。
        //     迟滞那两项（trigger/threshold/hold）是"什么时候开始切"，**没动**；过渡是对称的 ⇒
        //     「平滑」是唯一的过渡时长旋钮，所以把它钉住，免得哪次 re-splice 又带回"粘滞"那一档。
        float formSmoothSeconds = -1f;
        if (form.modifiers != null)
            foreach (var modifier in form.modifiers)
                if (modifier != null && modifier.kind == HoFaceModifierKind.Smooth) formSmoothSeconds = modifier.seconds;
        Check("风格化链：过渡平滑 = 0.08s（实际 " + formSmoothSeconds.ToString("F3")
            + "；手感参，允许带 0.05..0.30 —— 再粘就降、抖了就升）", Near(formSmoothSeconds, 0.08f));

        // ④ 合成式：写自己 = (读数 × 自动开关) + 外部开关
        HoFaceExpression evaluateForm;
        string why;
        if (!Check("风格化链：写自己那行的表达式能解析",
            HoFaceExpression.TryParse(form.expression, out evaluateForm, out why), why))
        {
            return;
        }

        Func<float, float, float, float, float> evalForm = (pucker, nose, auto, external) =>
            evaluateForm.Evaluate(name => name == "mouthPucker" ? pucker
                : name == "noseSneerLeft" ? nose
                    : name == "noseSneerRight" ? nose
                        // funnel 固定按"真撅"给（实测 0.178~0.210）⇒ funnel 门全开，不影响下面这些老断言
                        : name == "mouthFunnel" ? 0.20f
                            : name == "HoAutoInvertedV" ? auto
                                : name == "HoExternalInvertedV" ? external : 0f);
        // 只变 funnel 的两条（2026-09-28 傍晚加的门的正/负样本）
        // ⚠️ funnel 门撤掉之后，**收嘴不撅也会点亮倒V**（两者 `mouthPucker` 0.97~0.99 同值，设备分不开）——
        //    这是**刻意的**：用户日常那一下噘嘴就是不撅的那种。代价：嘴宽 1D 表的"窄"端会被形态门吃掉。
        Check("风格化链：**收嘴不撅**（pucker 0.976 / funnel 0.088 / 鼻 0.10）⇒ 读数 "
            + evalForm(0.976f, 0.10f, 1f, 0f).ToString("F3") + " ≥ 0.8（会点亮倒V —— funnel 门已撤）",
            evalForm(0.976f, 0.10f, 1f, 0f) >= 0.8f);
        // 2026-09-27 十五段 5 秒实测（用户「记 5 秒」）：静息 max **0.3797** / 挤眼 max **0.7769** /
        // 真噘嘴 min **0.9634** / 笑 max 0.0463；鼻：真噘嘴 ≤**0.2942**、挤眼 ≥**0.7821**。
        Check("风格化链：真噘嘴 0.9634（鼻 0.2942）× 自动开 ⇒ 读数 " + evalForm(0.9634f, 0.2942f, 1f, 0f).ToString("F3")
            + " ≥ 0.8（会点亮）", evalForm(0.9634f, 0.2942f, 1f, 0f) >= 0.8f);
        Check("风格化链：静息 0.3797（鼻 0.2036）⇒ 读数 " + evalForm(0.3797f, 0.2036f, 1f, 0f).ToString("F3")
            + " = 0（死区把静息天花板压平）", Near(evalForm(0.3797f, 0.2036f, 1f, 0f), 0f));
        Check("风格化链：挤眼 0.7769（鼻 0.8639）⇒ 读数 " + evalForm(0.7769f, 0.8639f, 1f, 0f).ToString("F3")
            + " = 0（鼻门关掉 —— 裸读数 0.7769 离旧的 0.8 只剩 0.023）", Near(evalForm(0.7769f, 0.8639f, 1f, 0f), 0f));
        Check("风格化链：笑 0.0463 ⇒ 读数 0（死区）", Near(evalForm(0.0463f, 0.3750f, 1f, 0f), 0f));
        Check("风格化链：**没量到的**不皱鼻挤眼（pucker 0.7769、鼻 0.20）⇒ 读数 "
            + evalForm(0.7769f, 0.20f, 1f, 0f).ToString("F3") + " 仍 < 0.8（由触发线兜住）",
            evalForm(0.7769f, 0.20f, 1f, 0f) < 0.8f);
        Check("风格化链：自动开关写 0 ⇒ 读数 0（关得掉自动触发）", Near(evalForm(0.9634f, 0.2942f, 0f, 0f), 0f));
        Check("风格化链：外部开关写 1 ⇒ 读数 " + evalForm(0.3797f, 0.2036f, 1f, 1f).ToString("F3")
            + " ≥ 0.8（静息也必亮：常开独立于自动）", evalForm(0.3797f, 0.2036f, 1f, 1f) >= 0.8f);
        Check("风格化链：外部开关写 1、自动关着 ⇒ 读数 " + evalForm(0f, 0.2f, 0f, 1f).ToString("F3") + " = 1（照样常开）",
            Near(evalForm(0f, 0.2f, 0f, 1f), 1f));
        // 不用 clamp 的理由：两个开关是**加法**，但「维持」的输出恒等于 target
        // ⇒ 高出来的那一截到不了下游（这就是「两个都开也不翻倍」）
        Check("风格化链：两个都写 1 ⇒ 表达式 " + evalForm(0.9634f, 0.2942f, 1f, 1f).ToString("F3")
            + " 超过 1，由「维持」输出恒等于 target 收掉（所以不用 clamp）", evalForm(0.9634f, 0.2942f, 1f, 1f) > 1f);

        // ⑤ **关其他 = 输出表缓存上的同名链**：读上一条同名行、乘 `1 - 本形态`、写回同一个名字。
        //    这里不跑会话（台架没有修饰符状态），而是**照运行时的规矩手推一遍**：
        //    用真的 HoFaceOutputTable + 真的表达式求值（`out` 回调读表）—— 语义是同一份代码。
        HoFaceExpression evaluateGate;
        if (!Check("风格化链：关其他那行的表达式能解析",
            HoFaceExpression.TryParse(gateChain.expression, out evaluateGate, out why), why))
        {
            return;
        }
        Check("风格化链：门链第一条链行 = out(同名) × (1 − 鼓嘴) × (1 − 猫嘴)（优先级抑制）",
            gateChain.expression.Trim() == "out(\"Ho/Style/MouthGate\") * (1 - clamp(out(\"Ho/Style/Cheek\"), 0, 1))"
                + " * (1 - clamp(out(\"Ho/Style/CatMouth\"), 0, 1))",
            gateChain.expression);
        Check("风格化链：门链第二条链行 = out(同名) × (1 − 倒V)",
            gateChainInv.expression.Trim() == "out(\"Ho/Style/MouthGate\") * (1 - clamp(out(\"Ho/Style/InvertedV\"), 0, 1))",
            gateChainInv.expression);
        HoFaceExpression evaluateGateInv;
        if (!Check("风格化链：门链第二条链行的表达式能解析",
            HoFaceExpression.TryParse(gateChainInv.expression, out evaluateGateInv, out why), why)) return;
        Check("风格化链：关其他那行的 defaultValue 也是 1（万一无效 ⇒ 门开着，不会误关张嘴笑）",
            Near(gateChain.defaultValue, 1f), gateChain.defaultValue.ToString("F3"));

        var table = new HoFaceOutputTable();
        table.Clear();
        table.Write("Ho/Style/MouthGate", gateBase.defaultValue);          // 基准行
        Check("风格化链【表】：基准行之后门是 1（开着）", Near(table.Read("Ho/Style/MouthGate"), 1f));

        table.Write("Ho/Style/InvertedV", 0f);                             // 写自己那行：形态没亮
        table.Write("Ho/Style/MouthGate", evaluateGate.Evaluate(name => 0f, name => table.Read(name)));
        Check("风格化链【表】：倒V 没亮 ⇒ 门还是 1", Near(table.Read("Ho/Style/MouthGate"), 1f));

        table.Write("Ho/Style/InvertedV", 1f);                             // 形态亮
        // ⚠️ 用**第二条链行**（×(1−倒V)）：第一条链行现在管的是"鼓嘴/猫嘴"的优先级抑制。
        table.Write("Ho/Style/MouthGate", evaluateGateInv.Evaluate(name => 0f, name => table.Read(name)));
        Check("风格化链【表】：倒V 亮 ⇒ 门 0（张嘴笑整块被关掉）", Near(table.Read("Ho/Style/MouthGate"), 0f));


        // ⭐ **这次不是模拟了**：profile 里已经真的加了第二个形态（鼓嘴）—— 按行序把这几条行跑一遍，
        //    验证"谁亮都关门、都不亮就开门"（输出表缓存：后写覆盖先写）。
        Func<float, float, float> passGate = (invertedV, cheek) =>
        {
            var run = new HoFaceOutputTable();
            run.Clear();
            int seenGates = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.parameter == null) continue;
                float value;
                if (row.parameter == "Ho/Style/MouthGate")
                {
                    if (seenGates == 0) value = row.defaultValue;               // 基准行是常量
                    else
                    {
                        HoFaceExpression parsed;
                        string why3;
                        if (!HoFaceExpression.TryParse(row.expression, out parsed, out why3)) return float.NaN;
                        value = parsed.Evaluate(name => 0f, name => run.Read(name));
                    }
                    seenGates++;
                }
                else if (row.parameter == "Ho/Style/InvertedV") value = invertedV;
                else if (row.parameter == "Ho/Style/Cheek") value = cheek;
                else continue;
                run.Write(row.parameter, value);
            }
            return run.Read("Ho/Style/MouthGate");
        };
        Check("风格化链【真表】：两个形态都没亮 ⇒ 门 1", Near(passGate(0f, 0f), 1f));
        Check("风格化链【真表】：只倒V 亮 ⇒ 门 0", Near(passGate(1f, 0f), 0f));
        Check("风格化链【真表】：只鼓嘴亮 ⇒ 门 0（**加第二个形态没动上面任何一行**）", Near(passGate(0f, 1f), 0f));
        Check("风格化链【真表】：两个都亮 ⇒ 门 0", Near(passGate(1f, 1f), 0f));

        // 同名链**第一次**出现时不能读自己（上面没人写过 ⇒ 那一行无效、爆红）
        Func<string, string, HoFaceOutput> mk = (parameter, text) =>
            new HoFaceOutput { parameter = parameter, expression = text };
        var chainNoBase = new List<HoFaceOutput>
        {
            mk("Ho/Style/MouthGate", "out(\"Ho/Style/MouthGate\") * 0.5"),
            mk("Other", "jawOpen")
        };
        Check("风格化链：链的**第一条**同名行读自己 ⇒ 判无效（上面还没有人写过这个名字）",
            HoFaceOutputOrder.Validate(chainNoBase)[0] != null);
        // 而"基准 + 链"两行就是合法的
        var chainWithBase = new List<HoFaceOutput>
        {
            mk("Ho/Style/MouthGate", ""),
            mk("Ho/Style/MouthGate", "out(\"Ho/Style/MouthGate\") * 0.5")
        };
        var chainWithBaseErrors = HoFaceOutputOrder.Validate(chainWithBase);
        Check("风格化链：基准行 + 链行 ⇒ 两行都合法",
            chainWithBaseErrors[0] == null && chainWithBaseErrors[1] == null);
        // 引用了**只有下面才有**的名字：无效，且报错措辞指向"上面还没有"
        var forwardOnly = new List<HoFaceOutput> { mk("A", "out(\"B\")"), mk("B", "jawOpen") };
        Check("风格化链：引用只有下面才有的名字 ⇒ 判无效",
            HoFaceOutputOrder.Validate(forwardOnly)[0] != null);

        Check("风格化链：写自己那行的默认值是 0（表达不了时形态是灭的）", Near(form.defaultValue, 0f));

        // ⑥ 平滑只在**写自己**那一行上（关其他的两条同名行都不挂：两处各平滑一次会把过渡拉长）
        int gateSmooth = 0;
        foreach (int i in gateIndices)
        {
            var gate = rows[i];
            if (gate.modifiers == null) continue;
            foreach (var modifier in gate.modifiers)
                if (modifier != null && modifier.kind == HoFaceModifierKind.Smooth && modifier.Active) gateSmooth++;
        }
        Check("风格化链：平滑只在写自己那一行（两条关其他挂了 " + gateSmooth + " 个）", gateSmooth == 0);

        // ⑦ 两个开关都必须是**声明过的输入行**（否则 Warudo append 的那个名字读不到，
        //    表达式会静默取 0 —— 自动那一半就永远点不亮）
        foreach (string switchName in new[] { "HoAutoInvertedV", "HoExternalInvertedV" })
        {
            HoFaceOutput declared = null;
            foreach (var input in profile.inputs)
                if (input != null && input.expression == switchName) declared = input;
            Check("风格化链：" + switchName + " 是声明过的输入行（Warudo 就 append 这个名字）", declared != null);
            if (declared != null)
                Check("风格化链：" + switchName + " 的 parameter 与表达式同名（标识符形状：不能带 /）",
                    declared.parameter == switchName);
        }
        Check("风格化链：自动开关没人 append 时取 1（默认允许自动触发）",
            Near(InjectedWireDefault(profile, "HoAutoInvertedV"), 1f));
        Check("风格化链：外部开关没人 append 时取 0（没按就不常开）",
            Near(InjectedWireDefault(profile, "HoExternalInvertedV"), 0f));
        // ⭐ 猫嘴那一对**同形**（2026-09-28 加）：自动默认 1、外部默认 0 ⇒ 没人 append 时行为不变
        foreach (string switchName in new[] { "HoAutoCatMouth", "HoExternalCatMouth" })
        {
            HoFaceOutput declared = null;
            foreach (var input in profile.inputs)
                if (input != null && input.expression == switchName) declared = input;
            Check("猫嘴链：" + switchName + " 是声明过的输入行（Warudo 就 append 这个名字）", declared != null);
            if (declared != null)
                Check("猫嘴链：" + switchName + " 的 parameter 与表达式同名（标识符形状：不能带 /）",
                    declared.parameter == switchName);
        }
        Check("猫嘴链：自动开关没人 append 时取 1（默认允许自动触发，行为与本轮之前一样）",
            Near(InjectedWireDefault(profile, "HoAutoCatMouth"), 1f));
        Check("猫嘴链：外部开关没人 append 时取 0（没按就不常开）",
            Near(InjectedWireDefault(profile, "HoExternalCatMouth"), 0f));
    }

    /// <summary>
    /// **鼓嘴的识别链**（2026-09-27 第二批 12 段 ＋ 第一批 = **27 段** 5 秒实测选出来的）：
    /// 形态门 = `max(cheekPuff, max(mouthLeft, mouthRight)) × 颊门 × 死区`，`维持 trigger 0.50`；
    /// 两条颊轴 = `max(本侧, cheekPuff × (1 − clamp(另一侧 / 0.15, 0, 1))) × 颊门 × 死区`。
    ///
    /// ⭐ 这批实测**推翻了一条旧结论**：左右这一维**有**信号 —— 单边鼓嘴时嘴唇被推过去，
    /// `mouthLeft` 0.69~0.86（左鼓）/ `mouthRight` 0.67~0.87（右鼓），而**双鼓时它们只有 ≈0.03**
    /// （`cheekPuff` 0.95~0.98）⇒ 左右靠 `mouthLeft/Right` 分，不是靠颊那一路。
    /// ⚠️ 但 `mouthLeft/Right` **也是「撇嘴」的主要信号**（还没量过）⇒ 那道「颊确实在动」的门
    /// （`cheekPuff ≥ 0.10` 才放行）就是为它加的：**纯撇嘴算出来是 0**（下面有一条负例把它钉住）。
    /// </summary>
    private static void CheekRecognitionChain()
    {
        string root = RepoRoot();
        if (root == null) return;
        string profilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
        if (!File.Exists(profilePath)) { Check("鼓嘴链：发货配置在包里", false, profilePath); return; }

        HoFaceMiddleware profile;
        string error;
        if (!Check("鼓嘴链：发货配置能读进来", HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error), error)) return;

        var rows = new List<HoFaceOutput>(profile.outputs);
        Func<string, HoFaceOutput> find = name =>
        {
            foreach (var row in rows) if (row != null && row.parameter == name) return row;
            return null;
        };

        // ① 形态门
        HoFaceOutput form = find("Ho/Style/Cheek");
        Check("鼓嘴链：形态行在（Ho/Style/Cheek）", form != null);
        if (form == null) return;
        Check("鼓嘴链：读数 = max(cheekPuff, max(mouthLeft, mouthRight)) × 颊门 × 死区 × 自动 + 外部",
            form.expression.Trim() == "(max(cheekPuff, max(mouthLeft, mouthRight))"
                + " * clamp((cheekPuff - 0.05) / 0.05, 0, 1)"
                + " * clamp((max(cheekPuff, max(mouthLeft, mouthRight)) - 0.40) / 0.05, 0, 1)"
                + " * HoAutoCheek) + HoExternalCheek", form.expression);

        int stepsCount = 0, stepsIndex = -1, smoothIndex = -1;
        var stepValues = new List<HoFaceStep>();
        if (form.modifiers != null)
            for (int i = 0; i < form.modifiers.Count; i++)
            {
                var modifier = form.modifiers[i];
                if (modifier == null) continue;
                if (modifier.kind == HoFaceModifierKind.Steps)
                {
                    stepsIndex = i;
                    if (modifier.steps != null) { stepsCount += modifier.steps.Count; stepValues.AddRange(modifier.steps); }
                }
                if (modifier.kind == HoFaceModifierKind.Smooth && modifier.Active) smoothIndex = i;
            }
        Check("鼓嘴链：形态行挂了一个「维持」", stepsCount == 1);
        if (stepsCount == 1)
            Check("鼓嘴链：维持参数 = trigger 0.50 / threshold 0.05 / target 1 / hold 0.1（实际 "
                + stepValues[0].trigger.ToString("F2") + " / " + stepValues[0].threshold.ToString("F2") + " / "
                + stepValues[0].target.ToString("F1") + " / " + stepValues[0].hold.ToString("F2") + "）",
                Near(stepValues[0].trigger, 0.5f) && Near(stepValues[0].threshold, 0.05f)
                && Near(stepValues[0].target, 1f) && Near(stepValues[0].hold, 0.1f));
        Check("鼓嘴链：先维持、后平滑（维持 #" + stepsIndex + " / 平滑 #" + smoothIndex + "）",
            stepsIndex >= 0 && smoothIndex == stepsIndex + 1);

        // ④b **过渡时长（手感参）**：与倒V 同一条口径（2026-09-27：0.2 → 0.08，用户指明"就是平滑太厉害了"）
        float cheekSmoothSeconds = -1f;
        if (form.modifiers != null)
            foreach (var modifier in form.modifiers)
                if (modifier != null && modifier.kind == HoFaceModifierKind.Smooth) cheekSmoothSeconds = modifier.seconds;
        Check("鼓嘴链：过渡平滑 = 0.08s（实际 " + cheekSmoothSeconds.ToString("F3")
            + "；与倒V 同口径，允许带 0.05..0.30）", Near(cheekSmoothSeconds, 0.08f));

        HoFaceExpression evaluateForm;
        string why;
        if (!Check("鼓嘴链：形态行的表达式能解析",
            HoFaceExpression.TryParse(form.expression, out evaluateForm, out why), why)) return;
        Func<float, float, float, float, float, float> evalForm = (cheek, left, right, auto, external) =>
            evaluateForm.Evaluate(name => name == "cheekPuff" ? cheek
                : name == "mouthLeft" ? left
                    : name == "mouthRight" ? right
                        : name == "HoAutoCheek" ? auto
                            : name == "HoExternalCheek" ? external : 0f);

        // 实测锚点（稳态/最大）：双边鼓 0.9803 + 侧向 0.03 / 右鼓 cheekPuff 0.2555 + mouthRight 0.8104 /
        // 左鼓 mouthLeft 0.8446 / 静息 0.0181 / 噘嘴 0.1769 / 挤眼 0.0803 / 笑 0.1540
        Check("鼓嘴链：双边鼓（颊 0.98、两侧 0.03）⇒ 读数 " + evalForm(0.9803f, 0.03f, 0.03f, 1f, 0f).ToString("F3") + " ≥ 0.5",
            evalForm(0.9803f, 0.03f, 0.03f, 1f, 0f) >= 0.5f);
        Check("鼓嘴链：右脸颊鼓（颊 0.2555、mouthRight 0.8104）⇒ 读数 " + evalForm(0.2555f, 0.01f, 0.8104f, 1f, 0f).ToString("F3") + " ≥ 0.5",
            evalForm(0.2555f, 0.01f, 0.8104f, 1f, 0f) >= 0.5f);
        Check("鼓嘴链：左脸颊鼓（颊 0.4496、mouthLeft 0.8446）⇒ 读数 " + evalForm(0.4496f, 0.8446f, 0.01f, 1f, 0f).ToString("F3") + " ≥ 0.5",
            evalForm(0.4496f, 0.8446f, 0.01f, 1f, 0f) >= 0.5f);
        Check("鼓嘴链：静息（颊 0.0181）⇒ 读数 0（死区）", Near(evalForm(0.0181f, 0.01f, 0.01f, 1f, 0f), 0f));
        Check("鼓嘴链：噘嘴（颊 0.1769）⇒ 读数 0（死区膝 0.40 —— 噘嘴的颊读数能到 0.177）",
            Near(evalForm(0.1769f, 0.0226f, 0.0191f, 1f, 0f), 0f));
        Check("鼓嘴链：挤眼（颊 0.0803）⇒ 读数 0", Near(evalForm(0.0803f, 0.0201f, 0.0201f, 1f, 0f), 0f));
        Check("鼓嘴链：笑（颊 0.1540）⇒ 读数 0", Near(evalForm(0.1540f, 0.01f, 0.01f, 1f, 0f), 0f));
        Check("鼓嘴链：**纯撇嘴**（mouthLeft 0.80、颊只有 0.02）⇒ 读数 " + evalForm(0.02f, 0.80f, 0.01f, 1f, 0f).ToString("F3")
            + " = 0（「颊确实在动」那道门挡住的 —— 撇嘴还没实测过，这条是它的保险）",
            Near(evalForm(0.02f, 0.80f, 0.01f, 1f, 0f), 0f));
        Check("鼓嘴链：自动开关写 0 ⇒ 读数 0", Near(evalForm(0.9803f, 0.03f, 0.03f, 0f, 0f), 0f));
        Check("鼓嘴链：外部开关写 1 ⇒ 静息也必亮", evalForm(0.0181f, 0.01f, 0.01f, 1f, 1f) >= 0.5f);

        // ② 两条颊轴
        HoFaceOutput left = find("Ho/Drive/Cheek/Left/Puff");
        HoFaceOutput right = find("Ho/Drive/Cheek/Right/Puff");
        if (!Check("鼓嘴链：两条颊轴都在", left != null && right != null)) return;
        HoFaceExpression evalLeft = null, evalRight = null;
        if (!Check("鼓嘴链：颊轴表达式能解析",
            HoFaceExpression.TryParse(left.expression, out evalLeft, out why)
            && HoFaceExpression.TryParse(right.expression, out evalRight, out why) && evalLeft != null && evalRight != null, why)) return;
        Func<HoFaceExpression, float, float, float, float> axis = (parsed, cheek, side, other) =>
            parsed.Evaluate(name => name == "cheekPuff" ? cheek
                : name == "mouthLeft" ? side
                    : name == "mouthRight" ? other : 0f);
        Func<float, float, float, float> evalLeftAxis = (cheek, l, r) => axis(evalLeft, cheek, l, r);
        Func<float, float, float, float> evalRightAxis = (cheek, l, r) => axis(evalRight, cheek, l, r);

        Check("鼓嘴链：左轴在左脸鼓 ⇒ " + evalLeftAxis(0.4496f, 0.8446f, 0.01f).ToString("F3") + " ≥ 0.5",
            evalLeftAxis(0.4496f, 0.8446f, 0.01f) >= 0.5f);
        Check("鼓嘴链：左轴在**右**脸鼓 ⇒ " + evalLeftAxis(0.2555f, 0.01f, 0.8104f).ToString("F3") + " = 0（另一侧把它归零）",
            Near(evalLeftAxis(0.2555f, 0.01f, 0.8104f), 0f));
        Check("鼓嘴链：左轴在双边鼓 ⇒ " + evalLeftAxis(0.9761f, 0.03f, 0.03f).ToString("F3") + " ≥ 0.5（颊那一项把两侧同时点亮）",
            evalLeftAxis(0.9761f, 0.03f, 0.03f) >= 0.5f);
        Check("鼓嘴链：左轴在纯撇嘴 ⇒ 0", Near(evalLeftAxis(0.02f, 0.80f, 0.01f), 0f));
        Check("鼓嘴链：右轴在右脸鼓 ⇒ " + evalRightAxis(0.2555f, 0.01f, 0.8104f).ToString("F3") + " ≥ 0.5",
            evalRightAxis(0.2555f, 0.01f, 0.8104f) >= 0.5f);
        Check("鼓嘴链：右轴在**左**脸鼓 ⇒ 0（另一侧把它归零）", Near(evalRightAxis(0.4496f, 0.8446f, 0.01f), 0f));
        Check("鼓嘴链：右轴在双边鼓 ⇒ " + evalRightAxis(0.9761f, 0.03f, 0.03f).ToString("F3") + " ≥ 0.5",
            evalRightAxis(0.9761f, 0.03f, 0.03f) >= 0.5f);

        // ③ 两个开关都必须是声明过的输入行（defaultValue 1 / 0）
        foreach (string switchName in new[] { "HoAutoCheek", "HoExternalCheek" })
        {
            HoFaceOutput declared = null;
            foreach (var input in profile.inputs)
                if (input != null && input.expression == switchName) declared = input;
            Check("鼓嘴链：" + switchName + " 是声明过的输入行（Warudo 就 append 这个名字）", declared != null);
            if (declared != null)
                Check("鼓嘴链：" + switchName + " 的 parameter 与表达式同名", declared.parameter == switchName);
        }
        Check("鼓嘴链：自动开关没人 append 时取 1", Near(InjectedWireDefault(profile, "HoAutoCheek"), 1f));
        Check("鼓嘴链：外部开关没人 append 时取 0", Near(InjectedWireDefault(profile, "HoExternalCheek"), 0f));

        // ④ 与倒V 互不干扰：鼓嘴那 9 段的 mouthPucker 最大 0.7453 < 倒V 的 0.80
        var invertedV = find("Ho/Style/InvertedV");
        HoFaceExpression evalInvertedV;
        if (invertedV != null && HoFaceExpression.TryParse(invertedV.expression, out evalInvertedV, out why))
        {
            float atDoubleCheek = evalInvertedV.Evaluate(name => name == "mouthPucker" ? 0.7453f
                : name == "noseSneerLeft" ? 0.2297f
                    : name == "noseSneerRight" ? 0.2297f
                        : name == "HoAutoInvertedV" ? 1f : 0f);
            Check("鼓嘴链：双边鼓那一段（pucker 0.7453）不会误点倒V ⇒ 读数 "
                + atDoubleCheek.ToString("F3") + " < 0.8", atDoubleCheek < 0.8f);
        }

        // ⑤ 契约行：控制器**看不见** `Ho/Style/*` ⇒ 每条形态要拿一行把自己的值发出去。
        //    倒V = "一个固定姿势"（控制器那棵 `InvertedV` Simple1D 吃 `Ho/Drive/Style/InvertedV`）；
        //    形态门 = "张嘴 × 笑"整块的权重（`MouthRegion` 的三个孩子吃 `Ho/Drive/Gate/MouthStyle`）。
        HoFaceOutput gateContract = null, styleContract = null, catContract = null;
        foreach (var row in rows)
        {
            if (row == null || row.parameter == null) continue;
            if (row.parameter == "Ho/Drive/Gate/MouthStyle") gateContract = row;
            if (row.parameter == "Ho/Drive/Style/InvertedV") styleContract = row;
            if (row.parameter == "Ho/Drive/Style/CatMouth") catContract = row;      // 2026-09-28 加的第三条
        }
        if (!Check("契约行：三条都在（Ho/Drive/Gate/MouthStyle + Ho/Drive/Style/InvertedV + Ho/Drive/Style/CatMouth）",
            gateContract != null && styleContract != null && catContract != null)) return;
        Check("契约行：MouthStyle 的式子 = out(\"Ho/Style/MouthGate\")（只是转发，不加料）",
            gateContract.expression != null && gateContract.expression.Trim() == "out(\"Ho/Style/MouthGate\")",
            gateContract.expression);
        Check("契约行：MouthStyle 的 defaultValue = 1（万一无效 ⇒ 门开着，不把张嘴笑误关掉）",
            Near(gateContract.defaultValue, 1f));
        Check("契约行：倒V 权重的式子 = out(\"Ho/Style/InvertedV\")",
            styleContract.expression != null && styleContract.expression.Trim() == "out(\"Ho/Style/InvertedV\")",
            styleContract.expression);
        Check("契约行：倒V 权重的 defaultValue = 0（表达不了 ⇒ 中性，不是倒V）", Near(styleContract.defaultValue, 0f));
        Check("契约行：倒V / 猫嘴 的权重式子都是 out(同名)",  // 猫嘴那条与倒V **同形**（2026-09-28）
            catContract.expression != null && catContract.expression.Trim() == "out(\"Ho/Style/CatMouth\")",
            catContract.expression);
        Check("契约行：三条都不挂时域修饰（平滑/维持挂在形态自己那一行上，转发行不该再来一遍）",
            (gateContract.modifiers == null || gateContract.modifiers.Count == 0)
            && (styleContract.modifiers == null || styleContract.modifiers.Count == 0)
            && (catContract.modifiers == null || catContract.modifiers.Count == 0));

        // 整份输出行**按行序**跑一遍：真的 HoFaceOutputTable ⇒ `out("…")` 读到"上面最后写的那一份"。
        // 这也是唯一能验"契约行排在链行**下面**"的办法 —— 万一排到上面，它读到的是基准行的 1：
        // 形态亮起来门却还是 1，下面那几条场景检查立刻红。
        Func<Dictionary<string, float>, Dictionary<string, float>> runProfile = sources =>
        {
            var runTable = new HoFaceOutputTable();
            runTable.Clear();
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in profile.outputs)
            {
                if (row == null || row.parameter == null) continue;
                float value;
                if (string.IsNullOrWhiteSpace(row.expression)) value = row.defaultValue;
                else
                {
                    HoFaceExpression parsed;
                    string why4;
                    if (!HoFaceExpression.TryParse(row.expression, out parsed, out why4)) return null;
                    value = row.Transform(parsed.Evaluate(
                        name => sources.TryGetValue(name, out float source) ? source : 0f,
                        name => runTable.Read(name)));
                }
                runTable.Write(row.parameter, value);
                values[row.parameter] = value;
            }
            return values;
        };

        var allOrderErrors = HoFaceOutputOrder.Validate(profile.outputs);
        int allOrderBad = 0;
        var allOrderText = new List<string>();
        for (int i = 0; i < profile.outputs.Count; i++)
        {
            if (allOrderErrors == null || i >= allOrderErrors.Length || allOrderErrors[i] == null) continue;
            allOrderBad++;
            if (allOrderText.Count < 3) allOrderText.Add(profile.outputs[i].parameter + ":" + allOrderErrors[i]);
        }
        Check("契约行：整份配置的输出行引用顺序全对（`out` 只引上面的行；不对 " + allOrderBad + "）"
            + (allOrderText.Count > 0 ? "：" + string.Join(" | ", allOrderText.ToArray()) : ""), allOrderBad == 0);

        var contractRest = runProfile(new Dictionary<string, float>(StringComparer.Ordinal));
        if (!Check("契约行：整份输出行能按行序跑完（表达式全都能解析）", contractRest != null)) return;
        Check("契约行：静息 ⇒ MouthStyle 门 = 1、倒V 权重 = 0（实际 "
            + contractRest["Ho/Drive/Gate/MouthStyle"].ToString("F3") + " / "
            + contractRest["Ho/Drive/Style/InvertedV"].ToString("F3") + "）",
            Near(contractRest["Ho/Drive/Gate/MouthStyle"], 1f) && Near(contractRest["Ho/Drive/Style/InvertedV"], 0f));

        var contractPucker = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "mouthPucker", 0.9634f }, { "noseSneerLeft", 0.2942f }, { "noseSneerRight", 0.2942f },
            { "mouthFunnel", 0.19f },   // （判据里已经没有 funnel 门了 —— 当晚试过又撤，这一行留着无害）

            { "HoAutoInvertedV", 1f },

        });
        Check("契约行：真噘嘴（0.9634 / 鼻 0.2942）⇒ 倒V 权重 " + contractPucker["Ho/Drive/Style/InvertedV"].ToString("F3")
            + " ≥ 0.9、门 " + contractPucker["Ho/Drive/Gate/MouthStyle"].ToString("F3") + " ≤ 0.1",
            contractPucker["Ho/Drive/Style/InvertedV"] >= 0.9f && contractPucker["Ho/Drive/Gate/MouthStyle"] <= 0.1f);

        // ⭐ **2026-09-28 晚：嘴宽轴被猫嘴掐掉**（用户报「猫嘴现在会永远触发嘴宽最大值」）——
        //    猫嘴 = dimple 0.78~0.84 + pucker 低 ⇒ 这根轴算到 ≈3.1（远超满档 +2.0），
        //    而形态门只挡倒V/鼓嘴、**不挡猫嘴**（猫嘴是块内变体、不关其他）⇒ 在这根轴上自己掐。
        var contractCat = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "mouthRollLower", 0.4595f }, { "mouthRollUpper", 0.0260f },
            { "mouthDimpleLeft", 0.80f }, { "mouthDimpleRight", 0.80f },
            { "mouthFrownLeft", 0.34f }, { "mouthFrownRight", 0.34f },
            { "HoAutoCatMouth", 1f },
        });
        Check("契约行：猫嘴亮（权重 " + contractCat["Ho/Drive/Style/CatMouth"].ToString("F3") + "）⇒ 嘴宽轴 "
            + contractCat["Ho/Drive/Mouth/Pucker"].ToString("F3") + " = 0（不再顶满 +2.0；门膝 0.25/0.15）",
            contractCat["Ho/Drive/Style/CatMouth"] >= 0.30f && Near(contractCat["Ho/Drive/Mouth/Pucker"], 0f));

        // ⭐ **反例：抿嘴嘴宽不能被掐**（用户报「width 到 2 后立刻就归零了」）—— 那根轴的满档 ≈2.0 正是「抿嘴嘴宽」
        //    的读数；它与猫嘴共用信号、只差 `mouthRollLower`（0.14 vs 0.43~0.46）⇒ 门膝必须抬到 0.25 才留出余量。
        var contractWide = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "mouthRollLower", 0.145f }, { "mouthRollUpper", 0.010f },
            { "mouthDimpleLeft", 0.525f }, { "mouthDimpleRight", 0.525f },
            { "mouthSmileLeft", 0.51f }, { "mouthSmileRight", 0.51f },
            { "mouthPucker", 0.104f },
            { "HoAutoCatMouth", 1f },
        });
        Check("契约行：**抿嘴嘴宽**（9 段补录那一档）⇒ 猫嘴权重只有 "
            + contractWide["Ho/Drive/Style/CatMouth"].ToString("F3") + "、嘴宽轴 "
            + contractWide["Ho/Drive/Mouth/Pucker"].ToString("F3") + " ≈ +2.0（**门不许掐它**）",
            contractWide["Ho/Drive/Style/CatMouth"] < 0.25f && contractWide["Ho/Drive/Mouth/Pucker"] > 1.8f);

        // ⭐ **风格优先级链：鼓嘴 > 猫嘴 > 倒V**（2026-09-28 深夜用户定）—— 抑制因子只乘在 **auto 项**上
        //    （`HoExternal*` 手动常开不被压），且写在「维持」**之前**。⚠️ 这里量到的是**维持之前**的判据值：
        //    运行时 `维持` 把鼓嘴/猫嘴压成 0/1 ⇒ 抑制是**精确 0**；台架上只能验"方向与量级"。
        var prioCheek = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "cheekPuff", 0.9761f }, { "HoAutoCheek", 1f },
            { "mouthRollLower", 0.4595f }, { "mouthDimpleLeft", 0.80f }, { "mouthDimpleRight", 0.80f },
            { "mouthFrownLeft", 0.34f }, { "mouthFrownRight", 0.34f }, { "HoAutoCatMouth", 1f },
            { "mouthPucker", 0.9634f }, { "noseSneerLeft", 0.2942f }, { "noseSneerRight", 0.2942f },
            { "HoAutoInvertedV", 1f },
        });
        Check("风格优先级：**鼓嘴亮**（颊 " + prioCheek["Ho/Style/Cheek"].ToString("F3")
            + "）⇒ 猫嘴/倒V 的 auto 部分被压到 0（实际 猫嘴 "
            + prioCheek["Ho/Drive/Style/CatMouth"].ToString("F3") + " / 倒V "
            + prioCheek["Ho/Drive/Style/InvertedV"].ToString("F3") + "；维持之后是精确 0）",
            prioCheek["Ho/Style/Cheek"] > 0.9f
            && prioCheek["Ho/Drive/Style/CatMouth"] < 0.05f
            && prioCheek["Ho/Drive/Style/InvertedV"] < 0.05f);

        var prioCat = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "cheekPuff", 0.02f }, { "HoAutoCheek", 1f },
            { "mouthRollLower", 0.4595f }, { "mouthDimpleLeft", 0.80f }, { "mouthDimpleRight", 0.80f },
            { "mouthFrownLeft", 0.34f }, { "mouthFrownRight", 0.34f }, { "HoAutoCatMouth", 1f },
            { "mouthPucker", 0.9634f }, { "noseSneerLeft", 0.2942f }, { "noseSneerRight", 0.2942f },
            { "HoAutoInvertedV", 1f },
        });
        Check("风格优先级：**猫嘴亮**（" + prioCat["Ho/Drive/Style/CatMouth"].ToString("F3")
            + "，鼓嘴 " + prioCat["Ho/Style/Cheek"].ToString("F3")
            + "）⇒ 倒V 的 auto 部分按 (1 − 猫嘴) 缩小（实际 "
            + prioCat["Ho/Drive/Style/InvertedV"].ToString("F3") + " ⇒ 维持之后是精确 0）",
            prioCat["Ho/Drive/Style/CatMouth"] > 0.4f && prioCat["Ho/Style/Cheek"] < 0.05f
            && prioCat["Ho/Drive/Style/InvertedV"] > 0.40f && prioCat["Ho/Drive/Style/InvertedV"] < 0.60f);

        Check("风格优先级：三条都关时互不干扰（鼓嘴 0 / 猫嘴静息 " + contractRest["Ho/Drive/Style/CatMouth"].ToString("F3")
            + " / 倒V " + contractRest["Ho/Drive/Style/InvertedV"].ToString("F3") + "）",
            Near(contractRest["Ho/Style/Cheek"], 0f) && Near(contractRest["Ho/Drive/Style/InvertedV"], 0f));


        // ⭐ **2026-09-28 深夜：猫嘴的「决策线」是 `维持` 的 trigger，不是判据表达式** ——
        //    `维持` 的输出是二值（`HoFaceAnimationSession.Step` 直接返回 target）⇒ 这一行实际是
        //    「0/1 + 迟滞」；控制器里那两档 0.15/0.30 作用在 0/1 上等于恒等。所以「切不切」只看 trigger。
        //    旧参数 trigger 0.15 / threshold 0.13 ⇒ 释放点 = 0.15 − 0.13 = **0.02** ⇒ 碰一下就一直粘住
        //    （用户看到的「切到猫嘴」与「嘴宽轴被掐成 0」是同一个事件 —— 门读的就是这根 0/1 线）。
        float catTrigger = 0f, catRelease = 0f;
        foreach (var catRow in profile.outputs)
        {
            if (catRow.parameter != "Ho/Style/CatMouth" || catRow.modifiers == null) continue;
            foreach (var mod in catRow.modifiers)
            {
                if (mod.kind != HoFaceModifierKind.Steps || mod.steps == null || mod.steps.Count == 0) continue;
                catTrigger = mod.steps[0].trigger;
                catRelease = mod.steps[0].trigger - System.Math.Abs(mod.steps[0].threshold);
            }
        }
        Check("契约行：猫嘴卡在缝里 —— 触发线 " + catTrigger.ToString("F2") + "（释放 " + catRelease.ToString("F2")
            + "）必须高于误报最坏（抿嘴嘴宽 " + contractWide["Ho/Drive/Style/CatMouth"].ToString("F3")
            + "）、低于正例（猫嘴 " + contractCat["Ho/Drive/Style/CatMouth"].ToString("F3") + "）",
            catTrigger > contractWide["Ho/Drive/Style/CatMouth"] + 0.10f
            && catTrigger < contractCat["Ho/Drive/Style/CatMouth"] - 0.10f);
        Check("契约行：猫嘴的释放点**故意贴地**（" + catRelease.ToString("F2") + "，锁存是用户要的：表演时要挂得住）"
            + " ⇒ 防误报全靠触发线，不靠释放线", Near(catRelease, 0.02f));




        var contractCheek = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)

        {
            { "cheekPuff", 0.9761f }, { "mouthLeft", 0.03f }, { "mouthRight", 0.03f }, { "HoAutoCheek", 1f },
        });
        Check("契约行：只鼓嘴亮（颊 0.9761）⇒ 倒V 权重 0、门 "
            + contractCheek["Ho/Drive/Gate/MouthStyle"].ToString("F3") + " ≤ 0.1（**两个形态都关同一扇门**）",
            Near(contractCheek["Ho/Drive/Style/InvertedV"], 0f) && contractCheek["Ho/Drive/Gate/MouthStyle"] <= 0.1f);

        var contractNeither = runProfile(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "mouthPucker", 0.3797f }, { "noseSneerLeft", 0.2036f }, { "noseSneerRight", 0.2036f },
            { "cheekPuff", 0.0181f }, { "HoAutoInvertedV", 1f }, { "HoAutoCheek", 1f },
        });
        Check("契约行：两个形态都没亮（静息 / 只是张嘴笑）⇒ 门 "
            + contractNeither["Ho/Drive/Gate/MouthStyle"].ToString("F3") + " = 1（张嘴笑照常走）",
            Near(contractNeither["Ho/Drive/Gate/MouthStyle"], 1f));
    }

    /// <summary>我们自己 append 的**外部开关**（`HoAuto*` / `HoExternal*`）—— 不是设备线。</summary>
    private static bool IsInjectedWire(string wire)
    {
        if (string.IsNullOrEmpty(wire)) return false;
        return wire.StartsWith("HoAuto", StringComparison.Ordinal)
            || wire.StartsWith("HoExternal", StringComparison.Ordinal);
    }

    /// <summary>那根外部开关线一帧都没来过时，中间层取它的 <c>defaultValue</c>。</summary>
    private static float InjectedWireDefault(HoFaceMiddleware profile, string name)
    {
        foreach (var input in profile.inputs)
            if (input != null && input.expression == name) return input.defaultValue;
        return float.NaN;
    }

    private static bool Check(string what, bool ok, string detail = null)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine("  OK   " + what);
        }
        else
        {
            _failed++;
            Console.WriteLine("  FAIL " + what + (detail != null ? "   [" + detail + "]" : ""));
        }
        return ok;
    }
}
