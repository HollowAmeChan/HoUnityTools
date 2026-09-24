using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **调试宿主**：角色上不挂任何组件之后，**由它拿着全部调试状态、并且拥有每帧的 tick**。
    ///
    /// 【为什么需要它，而不是让窗口自己 tick】
    /// 窗口随时会被关掉、也不一定开着；而"影子求值 + 纯写入"这件事在播放中要连续发生。
    /// 所以 tick 挂在这里（<see cref="EditorApplication.update"/>），窗口只是它的一个视图 ——
    /// 关掉窗口，调试照常跑；这就是"面捕调试完全由菜单接管"。
    ///
    /// 【每帧做三段】
    ///   ① <see cref="HoFaceInputHub.HostUpdate"/>：收包、重连、清理死会话；
    ///   ② <see cref="HoFaceInputHub.Tick"/>：算参数 → 喂影子 Animator；
    ///   ③ <see cref="HoFaceInputHub.LateTick"/>：读回影子结果 → 纯写入角色。
    ///
    /// ⚠️ ②和③必须在**同一帧内**一前一后跑完，中间不能让 Unity 自己做动画求值 —— 否则就是在赌
    /// 执行顺序（原来是靠组件的 Update/LateUpdate 一对，现在没有组件了）。做法是会话在设完参数后
    /// 自己 <c>shadow.Update(0f)</c> 强制求值一次，于是"设参数 → 读结果"变成一次同步调用。
    /// </summary>
    [InitializeOnLoad]
    public static class HoFaceDebugHost
    {
        /// <summary>设置文件落点（工程相对路径；在 `Assets/` 下，跟着工程走）。</summary>
        public const string DefaultSettingsPath = HoFaceDebugSettings.DefaultAssetPath;

        private static HoFaceDebugSettings settings;

        /// <summary>当前设置。**永远非 null**（读不到就给一份空的）。</summary>
        public static HoFaceDebugSettings Settings
        {
            get
            {
                if (settings == null) settings = HoFaceDebugSettings.LoadOrCreate(DefaultSettingsPath);
                return settings;
            }
        }

        /// <summary>会话开着没有。</summary>
        public static bool Running { get { return HoFaceInputHub.Session(Settings) != null; } }

        /// <summary>会话最近的错误（面板显示）。</summary>
        public static string Error { get { return HoFaceInputHub.Error(Settings); } }

        /// <summary>接收端连上并收到包没有。</summary>
        public static bool Connected { get { return HoFaceInputHub.Connected; } }

        static HoFaceDebugHost()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>重新从磁盘读设置（面板上"重读"用它）。</summary>
        public static void Reload()
        {
            HoFaceInputHub.Stop(Settings);
            settings = HoFaceDebugSettings.LoadOrCreate(DefaultSettingsPath);
            Settings.ReloadProfile();
        }

        /// <summary>把当前设置写回磁盘。</summary>
        public static void Save()
        {
            Settings.Save();
        }

        /// <summary>开始跑（会话要求已经在播放模式）。</summary>
        public static void Start()
        {
            HoFaceInputHub.Start(Settings);
        }

        /// <summary>停止跑（会话关掉、角色回到它自己的状态）。</summary>
        public static void Stop()
        {
            HoFaceInputHub.Stop(Settings);
        }

        private static void OnUpdate()
        {
            // 输入侧（收包/重连/清理）由 HoFaceInputHub 自己的静态构造订阅驱动，这里不重复调 ——
            // 本宿主只管**会话那一侧**：进播放才推进，且必须是一帧内"设参数 → 读结果"跑完。
            if (!EditorApplication.isPlaying) return;
            if (settings == null) return;

            HoFaceInputHub.Tick(Settings);
            HoFaceInputHub.LateTick(Settings);
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                // 退出播放时把会话收干净：影子对象是 HideAndDontSave，不收会留在场景里。
                HoFaceInputHub.Stop(Settings);
                return;
            }

            if (change == PlayModeStateChange.EnteredPlayMode && Settings != null && Settings.startOnPlay)
            {
                try { Start(); }
                catch (Exception e) { Debug.LogWarning("[Ho 面捕] 自动开始失败：" + e.Message); }
            }
        }
    }
}
