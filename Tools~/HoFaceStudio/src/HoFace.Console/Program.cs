using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HoFace.Automation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "help")
            {
                Print(new { ok = true, usage = new[] {
                    "HoFace.Console <command> [payload-json] [--timeout-ms 5000]",
                    "HoFace.Console <command> --file payload.json",
                    "HoFace.Console <command> --stdin",
                    "HoFace.Console --batch [--file requests.jsonl]"
                }, result = AutomationContract.Describe() });
                return 0;
            }
            if (args[0] == "--version") { Print(new { ok = true, protocolVersion = AutomationContract.Version }); return 0; }
            string command = args[0], file = null, inline = null;
            bool stdin = false, batch = command == "--batch";
            int timeout = 5000;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--file":
                        if (++i >= args.Length) throw new ArgumentException("--file 需要路径。");
                        file = args[i]; break;
                    case "--stdin": stdin = true; break;
                    case "--timeout-ms":
                        if (++i >= args.Length || !int.TryParse(args[i], out timeout) || timeout < 100 || timeout > 120000)
                            throw new ArgumentException("--timeout-ms 范围为 100–120000。");
                        break;
                    default:
                        if (inline != null || args[i].StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException("未知或重复参数：" + args[i]);
                        inline = args[i]; break;
                }
            }
            if ((file != null ? 1 : 0) + (stdin ? 1 : 0) + (inline != null ? 1 : 0) > 1)
                throw new ArgumentException("payload-json、--file、--stdin 只能选择一种。");
            if (batch)
            {
                if (inline != null) throw new ArgumentException("--batch 从标准输入或 --file 读取逐行请求。");
                using var input = file == null ? null : new StreamReader(file, Encoding.UTF8);
                TextReader source = input ?? Console.In;
                int exit = 0;
                string line;
                while ((line = await source.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var request = JsonDocument.Parse(line);
                        ValidateRequest(request.RootElement);
                        if (await SendAsync(line, timeout) != 0) exit = 1;
                    }
                    catch (JsonException error) { Error("INVALID_JSON", error.Message); exit = 2; }
                    catch (ArgumentException error) { Error("INVALID_ARGUMENT", error.Message); exit = 2; }
                }
                return exit;
            }
            if (!AutomationContract.Commands.Any(x => x.Name == command)) throw new ArgumentException("未知命令：" + command + "。使用 --help 查看命令。");
            string payloadText = file != null ? await File.ReadAllTextAsync(file, Encoding.UTF8) :
                stdin ? await Console.In.ReadToEndAsync() : inline ?? "{}";
            using var payload = JsonDocument.Parse(payloadText);
            if (payload.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("payload 必须为 JSON 对象。");
            return await SendAsync(JsonSerializer.Serialize(new { command, payload = payload.RootElement }, Json), timeout);
        }
        catch (JsonException error) { Error("INVALID_JSON", error.Message); return 2; }
        catch (ArgumentException error) { Error("INVALID_ARGUMENT", error.Message); return 2; }
        catch (OperationCanceledException) { Error("TIMEOUT", "HoFaceStudio 连接或响应超时。"); return 4; }
        catch (TimeoutException) { Error("TIMEOUT", "HoFaceStudio 连接或响应超时。"); return 4; }
        catch (IOException error) { Error("TRANSPORT_ERROR", error.Message); return 3; }
        catch (UnauthorizedAccessException error) { Error("ACCESS_DENIED", error.Message); return 3; }
    }
    private static void ValidateRequest(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("command", out var command) ||
            command.ValueKind != JsonValueKind.String || !AutomationContract.Commands.Any(x => x.Name == command.GetString()))
            throw new ArgumentException("请求需要已知的 command 字符串。");
        if (request.TryGetProperty("payload", out var payload) && payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("payload 必须为 JSON 对象。");
    }
    private static async Task<int> SendAsync(string request, int timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(".", AutomationContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellation.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(request.AsMemory(), cancellation.Token);
        string response = await reader.ReadLineAsync(cancellation.Token);
        if (string.IsNullOrWhiteSpace(response)) throw new IOException("HoFaceStudio 返回了空响应。");
        using var result = JsonDocument.Parse(response);
        Console.WriteLine(response);
        return result.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True ? 0 : 1;
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
    private static void Error(string code, string message) => Print(new { ok = false, code, error = message });
}
