using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HoFace.Studio
{
    internal sealed class HoFaceAutomationServer : IDisposable
    {
        public static string PipeName => HoFace.Automation.AutomationContract.PipeName;
        private readonly MainWindow window;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly JsonSerializerOptions json = new JsonSerializerOptions
        { IncludeFields = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private readonly ConcurrentDictionary<NamedPipeServerStream, byte> clients = new ConcurrentDictionary<NamedPipeServerStream, byte>();

        public HoFaceAutomationServer(MainWindow window) => this.window = window;
        public void Start() => _ = Task.Run(RunAsync);
        private async Task RunAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 8,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    clients.TryAdd(pipe, 0);
                    try
                    {
                        await pipe.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
                        _ = HandleClientAsync(pipe);
                    }
                    catch { clients.TryRemove(pipe, out _); pipe.Dispose(); throw; }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    Trace.TraceWarning("HoFaceStudio automation listener: " + error.Message);
                    try { await Task.Delay(100, cancellation.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        private async Task HandleClientAsync(NamedPipeServerStream pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                while (!cancellation.IsCancellationRequested && pipe.IsConnected)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    string request = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                    if (request == null) break;
                    string response = request.Length > 4 * 1024 * 1024
                        ? JsonSerializer.Serialize(new { ok = false, code = "REQUEST_TOO_LARGE", error = "请求上限为 4 MiB 字符。" }, json)
                        : await ExecuteAsync(request, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                    await writer.WriteLineAsync(response.AsMemory(), deadline.Token).ConfigureAwait(false);
                    _ = window.Dispatcher.BeginInvoke(new Action(window.AutomationResponseSent));
                }
            }
            catch (Exception error) when (error is IOException || error is OperationCanceledException || error is ObjectDisposedException)
            { }
            catch (Exception error) { Trace.TraceWarning("HoFaceStudio automation client: " + error.Message); }
            finally { clients.TryRemove(pipe, out _); pipe.Dispose(); }
        }
        private Task<string> ExecuteAsync(string request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
                object id = null;
                try
                {
                    using var document = JsonDocument.Parse(request);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("请求必须为 JSON 对象。");
                    if (document.RootElement.TryGetProperty("id", out var requestId)) id = requestId.Clone();
                    string command = document.RootElement.TryGetProperty("command", out var c) ? c.GetString() : null;
                    JsonElement payload = document.RootElement.TryGetProperty("payload", out var p) ? p : default;
                    object result = window.ExecuteAutomation(command, payload);
                    completion.TrySetResult(JsonSerializer.Serialize(new { ok = true, id, result }, json));
                }
                catch (Exception error)
                {
                    string code = error is JsonException ? "INVALID_JSON" : error is ArgumentException || error is FormatException
                        ? "INVALID_ARGUMENT" : error is IOException ? "FILE_CONFLICT" : error is InvalidOperationException
                        ? "INVALID_STATE" : "COMMAND_FAILED";
                    completion.TrySetResult(JsonSerializer.Serialize(new { ok = false, id, code, error = error.Message }, json));
                }
            }));
            return completion.Task;
        }
        public void Dispose()
        {
            cancellation.Cancel();
            foreach (var pipe in clients.Keys) pipe.Dispose();
        }
    }
}
