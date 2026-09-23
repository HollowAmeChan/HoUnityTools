using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **一帧原始输入**：只有"手机发来的线名 → 原值"。
    ///
    /// 这里**不做任何隐式处理** —— 不改名、不换算、不猜缺键、不合并左右。理由：
    /// VBridger 把这些散在五个协议 handler 里（量纲 `/100` 在默认路、VTS 路不除、NVidia 硬编码
    /// `cheekPuff=max(L,R)`、改名按下标还对不上 Apple 的顺序），换一个源要读五处代码。
    /// 我们的约定是：**接收端只交原样，映射与量纲写在中间层配置的"输入行"里**
    /// （`规范名 = 曲线(表达式(线名…))`），一处可 diff、可改。
    ///
    /// 线名的形式取决于协议：iFacialMocap 用 `eyeBlink_L` / `jawOpen`，VTS 手机用 `EyeBlinkLeft` / `JawOpen`；
    /// 姿态这种"一字段多分量"的按 `<字段>_<下标>` 摊平（iFacialMocap 的 `=head#` 6 个数 → `head_0..head_5`、
    /// `leftEye#` → `leftEye_0..2`；VTS 的 `Rotation` → `Rotation_x/y/z`）。
    /// 下标与分量顺序**照原样**，不做任何语义解释（哪一个是"绝对角"、哪一个是"位置"由配置文件说了算）。
    /// </summary>
    public sealed class HoFaceInputPacket
    {
        /// <summary>线名 → 原值。名字大小写敏感（两个协议的拼写本来就不同）。</summary>
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>本帧带来了多少个字段（键数 = <see cref="Values"/>.Count）。</summary>
        public int EntryCount => Values.Count;

        /// <summary>格式就不对的字段数（没有分隔符 / 数值解析失败）。</summary>
        public int InvalidCount;

        public void Set(string wire, float value)
        {
            if (string.IsNullOrEmpty(wire) || float.IsNaN(value) || float.IsInfinity(value)) return;
            Values[wire] = value;
        }

        public bool Has(string wire) => wire != null && Values.ContainsKey(wire);
        public float Get(string wire) => wire != null && Values.TryGetValue(wire, out float value) ? value : 0f;

        /// <summary>
        /// VTS 手机载荷里的 JSON：字段名照原样摊成线名。**值不做任何换算**（官方说这是"iOS 原始值"，本来就是 0..1）。
        /// 实现放在接收端文件里（<see cref="VtsIphoneReceiver"/>），这个类只当容器。
        /// </summary>
        internal static bool TryParseJson(string json, out HoFaceInputPacket packet, out VtsTrackingData raw)
        {
            packet = new HoFaceInputPacket();
            raw = null;
            if (string.IsNullOrEmpty(json)) return false;
            try { raw = JsonUtility.FromJson<VtsTrackingData>(json); }
            catch { return false; }
            return raw != null;
        }

        /// <summary>
        /// VTS iPhone 载荷的字段定义，逐字照官方 `VTubeStudioRawTrackingData.cs`。
        /// 官方要求"以后可能加字段，反序列化不能因为不认识的字段就失败" —— JsonUtility 正好忽略多余字段。
        /// </summary>
        [Serializable]
        public sealed class VtsEntry
        {
            public string k;
            public float v;
        }

        [Serializable]
        public sealed class VtsTrackingData
        {
            public long Timestamp;
            public int Hotkey = -1;
            public bool FaceFound;
            public Vector3 Rotation;
            public Vector3 Position;
            public List<VtsEntry> BlendShapes = new List<VtsEntry>();
            public Vector3 EyeLeft;
            public Vector3 EyeRight;
        }
    }
}
