// VBridger 隐式输入层 × 我们中间层表达式的**覆盖验证**（脱离 Unity 跑）。
//
// 目的：逐条回答"VB 在代码里硬编码的那些处理（改名 / 量纲 / 合并 / 标定 / 姿态分量 / 门控），
// 用我们 .hoface.json 的行（`参数名 = 曲线(表达式(源键…))`）能不能表达出来"。
//
// 方法：每条 VB 行为写成一条行——**左值 = 我们要的规范名（这里只打印）**，RHS 用**线名**（协议原样发的名字）
// 当变量；用 Dictionary reader 喂进协议的原始数值，对答案（期望值按 VB 源码语义）。
// 跑法：dotnet run --project Tools~/FaceTracking/expression-coverage
using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;

internal static class Coverage
{
    private static int passed, failed;

    private static void Row(string target, string expression, Dictionary<string, float> wire, float expected, float tolerance = 0.0005f)
    {
        if (!HoFaceExpression.TryParse(expression, out var parsed, out string error))
        {
            Console.WriteLine($"  [语法错] {target,-26} <- {expression}");
            Console.WriteLine($"           {error}");
            failed++;
            return;
        }

        float actual = parsed.Evaluate(name => wire.TryGetValue(name, out float value) ? value : 0f);
        bool ok = Math.Abs(actual - expected) <= tolerance;
        if (ok) passed++; else failed++;
        Console.WriteLine($"  [{(ok ? "OK  " : "FAIL")}] {target,-26} <- {expression}");
        Console.WriteLine($"         期望 {expected,10:F5}   实得 {actual,10:F5}");
    }

