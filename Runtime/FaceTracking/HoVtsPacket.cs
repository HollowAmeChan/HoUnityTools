// HoVtsPacket.cs  --  VTS 手机包（JSON）→「线名 → 原值」
//
// 【为什么单独一个文件、而且不碰 socket】
// 解析是这条链上最容易出错、也最值得单独验的一步。抽成纯静态函数之后，
// 它可以脱离 Unity / Warudo 在离线测试里跑（见 .research/profile-json-test），
// 而不是只能"连上手机试试看"。
//
// 【为什么不用 JsonUtility（第三次了）】
// 官方载荷里 `BlendShapes` 是 `List<VTSTrackingDataEntry>`（`{k, v}` 的列表）。
// 用 `JsonUtility.FromJson` 解析的结果是：**12 个头眼分量全在、52 个形态键全丢** ——
// 解析"成功"、字段名也对，但最要紧的东西没了（本机实测，`本帧键数=15`）。
// `JsonUtility` 在 Warudo 播放器里会静默丢掉 `List<嵌套类>` 字段，所以这里走
// 我们自己的 `HoJsonReader`。详见 Runtime/FaceTracking/HoJson.cs 里记的三次事故。
//
// 【字段名来源（不是逆向）】
// https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest
//   Assets/VTubeStudioBlendshapeDataReceiver/VTubeStudioRawTrackingData.cs
//   Timestamp(long) / Hotkey(int) / FaceFound(bool) /
//   Rotation、Position、EyeLeft、EyeRight(Vector3 —— Unity 序列化成 {x,y,z}) /
//   BlendShapes(List<{k:string, v:float}>)
// README 原话："Some fields may be **added** to this payload in the future" →
// 所以未知字段一律跳过，不许因为多了个字段就整包失败。

