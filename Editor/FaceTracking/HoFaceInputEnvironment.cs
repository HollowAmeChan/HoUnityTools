using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 输入源的类型。**这里就是"隐式注册表"**：加一个设备 = 加一个 <see cref="HoFaceReceiverBase"/> 子类
    /// + 在这里加一个成员 + 在 <see cref="HoFaceReceiverFactory.Create"/> 与 <see cref="HoFaceReceiverFactory.DisplayName"/>
    /// 各加一行。差异只在接收端那个文件的 <c>TryParsePacket</c> 里，其余形状由基类固定。
    ///
    /// ⚠️ **只留 VTS**：iFacialMocap 那条已经删掉了 —— 它给不出 `FaceFound`，而这正是
    /// Warudo 侧"丢追回中性"整条机制唯一的开关（见 HoFaceDebugSettings 的 IsTracked 说明）。
    /// 要再加设备就照上面那三步加回来，别把"猜"塞进接收器。
    /// </summary>
    public enum HoFaceSourceKind
    {
        /// <summary>VTubeStudio iPhone App 的 "3rd Party PC Clients"：JSON 包，0..1，带 faceFound/热键。</summary>
        VtsIphone = 0
    }

    /// <summary>
    /// 一路输入源的配置条目。**顺序就是优先级**（环境里的顺序）：某个通道取"第一个还新鲜、且这一帧带来了它"的源。
    /// 留着一份列表是为了以后加第二个设备时不用改结构；现在只有 VTS 一项。
    /// </summary>
    [Serializable]
    public sealed class HoFaceSourceEntry
    {
        public HoFaceSourceKind kind;
        public bool enabled = true;
        [Tooltip("手机在局域网里的地址。不同源可以是不同手机。")]
        public string phoneIp = "192.168.1.100";
        [Tooltip("本机监听端口。手机把数据推回这个端口（通过请求包里的 ports 告诉它）。")]
        public int localPort = 49984;
    }

    /// <summary>
    /// **输入环境**：工程级的一份有序源列表（用户的选择：环境只在工程设置里，不挂到每个角色上）。
    /// 从它派生两份东西：① 要拉起哪些接收端；② 每个通道该信谁（见 <see cref="HoFaceInputHub"/> 的合并）。
    /// </summary>
    [FilePath("UserSettings/HoUnityTools/FaceInput.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class HoFaceInputEnvironment : ScriptableSingleton<HoFaceInputEnvironment>
    {
        /// <summary>有序：**越靠前优先级越高**。</summary>
        public List<HoFaceSourceEntry> sources = Default();

        /// <summary>
        /// 用户是否处于"想连着"的状态。**必须持久化**：进播放模式会域重载，静态字段会被清掉，
        /// 而这个意图得活着 —— 否则"按 Play"就等于把手机连接丢掉了。
        /// </summary>
        public bool wantConnected;

        public void Persist() => Save(true);

        private bool sanitized;

        private void OnEnable()
        {
            // ⚠️ `sources` 是**落盘**的：改协议时（比如删掉 iFacialMocap）不会自动清掉旧列表，
            // 里面会留下 kind 已经非法的僵尸条目 —— 而 Create() 碰到不认识 kind 会直接抛异常。
            // 所以每次加载都过一遍：丢掉不支持的源，一个都不剩就回默认。
            if (sanitized) return;
            sanitized = true;
            // ⚠️ 这里**不能当场 `Save(true)`**：OnEnable 是 `CreateAndLoad()` 反序列化路径上的一环，
            // 那一刻资产已经持久化了，Unity 会拒收并打
            // 「You may not pass in objects that are already persistent」——清理结果反而没落盘。
            // 推迟一帧再写，效果一样（内存里已经是干净的列表）。
            if (Sanitize()) EditorApplication.delayCall += () => Save(true);
        }

        /// <summary>丢掉不再支持的源。返回是否改过（改过就该落盘）。</summary>
        public bool Sanitize()
        {
            int removed = sources.RemoveAll(entry => entry == null || entry.kind != HoFaceSourceKind.VtsIphone);
            bool changed = removed > 0;
            if (sources.Count == 0)
            {
                sources.AddRange(Default());
                changed = true;
            }
            return changed;
        }

        /// <summary>默认环境：只有 VTS 手机一项。</summary>
        public static List<HoFaceSourceEntry> Default() => new List<HoFaceSourceEntry>
        {
            new HoFaceSourceEntry { kind = HoFaceSourceKind.VtsIphone, phoneIp = "192.168.1.100", localPort = 49984 }
        };
    }

    /// <summary>类型 → 接收端实例 / 显示名。新增设备时只改这里。</summary>
    public static class HoFaceReceiverFactory
    {
        public static IHoFaceInputReceiver Create(HoFaceSourceKind kind)
        {
            switch (kind)
            {
                case HoFaceSourceKind.VtsIphone: return new VtsIphoneReceiver();
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string DisplayName(HoFaceSourceKind kind)
        {
            switch (kind)
            {
                case HoFaceSourceKind.VtsIphone: return "VTS 手机（3rd Party PC Clients）";
                default: return kind.ToString();
            }
        }

        /// <summary>面板提示（不建实例也能取：建实例会白开一个对象）。</summary>
        public static string Hint(HoFaceSourceKind kind)
        {
            switch (kind)
            {
                case HoFaceSourceKind.VtsIphone:
                    return "VTS 手机版设置第一页底部打开「3rd Party PC Clients」（默认端口 " + VtsIphoneReceiver.PhonePort + "）；"
                        + "我们是请求式收包，每秒向手机续一次 " + VtsIphoneReceiver.RequestSeconds.ToString("F0") + " 秒。"
                        + "手机那边没有『目标地址』可填：它把数据发回**请求的源 IP**、端口用我们请求里指定的那个。";
                default: return "";
            }
        }
    }
}
