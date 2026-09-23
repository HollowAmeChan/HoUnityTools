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
    /// </summary>
    public enum HoFaceSourceKind
    {
        /// <summary>VTubeStudio iPhone App 的 "3rd Party PC Clients"：JSON 包，0..1，带 faceFound/热键。</summary>
        VtsIphone = 0,
        /// <summary>iFacialMocap / Facemotion3d：文本包（`名字-值|…`），0..100，没有 faceFound。</summary>
        IFacialMocap = 1
    }

    /// <summary>
    /// 一路输入源的配置条目。**顺序就是优先级**（环境里的顺序）：某个通道取"第一个还新鲜、且这一帧带来了它"的源。
    /// 端口故意各用各的（默认 49984 / 49983），所以"VTS 手机 + iFacialMocap 同时连着"是合法状态。
    /// </summary>
    [Serializable]
    public sealed class HoFaceSourceEntry
    {
        public HoFaceSourceKind kind;
        public bool enabled = true;
        [Tooltip("手机在局域网里的地址。不同源可以是不同手机。")]
        public string phoneIp = "192.168.1.100";
        [Tooltip("本机监听端口。固定端口的协议（iFacialMocap = 49983）会忽略这里。")]
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

        /// <summary>
        /// 默认环境：**VTS 手机在前、iFacialMocap 在后**。
        /// 为什么这样排：合并规则是"第一个新鲜的源胜"，所以只开 iFacialMocap 的人行为不变；
        /// 两条都活着时优先用 VTS 那条（它多给 faceFound / 时间戳，而且正是 VBridger 走的那条）。
        /// </summary>
        public static List<HoFaceSourceEntry> Default() => new List<HoFaceSourceEntry>
        {
            new HoFaceSourceEntry { kind = HoFaceSourceKind.VtsIphone, phoneIp = "192.168.1.100", localPort = 49984 },
            new HoFaceSourceEntry { kind = HoFaceSourceKind.IFacialMocap, phoneIp = "192.168.1.100", localPort = 49983 }
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
                case HoFaceSourceKind.IFacialMocap: return new IFacialMocapReceiver();
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string DisplayName(HoFaceSourceKind kind)
        {
            switch (kind)
            {
                case HoFaceSourceKind.VtsIphone: return "VTS 手机（3rd Party PC Clients）";
                case HoFaceSourceKind.IFacialMocap: return "iFacialMocap";
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
                        + "我们是请求式收包，每秒向手机续一次 " + VtsIphoneReceiver.RequestSeconds.ToString("F0") + " 秒。";
                case HoFaceSourceKind.IFacialMocap:
                    return "App「iFacialMocap」在手机上打开即可（PC 端由我们主动握手）；本机端口固定 " + IFacialMocapReceiver.Port + "。";
                default: return "";
            }
        }

        /// <summary>这个协议的本机端口能不能改（iFacialMocap 固定 49983）。</summary>
        public static bool FixedPort(HoFaceSourceKind kind) => kind == HoFaceSourceKind.IFacialMocap;
    }
}
