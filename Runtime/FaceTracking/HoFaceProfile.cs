namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **中间层配置文件**（我们自己的 JSON）：**输入行**（线名 → 规范名）+ **输出行**
    /// （规范名 → 输出名），两类行同一套形状 <c>名字 = 曲线(表达式) + 有序修饰符</c>。
    ///
    /// <para>
    /// 为什么是文本文件而不是 Unity 资产：它是**数据**，应该能 diff、能用任何编辑器打开、
    /// 能随手发给别人；同一份 spec 还要给 Warudo 侧的运行时复用。
    /// 形状照 VBridger 的 `.vbridger`（一个 store 里一列参数行），但**不抄它的加密** ——
    /// 我们要的是可读可 diff。
    /// </para>
    /// <para>
    /// ⚠️ **映射与量纲全在 <c>inputs</c> 里**：接收端只交"手机发来的线名 + 原值"，
    /// 所以 `eyeBlink_L * 0.01` 这种换算必须写成一行。这样换设备/换协议只改这份文件。
    /// </para>
    /// <para>
    /// ⚠️ **读写都走 <see cref="HoFaceProfileJson"/>，不用 <c>JsonUtility</c>** ——
    /// Unity 的 <c>JsonUtility</c> 在播放器里会静默丢掉 <c>inputs</c> / <c>outputs</c>
    /// 这两个 <c>List&lt;内部类&gt;</c> 字段（写只剩头部四个字段、读回来是空的），
    /// 而编辑器里是好的。原因与实测见 <see cref="HoFaceProfileJson"/> 的注释。
    /// 这个类现在只是"格式的名字 + 入口"。
    /// </para>
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
    /// 未知字段会被跳过（向前兼容）；`kind` 不认识时那一条修饰符被丢掉，并在面板上点名。
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
            return HoFaceProfileJson.TryParse(json, out middleware, out error);
        }

        /// <summary>写成配置文件（带 format/version 头，2 空格缩进）。</summary>
        public static string Write(HoFaceMiddleware middleware)
        {
            return HoFaceProfileJson.Write(middleware);
        }

        /// <summary>
        /// 内置默认表的文本（52 个 ARKit 出口 + 眼睑两根轴）。
        ///
        /// ⚠️ **"新建配置"不再用它**（2026-09-26 起新建出来是**空**的）——
        /// 从零建一份配置时替作者决定映射什么是越界，而且这张表的血统（`ARKit/` 前缀）
        /// 正是我们判定不该往发货配置里写的那个形状。现在它只剩两个用途：
        /// 导出/查看默认表、以及验证用例的夹具。
        /// </summary>
        public static string WriteDefaults()
        {
            return Write(HoFaceMiddlewareDefaults.Create());
        }
    }
}
