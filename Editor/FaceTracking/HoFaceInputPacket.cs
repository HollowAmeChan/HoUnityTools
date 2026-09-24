using System;
using System.Collections.Generic;

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
    /// 线名的形式由协议决定（现在只有 VTS 手机：`EyeBlinkLeft` / `JawOpen`）；
    /// 姿态这种"一字段多分量"的按 `<字段>_<下标>` 摊平（VTS 的 `Rotation` → `Rotation_x/y/z`）。
    /// 下标与分量顺序**照原样**，不做任何语义解释（哪一个是"绝对角"、哪一个是"位置"由配置文件说了算）。
    ///
    /// ⚠️ 这个类**只当容器**。载荷的解析在 `HoVtsPacket`（包里那份，Unity 侧与 Warudo 侧共用，
    /// 离线测试覆盖）—— 这里曾经有一份 `JsonUtility.FromJson&lt;VtsTrackingData&gt;` + 嵌套 DTO，
    /// 那正是"静默丢掉 52 个形态键"的模式，已经删掉。**别再加回来。**
    /// </summary>
    public sealed class HoFaceInputPacket
    {
        /// <summary>线名 → 原值。名字大小写敏感（协议拼写是什么就是什么）。</summary>
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>本帧带来了多少个字段（键数 = <see cref="Values"/>.Count）。</summary>
        public int EntryCount => Values.Count;

        public void Set(string wire, float value)
        {
            if (string.IsNullOrEmpty(wire) || float.IsNaN(value) || float.IsInfinity(value)) return;
            Values[wire] = value;
        }

        public bool Has(string wire) => wire != null && Values.ContainsKey(wire);
        public float Get(string wire) => wire != null && Values.TryGetValue(wire, out float value) ? value : 0f;
    }
}
