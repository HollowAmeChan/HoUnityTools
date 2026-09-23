using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    // ── 配置文件的 DTO ────────────────────────────────────────────────────────
    // 这些类只服务 JSON：字段名短、能手写、能 diff。曲线在 Unity 里是 AnimationCurve，
    // 在文件里就是关键点列表（Unity 的 JsonUtility 不认识 AnimationCurve，所以必须自己搬）。

    [Serializable]
    internal sealed class HoFaceProfileKeyDto
    {
        public float t;      // time
        public float v;      // value
        public float inT;    // inTangent
        public float outT;   // outTangent
    }

    [Serializable]
    internal sealed class HoFaceProfileCurveDto
    {
        public List<HoFaceProfileKeyDto> keys = new List<HoFaceProfileKeyDto>();
    }

    [Serializable]
    internal sealed class HoFaceProfileStepDto
    {
        public float trigger = 0.3f;
        public float target = 0.4f;
        public float hold;
        public float threshold = 0.1f;
    }

    [Serializable]
    internal sealed class HoFaceProfileModifierDto
    {
        public string kind = "smooth";     // smooth / delay / steps
        public float seconds;
        public List<HoFaceProfileStepDto> steps = new List<HoFaceProfileStepDto>();
    }

    [Serializable]
    internal sealed class HoFaceProfileOutputDto
    {
        public string parameter = "";
        public string expression = "";
        public string notes = "";
        public HoFaceProfileCurveDto curve = new HoFaceProfileCurveDto();
        public List<HoFaceProfileModifierDto> modifiers = new List<HoFaceProfileModifierDto>();
    }

    [Serializable]
    internal sealed class HoFaceProfileDto
    {
        public string format = HoFaceProfile.Format;
        public int version = HoFaceProfile.Version;
        public string displayName = "";
        public string notes = "";
        /// <summary>输入行：线名 → 规范名（+ 量纲）。</summary>
        public List<HoFaceProfileOutputDto> inputs = new List<HoFaceProfileOutputDto>();
        public List<HoFaceProfileOutputDto> outputs = new List<HoFaceProfileOutputDto>();
    }

    /// <summary>
    /// **中间层配置文件**（我们自己的 JSON）：**输入行**（线名 → 规范名）+ **输出行**
    /// （规范名 → 控制器参数名），两类行同一套形状 <c>名字 = 曲线(表达式) + 有序修饰符</c>。
    ///
    /// 为什么是文本文件而不是 Unity 资产：它是**数据**，应该能 diff、能用任何编辑器打开、
    /// 能随手发给别人；同一份 spec 以后还要给 Warudo / VRC 那两套中间层复用。
    /// 形状照 VBridger 的 `.vbridger`（一个 store 里一列参数行），但**不抄它的加密** ——
    /// 我们要的是可读可 diff（VBridger 加密只是为了保护它的商业预设）。
    ///
    /// ⚠️ **映射与量纲全在 `inputs` 里**：接收端只交"手机发来的线名 + 原值"，
    /// 所以 `eyeBlink_L * 0.01` 这种换算必须写成一行。这样换设备/换协议只改这份文件。
    ///
    /// 文件长这样：
    /// <code>
    /// {
    ///   "format": "ho-face-middleware",
    ///   "version": 2,
    ///   "displayName": "ho-2d-test1",
    ///   "inputs": [
    ///     { "parameter": "jawOpen", "expression": "jawOpen * 0.01", "notes": "iFacialMocap" },
    ///     { "parameter": "jawOpen", "expression": "JawOpen", "notes": "VTS 手机" }
    ///   ],
    ///   "outputs": [
    ///     { "parameter": "ARKit/jawOpen", "expression": "jawOpen",
    ///       "curve": { "keys": [ { "t": 0, "v": 0, "inT": 0, "outT": 0 },
    ///                            { "t": 1, "v": 1, "inT": 0, "outT": 0 } ] },
    ///       "modifiers": [ { "kind": "smooth", "seconds": 0.03 } ] }
    ///   ]
    /// }
    /// </code>
    /// 未知字段会被忽略（向前兼容）；`kind` 不认识时那一条修饰符被跳过，并在面板上点名。
    /// </summary>
    public static class HoFaceProfile
    {
        public const string Format = "ho-face-middleware";
        /// <summary>2 = 增加了 `inputs`（1 的旧文件仍然能读，只是没有输入行）。</summary>
        public const int Version = 2;
        public const string Extension = ".hoface.json";

        /// <summary>读一份配置。失败时 <paramref name="error"/> 是中文说明（面板直接显示）。</summary>
        public static bool TryParse(string json, out HoFaceMiddleware middleware, out string error)
        {
            middleware = null;
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "配置文件是空的。";
                return false;
            }

            HoFaceProfileDto dto;
            try { dto = JsonUtility.FromJson<HoFaceProfileDto>(json); }
            catch (Exception e)
            {
                error = "JSON 解析失败：" + e.Message;
                return false;
            }

            if (dto == null)
            {
                error = "JSON 解析失败（内容不是一个对象）。";
                return false;
            }

            if (!string.IsNullOrEmpty(dto.format) && dto.format != Format)
            {
                error = "这不是 Ho 的中间层配置（format = " + dto.format + "）。";
                return false;
            }

            middleware = new HoFaceMiddleware { displayName = dto.displayName, notes = dto.notes };
            ParseRows(dto.inputs, middleware.inputs, ref error);
            ParseRows(dto.outputs, middleware.outputs, ref error);

            if (middleware.outputs.Count == 0) error = "配置文件里一行输出都没有。";
            return middleware.outputs.Count > 0;
        }

        /// <summary>把一类行（输入行 / 输出行）从 DTO 读进数据对象。两类行形状完全一样。</summary>
        private static void ParseRows(List<HoFaceProfileOutputDto> source, List<HoFaceOutput> into, ref string error)
        {
            if (source == null) return;
            foreach (var item in source)
            {
                if (item == null || string.IsNullOrEmpty(item.parameter)) continue;
                var row = new HoFaceOutput
                {
                    parameter = item.parameter,
                    expression = item.expression ?? "",
                    notes = item.notes
                };
                if (item.curve != null && item.curve.keys != null && item.curve.keys.Count > 0)
                {
                    var keys = new List<Keyframe>();
                    foreach (var key in item.curve.keys)
                        if (key != null) keys.Add(new Keyframe(key.t, key.v, key.inT, key.outT));
                    if (keys.Count > 0) row.curve = new AnimationCurve(keys.ToArray());
                }

                if (item.modifiers != null)
                    foreach (var modifier in item.modifiers)
                    {
                        if (modifier == null) continue;
                        var parsed = new HoFaceModifier { seconds = modifier.seconds };
                        switch ((modifier.kind ?? "").ToLowerInvariant())
                        {
                            case "smooth": parsed.kind = HoFaceModifierKind.Smooth; break;
                            case "delay": parsed.kind = HoFaceModifierKind.Delay; break;
                            case "steps": parsed.kind = HoFaceModifierKind.Steps; break;
                            default:
                                error = (error == null ? "" : error + "；")
                                    + "认不出的修饰符 kind = " + modifier.kind + "（" + item.parameter + "）";
                                continue;
                        }

                        if (modifier.steps != null)
                            foreach (var step in modifier.steps)
                                if (step != null)
                                    parsed.steps.Add(new HoFaceStep
                                    {
                                        trigger = step.trigger, target = step.target,
                                        hold = step.hold, threshold = step.threshold
                                    });
                        row.modifiers.Add(parsed);
                    }

                into.Add(row);
            }
        }

        /// <summary>写成配置文件（带 format/version 头，2 空格缩进）。</summary>
        public static string Write(HoFaceMiddleware middleware)
        {
            var dto = new HoFaceProfileDto
            {
                format = Format,
                version = Version,
                displayName = middleware != null ? middleware.displayName : "",
                notes = middleware != null ? middleware.notes : ""
            };

            if (middleware != null && middleware.inputs != null)
                foreach (var row in middleware.inputs) WriteRow(row, dto.inputs);
            if (middleware != null && middleware.outputs != null)
                foreach (var row in middleware.outputs) WriteRow(row, dto.outputs);

            return JsonUtility.ToJson(dto, true);
        }

        private static void WriteRow(HoFaceOutput row, List<HoFaceProfileOutputDto> into)
        {
            if (row == null) return;
            var output = new HoFaceProfileOutputDto
            {
                parameter = row.parameter ?? "",
                expression = row.expression ?? "",
                notes = row.notes ?? ""
            };

            if (row.curve != null && row.curve.length > 0)
                foreach (var key in row.curve.keys)
                    output.curve.keys.Add(new HoFaceProfileKeyDto { t = key.time, v = key.value, inT = key.inTangent, outT = key.outTangent });

            if (row.modifiers != null)
                foreach (var modifier in row.modifiers)
                {
                    if (modifier == null) continue;
                    var item = new HoFaceProfileModifierDto { seconds = modifier.seconds };
                    switch (modifier.kind)
                    {
                        case HoFaceModifierKind.Delay: item.kind = "delay"; break;
                        case HoFaceModifierKind.Steps: item.kind = "steps"; break;
                        default: item.kind = "smooth"; break;
                    }

                    if (modifier.steps != null)
                        foreach (var step in modifier.steps)
                            if (step != null)
                                item.steps.Add(new HoFaceProfileStepDto
                                {
                                    trigger = step.trigger, target = step.target,
                                    hold = step.hold, threshold = step.threshold
                                });
                    output.modifiers.Add(item);
                }

            into.Add(output);
        }

        /// <summary>内置默认的配置文本（"新建配置文件"与包内那份默认配置都用它）。</summary>
        public static string WriteDefaults() => Write(HoFaceMiddlewareDefaults.Create());
    }
}
