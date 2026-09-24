namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 调试侧唯一的时间源：**单调、跨域重载连续、任何线程都能读**。
    ///
    /// 为什么单独一个文件：以前各处写的是 `IFacialMocapReceiver.Now` —— 一个被删掉的接收器
    /// 兼职当全项目的时钟。删那个类的时候才看出来这个依赖有多别扭（"现在几点"跟"哪个手机协议"
    /// 根本没关系）。留个名字明确的家，以后换实现也只动这一处。
    ///
    /// ⚠️ **不许换成 `EditorApplication.timeSinceStartup`**（2026-09-25 实测）：接收端在**后台线程**上收包，
    /// 也要拿这个时间给包打时间戳，而那个属性只能在主线程读 —— 一读就抛
    /// `get_timeSinceStartup can only be called from the main thread`，接收线程当场死掉。
    /// 症状是"连不上"（`packets=0`、面板显示已断流），但 socket 与端口其实都是好的，方向完全被带偏。
    ///
    /// 所以用 `Stopwatch`：同一次开机内单调递增（不受系统时钟调整影响）、任何线程可读、域重载也不影响它
    /// （它是直接问操作系统，不存托管状态）。原点在哪无所谓 —— 所有用法都是**算差值**。
    ///
    /// ⚠️ **必须只有一个实现**：会话判"这一包新不新鲜"算的是「会话的现在 − 包上的时间戳」。
    /// 两个时钟只要差一个恒定偏移，那个差值就永远越界，症状是"包到了、合并里也有，通道就是不写"
    /// （那一版是会话读 `timeSinceStartup`、接收端读 `Stopwatch`）。
    /// </summary>
    public static class HoFaceClock
    {
        /// <summary>单调秒数（double）。原点与纪元无关，只用来算差值。</summary>
        public static double Now =>
            System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    }
}