    private static Dictionary<string, float> Wire(params (string name, float value)[] pairs)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs) result[name] = value;
        return result;
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title);
    }

    private static int Main()
    {
        Console.WriteLine("VB 的隐式输入层 → 我们中间层表达式：覆盖验证");
        Console.WriteLine("（期望值按 VBridger 源码语义；行号见 docs/archive/VBRIDGER_IO_VOCABULARY.md）");

        Section("A 改名：线名 -> 规范名（VB 用改名表 + 下标；我们只写右值是线名、左值是规范名）");
        Row("eyeBlinkLeft", "eyeBlink_L * 0.01", Wire(("eyeBlink_L", 30f)), 0.3f);
        Row("eyeBlinkLeft（VTS 拼写）", "EyeBlinkLeft", Wire(("EyeBlinkLeft", 0.3f)), 0.3f);
        Row("jawRight（不靠下标）", "JawRight", Wire(("JawRight", 0.4f), ("JawLeft", 0.7f)), 0.4f);

        Section("B 量纲：VB 每个 handler 自己记得除/不除，我们写在行里");
        Row("jawOpen（iFacialMocap 0..100）", "jawOpen * 0.01", Wire(("jawOpen", 12f)), 0.12f);
        Row("jawOpen（VTS 本来 0..1）", "JawOpen", Wire(("JawOpen", 0.12f)), 0.12f);
        Row("headRotX（度 -> 弧度）", "head_0 * 0.0174533", Wire(("head_0", 30f)), 0.5236f);
        Row("headPosY（米，直接透传）", "head_4", Wire(("head_4", 0.02f)), 0.02f);

        Section("C 合并与派生：VB 里是硬编码特例 / 预设公式");
        Row("cheekPuff（NVidia 左右取大）", "max(cheekPuff_L, cheekPuff_R) * 0.01", Wire(("cheekPuff_L", 20f), ("cheekPuff_R", 55f)), 0.55f);
        Row("EyeOpenLeft（0.5 中性轴）", ".5 + (EyeBlinkLeft * -.8 + EyeWideLeft * .8)", Wire(("EyeBlinkLeft", 0.5f), ("EyeWideLeft", 0.25f)), 0.3f);
        Row("MouthSmile（正负相减归一）",
            "(2 - (MouthFrownLeft + MouthFrownRight + MouthPucker) + (MouthSmileLeft + MouthSmileRight + (MouthDimpleLeft + MouthDimpleRight) / 2)) / 4",
            Wire(("MouthFrownLeft", 0.1f), ("MouthFrownRight", 0.1f), ("MouthPucker", 0.2f),
                 ("MouthSmileLeft", 0.6f), ("MouthSmileRight", 0.4f), ("MouthDimpleLeft", 0.2f), ("MouthDimpleRight", 0.2f)),
            0.7f);

        Section("D 标定与全局系数：VB 的 MapValue / InputCurves / Mouth Multiplier");
        Row("静息标定（静止值压回 0）", "clamp((jawOpen * 0.01 - 0.03) / (1 - 0.03), 0, 1)", Wire(("jawOpen", 3f)), 0f);
        Row("静息标定（半程仍是一半）", "clamp((jawOpen * 0.01 - 0.03) / (1 - 0.03), 0, 1)", Wire(("jawOpen", 51.5f)), 0.5f);
        Row("Mouth Multiplier（写进行里）", "jawOpen * 0.01 * 1.2", Wire(("jawOpen", 50f)), 0.6f);

        Section("E 门控与选择性：VB 用 viseme_*_abs 当开关、只对 mouthKeys 乘系数");
        Row("门控（静音时走面捕）", "if('viseme_SIL_abs > 0.5', jawOpen * 0.01, voiceA * 0.8)",
            Wire(("viseme_SIL_abs", 1f), ("jawOpen", 40f), ("voiceA", 0.9f)), 0.4f);
        Row("门控（有声时走元音）", "if('viseme_SIL_abs > 0.5', jawOpen * 0.01, voiceA * 0.8)",
            Wire(("viseme_SIL_abs", 0f), ("jawOpen", 40f), ("voiceA", 0.9f)), 0.72f);
        Row("音频口型（加权和 x 音量）",
            "(viseme_AA * 1 + viseme_EE * 0.8 + viseme_OH * 1.1) * volume * (1 - viseme_SIL_abs)",
            Wire(("viseme_AA", 0.5f), ("viseme_EE", 0.25f), ("viseme_OH", 0f), ("volume", 0.8f), ("viseme_SIL_abs", 0f)),
            0.56f);

        Section("F 自指：线名与规范名同名时（iFacialMocap 的 jawOpen）");
        Row("jawOpen（RHS 读到的仍是线名原值）", "jawOpen * 0.01", Wire(("jawOpen", 77f)), 0.77f);

        Section("G 覆盖不到的地方（本轮实测，不是猜）");
        // G1 VB 自带 stabil(var,dif)：**有状态**的迟滞防抖，纯函数表达不了。
        bool stabilParses = HoFaceExpression.TryParse("stabil(jawOpen, 0.1)", out _, out string stabilError);
        Console.WriteLine($"  [{(stabilParses ? "FAIL" : "缺口")}] VB 的 stabil(var,dif)（迟滞防抖）");
        Console.WriteLine($"         TryParse = {stabilParses}" + (stabilParses ? "" : $"（{stabilError}）"));
        Console.WriteLine("         → 表达式是纯函数，做不了「记住上一帧」；要补就得做成输出行的修饰符（像 Smooth/Delay 那样）");
        if (stabilParses) failed++; else passed++;

        // G2 缺键语义：VB 只在收到键时写全局变量（缺键 = 保持上一帧），我们读不到就是 0。
        float missing = 0f;
        HoFaceExpression.TryParse("jawOpen * 0.01", out var missingExpr, out _);
        missing = missingExpr.Evaluate(_ => 0f);
        Console.WriteLine($"  [{(Math.Abs(missing) < 0.0001f ? "缺口" : "FAIL")}] 缺键语义：VB 保持上一帧，我们读作 {missing:F5}");
        Console.WriteLine("         → 要么接受会话那套「断流 → 淡回中性」（现在的行为），要么给输入行加 Hold 修饰符");
        if (Math.Abs(missing) < 0.0001f) passed++; else failed++;

        Console.WriteLine("  [缺口] iFacialMocap 根本没有 faceFound 字段（VTS 手机载荷才有）");
        Console.WriteLine("         → 不是表达力问题：那个协议里没有这个量，配置文件里引用它只会得到 0");
        Console.WriteLine("  [缺口] 没有「全局常量」段：Mouth Multiplier 那种系数要写进每一条相关行（能表达，但不省事）");

        Console.WriteLine();
        Console.WriteLine($"通过 {passed} / {passed + failed}");
        return failed == 0 ? 0 : 1;
    }
}
