using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    /// 规则范围刻意收窄到「指定 exe + 我们实际在听的那几个 UDP 端口」，比直接开一个端口安全得多，也随时可撤销。
    ///
    /// 提权进程是独立进程，拿不到它的 stdout，所以脚本把每一步写进一个日志文件，由面板读回来。
    /// 「退出码 1 但不告诉你为什么」是没法排查的 —— 这一点是踩过之后补上的。
    /// </summary>
    public static class HoFaceFirewall
    {
        public const string RuleName = "HoUnityTools FaceTracking UDP";

        /// <summary>
        /// 要放行的本机端口列表（逗号分隔）：**按当前环境里启用的源算**。
        /// 现在只有 VTS 一种协议，端口就是那条源自己的 `localPort`；留成"算出来的"
        /// 是为了以后加设备时不用回来改这里。
        /// </summary>
        public static string Ports
        {
            get
            {
                var ports = new System.Collections.Generic.List<int>();
                foreach (var entry in HoFaceInputEnvironment.instance.sources)
                {
                    if (entry == null || !entry.enabled) continue;
                    int port = entry.localPort;
                    if (port >= 1024 && port <= 65535 && !ports.Contains(port)) ports.Add(port);
                }

                if (ports.Count == 0) ports.Add(49984);
                return string.Join(",", ports);
            }
        }

        private static volatile bool exists;
        private static volatile bool elevating;
        private static volatile Process pending;
        private static volatile string status = "";
        private static volatile string lastLog = "";
        private static int probing;

        /// <summary>提权脚本的执行日志。提权进程的 stdout 拿不到，只能靠它自己写文件。</summary>
        public static string LogPath => Path.Combine(Path.GetTempPath(), "HoUnityTools-firewall.log");

        /// <summary>只有 Windows 编辑器才有这套东西。</summary>
        public static bool Supported => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>我们的放行规则当前是否存在（后台探测，不会阻塞编辑器）。</summary>
        public static bool Exists => exists;

        /// <summary>正在等 UAC / 正在写规则。</summary>
        public static bool Busy => elevating || pending != null;

        public static string Status => status;

        /// <summary>上一次提权脚本的执行日志；失败时面板会把它摊开给用户看。</summary>
        public static string LastLog => lastLog;

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
            lastLog = ReadLog();
            status = code == 0
                ? "已放行：Unity 的入站 UDP " + Ports + " 现在被允许。点一次「断开全部」再「连接」，把握手/请求重发。"
                : "没有改成（退出码 " + code + "）。下面是提权脚本的执行日志，失败原因一般就在里面。";
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

        private static string ReadLog()
        {
            try
            {
                return File.Exists(LogPath) ? File.ReadAllText(LogPath).Trim() : "";
            }
            catch (Exception e) { return "（读不到日志 " + LogPath + "：" + e.Message + "）"; }
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
            lastLog = "";
            try { if (File.Exists(LogPath)) File.Delete(LogPath); }
            catch { }
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
                        // 隐藏窗口交给 PowerShell 自己的 -WindowStyle，而不是 ProcessStartInfo.WindowStyle：
                        // 后者要经过 ShellExecute 提权路径，少一层牵扯少一个变量。
                        Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
                        UseShellExecute = true,
                        Verb = "runas"
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

        /// <summary>单引号字符串里再出现单引号要翻倍。Windows 路径正常不带单引号，这里只是防御。</summary>
        private static string Quote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        /// <summary>两条脚本共用的开头：日志函数 + 目标程序。</summary>
        private static StringBuilder Header()
        {
            // Unity 在 Windows 上给的 applicationPath 可能是正斜杠形式；防火墙规则的 Program
            // 字段要的是反斜杠路径，这里先归一化，并把原始值也写进日志以便核对。
            string raw = EditorApplication.applicationPath ?? "";
            string program = raw.Replace('/', '\\');
            var script = new StringBuilder();
            // 故意不用 $ErrorActionPreference='Stop'：每一步单独 try/catch 并记日志，
            // 比"整段中断只留一个退出码"有用得多。
            script.AppendLine("$ErrorActionPreference = 'Continue'");
            script.AppendLine("$log = " + Quote(LogPath));
            script.AppendLine("function L([string]$m) { $m | Out-File -FilePath $log -Append -Encoding utf8 }");
            script.AppendLine("L '=== HoUnityTools 面捕：防火墙授权 ==='");
            script.AppendLine("$name = " + Quote(RuleName));
            script.AppendLine("$prog = " + Quote(program));
            script.AppendLine("L ('Unity 报告的路径: ' + " + Quote(raw) + ")");
            script.AppendLine("L ('归一化后        : ' + $prog)");
            script.AppendLine("L ('该 exe 是否存在 : ' + (Test-Path -LiteralPath $prog))");
            script.AppendLine("L ('提权身份        : ' + [Security.Principal.WindowsIdentity]::GetCurrent().Name)");
            script.AppendLine("L ('是否已提升      : ' + (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))");
            return script;
        }

        private static string BuildGrantScript()
        {
            var script = Header();
            // 1) 阻止规则优先于允许规则：先把这个 exe 的入站阻止规则关掉，否则加允许也没用。
            //    这里刻意不把 -Direction/-Action 和 -AssociatedNetFirewallApplicationFilter 混用：
            //    那组参数在 -EncodedCommand 下会绑不上（实测 ParameterBindingException），
            //    所以只按程序筛出规则，方向和动作在客户端判断。
            script.AppendLine("try {");
            script.AppendLine("  foreach ($f in @(Get-NetFirewallApplicationFilter -Program $prog -ErrorAction SilentlyContinue)) {");
            script.AppendLine("    foreach ($r in @(Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $f -ErrorAction SilentlyContinue)) {");
            script.AppendLine("      if ($r.Direction -eq 'Inbound' -and $r.Action -eq 'Block') {");
            script.AppendLine("        try { $r | Disable-NetFirewallRule -ErrorAction Stop; L ('已禁用阻止规则: ' + $r.DisplayName + ' [' + $r.Profile + ']') }");
            script.AppendLine("        catch { L ('禁用阻止规则失败: ' + $r.DisplayName + ' -> ' + $_.Exception.Message) }");
            script.AppendLine("      }");
            script.AppendLine("    }");
            script.AppendLine("  }");
            script.AppendLine("} catch { L ('扫描阻止规则失败: ' + $_.Exception.Message) }");
            // 2) 允许规则。先走 NetSecurity 模块，失败再退到 netsh（不依赖该模块）。
            script.AppendLine("try { Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop }");
            script.AppendLine("catch { L ('清理旧规则失败: ' + $_.Exception.Message) }");
            script.AppendLine("try {");
            script.AppendLine("  New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol UDP -LocalPort " + Ports + " -Program $prog -Profile Any -ErrorAction Stop | Out-Null");
            script.AppendLine("  L 'New-NetFirewallRule: OK'");
            script.AppendLine("} catch { L ('New-NetFirewallRule 失败: ' + $_.Exception.GetType().Name + ' / ' + $_.Exception.Message) }");
            script.AppendLine("if (@(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue).Count -eq 0) {");
            script.AppendLine("  L '改用 netsh 退路'");
            script.AppendLine("  $raw = & netsh advfirewall firewall add rule name=`\"$name`\" dir=in action=allow protocol=UDP localport=" + Ports + " program=`\"$prog`\" enable=yes profile=any 2>&1");
            script.AppendLine("  $code = $LASTEXITCODE");
            script.AppendLine("  L ('netsh 输出: ' + (($raw | Out-String).Trim()) + '  (exit=' + $code + ')')");
            script.AppendLine("}");
            // 不信任"命令没报错"，直接回读一条规则才算数。
            script.AppendLine("$made = @(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)");
            script.AppendLine("L ('建后回读: ' + $made.Count + ' 条规则')");
            script.AppendLine("$ok = $made.Count -gt 0");
            script.AppendLine("L $(if ($ok) { 'RESULT: OK' } else { 'RESULT: FAILED' })");
            script.AppendLine("if ($ok) { exit 0 } else { exit 1 }");
            return script.ToString();
        }

        private static string BuildRevokeScript()
        {
            var script = Header();
            script.AppendLine("try {");
            script.AppendLine("  $rules = @(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)");
            script.AppendLine("  foreach ($r in $rules) { $r | Remove-NetFirewallRule -ErrorAction Stop }");
            script.AppendLine("  L ('删除 ' + $rules.Count + ' 条规则')");
            script.AppendLine("} catch { L ('删除失败: ' + $_.Exception.Message) }");
            script.AppendLine("if (@(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue).Count -gt 0) {");
            script.AppendLine("  $raw = & netsh advfirewall firewall delete rule name=`\"$name`\" 2>&1");
            script.AppendLine("  L ('netsh 输出: ' + (($raw | Out-String).Trim()) + '  (exit=' + $LASTEXITCODE + ')')");
            script.AppendLine("}");
            script.AppendLine("$left = @(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue).Count");
            script.AppendLine("$ok = $left -eq 0");
            script.AppendLine("L ('剩余规则: ' + $left)");
            script.AppendLine("L $(if ($ok) { 'RESULT: OK' } else { 'RESULT: FAILED' })");
            script.AppendLine("if ($ok) { exit 0 } else { exit 1 }");
            return script.ToString();
        }
    }
}
