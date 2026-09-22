using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 「给当前这个 Unity.exe 放行 UDP 49983」这件事的封装，供面板上的按钮使用。
    ///
    /// 两个必须说清楚的约束：
    ///
    /// 1. **改 Windows 防火墙需要管理员权限。** Unity 编辑器通常是普通权限进程，所以唯一正当的
    ///    做法是走 UAC 提权（runas）—— 也就是会弹一次系统确认框。这一步绕不过去，也不该绕过去：
    ///    按钮能做的是「把命令写对、把系统授权窗口叫出来」，不是「悄悄改掉你的安全设置」。
    ///
    /// 2. **防火墙里「阻止」规则优先于「允许」规则。** 实测踩到：某个 Unity.exe 在「公用」网络上
    ///    带着一条入站阻止规则，只加允许规则完全没用，包照样到不了 socket。所以这里先按程序路径
    ///    把该 exe 的入站阻止规则禁掉，再加允许规则 —— 两个动作缺一不可。
    ///
    /// 规则范围刻意收窄到「指定 exe + UDP 49983」，比直接开一个端口安全得多，也随时可撤销。
    /// </summary>
    public static class HoFaceFirewall
    {
        public const string RuleName = "HoUnityTools FaceTracking UDP 49983";
        public const int Port = IFacialMocapReceiver.Port;

        private static volatile bool exists;
        private static volatile bool elevating;
        private static volatile Process pending;
        private static volatile string status = "";
        private static int probing;

        /// <summary>只有 Windows 编辑器才有这套东西。</summary>
        public static bool Supported => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>我们的放行规则当前是否存在（后台探测，不会阻塞编辑器）。</summary>
        public static bool Exists => exists;

        /// <summary>正在等 UAC / 正在写规则。</summary>
        public static bool Busy => elevating || pending != null;

        public static string Status => status;

        // ── 探测 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 在后台线程问一次 netsh「我们那条规则在不在」。不碰任何 Unity API，所以可以离开主线程跑。
        /// </summary>
        public static void Refresh()
        {
            if (!Supported || Interlocked.Exchange(ref probing, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { exists = Probe(); }
                catch { }
                finally { Interlocked.Exchange(ref probing, 0); }
            });
        }

        /// <summary>UAC 弹窗与写规则是异步的，每帧问一次进程有没有结束，结束了才更新状态。</summary>
        public static void Poll()
        {
            Process process = pending;
            if (process == null) return;
            bool exited;
            try { exited = process.HasExited; }
            catch { exited = true; }   // 句柄没了就当作结束，别把面板永久卡在 Busy
            if (!exited) return;
            int code = -1;
            try { code = process.ExitCode; }
            catch { }
            process.Dispose();
            pending = null;
            status = code == 0
                ? "已放行：Unity 的入站 UDP " + Port + " 现在被允许。点一次「断开手机」再「连接手机」，把握手命令重发。"
                : "没有改成（退出码 " + code + "）。可能是在 UAC 里点了「否」，也可以照下面的命令手动执行。";
            Refresh();
        }

        private static bool Probe()
        {
            var info = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "advfirewall firewall show rule name=\"" + RuleName + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(info))
            {
                if (process == null) return false;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(4000);
                // 没有匹配规则时 netsh 也会输出一段说明文字，所以用「规则名有没有出现」判断，
                // 不去匹配会被本地化成中文的标签。
                return output != null && output.IndexOf(RuleName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // ── 授权 / 撤销 ───────────────────────────────────────────────────────

        public static void Grant()
        {
            if (!Supported || Busy) return;
            // 脚本要在主线程建好：里面要读 EditorApplication.applicationPath。
            Run(BuildGrantScript(), "已弹出系统授权窗口，请在 UAC 里点「是」…");
        }

        public static void Revoke()
        {
            if (!Supported || Busy) return;
            Run(BuildRevokeScript(), "已弹出系统授权窗口，请在 UAC 里点「是」…");
        }

        /// <summary>
        /// 提权必须放到后台线程：带 runas 的 Process.Start 会**阻塞到 UAC 弹窗被处理为止**
        /// （用户不理它的话能挂到两分钟），在主线程调就会把整个编辑器冻住。
        /// </summary>
        private static void Run(string script, string waiting)
        {
            elevating = true;
            status = waiting;
            string encoded = Encode(script);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Process process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        // 用 -EncodedCommand 而不是拼命令行：exe 路径里有空格和括号，
                        // 拼字符串在各种引号转义下迟早会出错，base64 让转义问题彻底消失。
                        Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    if (process == null) status = "无法启动提权进程。";
                    else pending = process;
                }
                catch (Win32Exception e)
                {
                    pending = null;
                    status = e.NativeErrorCode == 1223
                        ? "你在 UAC 里点了「否」，防火墙规则没有任何改动。"
                        : "无法提权：" + e.Message;
                }
                catch (Exception e)
                {
                    pending = null;
                    status = "无法提权：" + e.Message;
                }
                finally { elevating = false; }
            });
        }

        private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        private static string BuildGrantScript()
        {
            // 单引号字符串里再出现单引号需要翻倍；Windows 路径正常不会带单引号，这里只是防御。
            string program = EditorApplication.applicationPath.Replace("'", "''");
            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("try {");
            script.AppendLine("  $prog = '" + program + "'");
            script.AppendLine("  # 阻止规则优先于允许规则：先把这个 exe 的入站阻止规则全部关掉，否则加允许也没用。");
            script.AppendLine("  # 这里刻意不把 -Direction/-Action 和 -AssociatedNetFirewallApplicationFilter 混用：");
            script.AppendLine("  # 那组参数在 -EncodedCommand 下会绑不上（实测 ParameterBindingException），");
            script.AppendLine("  # 所以只按程序筛出规则，方向和动作在客户端判断。");
            script.AppendLine("  foreach ($f in @(Get-NetFirewallApplicationFilter -Program $prog -ErrorAction SilentlyContinue)) {");
            script.AppendLine("    foreach ($r in @(Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $f -ErrorAction SilentlyContinue)) {");
            script.AppendLine("      if ($r.Direction -eq 'Inbound' -and $r.Action -eq 'Block') { $r | Disable-NetFirewallRule -ErrorAction SilentlyContinue }");
            script.AppendLine("    }");
            script.AppendLine("  }");
            script.AppendLine("  Get-NetFirewallRule -DisplayName '" + RuleName + "' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue");
            script.AppendLine("  New-NetFirewallRule -DisplayName '" + RuleName + "' -Direction Inbound -Action Allow -Protocol UDP -LocalPort " + Port + " -Program $prog -Profile Any | Out-Null");
            script.AppendLine("  exit 0");
            script.AppendLine("} catch { exit 1 }");
            return script.ToString();
        }

        private static string BuildRevokeScript()
        {
            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("try {");
            script.AppendLine("  Get-NetFirewallRule -DisplayName '" + RuleName + "' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue");
            script.AppendLine("  exit 0");
            script.AppendLine("} catch { exit 1 }");
            return script.ToString();
        }
    }
}