using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>VTS 手机包的解析。**只摊平，不解释**：线名与数值都照原样交出去。</summary>
    public static class HoVtsPacket
    {
        /// <summary>`FaceFound` 落到线名空间时用的键（1 = 找到脸）。</summary>
        public const string FaceFoundKey = "FaceFound";

        /// <summary>`Hotkey` 落到线名空间时用的键（没按是 −1）。</summary>
        public const string HotkeyKey = "Hotkey";

        /// <summary>
        /// `Timestamp` 落到线名空间时用的键。
        /// ⚠️ 线上是 13 位毫秒，而线名空间的数值类型是 float（约 7 位有效数字），
        /// 所以这个键**只够调试时看个大概**，不要拿它做时间判断。
        /// </summary>
        public const string TimestampKey = "Timestamp";

        /// <summary>请求消息类型（官方文档里那个字符串）。</summary>
        public const string RequestMessageType = "iOSTrackingDataRequest";

        /// <summary>我们向手机自报的名字 —— **手机上 `Connected VSF Clients:` 列表里显示的就是它**。</summary>
        public const string SenderName = "HoFaceTracking";

        /// <summary>每次买多少秒的数据。官方允许 0.5–10；我们每秒续一次，所以 5 秒足够宽裕。</summary>
        public const float RequestSeconds = 5f;

        /// <summary>
        /// 构造"请求推流"的包。**手写这几行 JSON**，不用 `JsonUtility`：
        /// 它已经在本项目咬过三次（见 <see cref="HoJsonReader"/> 的记录），不必再给第四次机会；
        /// 而且这里全是字面量 ASCII，手写反而更清楚、也更容易断言（离线测试就断言了它的原文）。
        ///
        /// 字段约束照官方文档：`time` ∈ [0.5, 10]、`sentBy` 长度 1–64、`ports` 至少一个。
        /// 手机收到之后会把数据发回**这个包的源 IP**、端口用 `ports` 里列的那些。
        /// </summary>
        public static string BuildRequest(int localPort)
        {
            return "{\"messageType\":\"" + RequestMessageType + "\",\"time\":"
                + RequestSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"sentBy\":\"" + SenderName + "\",\"ports\":[" + localPort + "]}";
        }

        /// <summary>
        /// 解析一包。成功时 <paramref name="values"/> 是「线名 → 原值」：
        /// <c>Rotation_x/y/z</c>、<c>Position_x/y/z</c>、<c>EyeLeft_x/y/z</c>、<c>EyeRight_x/y/z</c>、
        /// <c>FaceFound</c>、<c>Hotkey</c>、<c>Timestamp</c>，以及 **52 个形态键的原始线名**。
        /// </summary>
        public static bool TryParse(string json, out Dictionary<string, float> values, out bool faceFound, out string error)
        {
            values = null;
            faceFound = false;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "空包";
                return false;
            }

            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            try
            {
                var reader = new HoJsonReader(json);

                // 手写循环（不用 ReadObject/ReadArray 那套便捷走法）：这条是 60 FPS 的热路径，
                // 那套走法会为每个对象分配闭包。
                reader.Expect('{');
                if (!reader.TryConsume('}'))
                {
                    while (true)
                    {
                        string key = reader.ReadString();
                        reader.Expect(':');
                        switch (key)
                        {
                            case "Timestamp": result[TimestampKey] = (float)reader.ReadNumber(); break;
                            case "Hotkey": result[HotkeyKey] = (float)reader.ReadNumber(); break;
                            case "FaceFound":
                                faceFound = reader.ReadBool();
                                result[FaceFoundKey] = faceFound ? 1f : 0f;
                                break;
                            case "Rotation": ReadVector(reader, result, "Rotation"); break;
                            case "Position": ReadVector(reader, result, "Position"); break;
                            case "EyeLeft": ReadVector(reader, result, "EyeLeft"); break;
                            case "EyeRight": ReadVector(reader, result, "EyeRight"); break;
                            case "BlendShapes": ReadBlendShapes(reader, result); break;
                            default: reader.SkipValue(); break;   // 官方说以后会加字段
                        }

                        if (reader.TryConsume(',')) continue;
                        reader.Expect('}');
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }

            values = result;
            return true;
        }

        /// <summary>`Rotation` → `Rotation_x/_y/_z`（分量名照 Unity 的 Vector3 字段名）。</summary>
        private static void ReadVector(HoJsonReader reader, Dictionary<string, float> into, string name)
        {
            float x = 0f, y = 0f, z = 0f;

            reader.Expect('{');
            if (!reader.TryConsume('}'))
            {
                while (true)
                {
                    string field = reader.ReadString();
                    reader.Expect(':');
                    if (field == "x") x = reader.ReadFloat();
                    else if (field == "y") y = reader.ReadFloat();
                    else if (field == "z") z = reader.ReadFloat();
                    else reader.SkipValue();

                    if (reader.TryConsume(',')) continue;
                    reader.Expect('}');
                    break;
                }
            }

            into[name + "_x"] = x;
            into[name + "_y"] = y;
            into[name + "_z"] = z;
        }

        /// <summary>`BlendShapes: [{k, v}, …]` → 线名 = 原值。**这一条就是 JsonUtility 丢掉的那 52 个键。**</summary>
        private static void ReadBlendShapes(HoJsonReader reader, Dictionary<string, float> into)
        {
            reader.Expect('[');
            if (reader.TryConsume(']')) return;

            while (true)
            {
                string name = null;
                float value = 0f;

                reader.Expect('{');
                if (!reader.TryConsume('}'))
                {
                    while (true)
                    {
                        string field = reader.ReadString();
                        reader.Expect(':');
                        if (field == "k") name = reader.ReadString();
                        else if (field == "v") value = reader.ReadFloat();
                        else reader.SkipValue();

                        if (reader.TryConsume(',')) continue;
                        reader.Expect('}');
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(name) && !float.IsNaN(value) && !float.IsInfinity(value))
                    into[name] = value;

                if (reader.TryConsume(',')) continue;
                reader.Expect(']');
                break;
            }
        }
    }
}
