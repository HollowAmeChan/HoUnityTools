using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HoFace.Studio
{
    internal sealed class HoFaceAutomationServer : IDisposable
    {
        public const string PipeName = "HoFaceStudio";
        private readonly MainWindow window;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
        private Task loop;

        public HoFaceAutomationServer(MainWindow window) => this.window = window;
        public void Start() => loop = Task.Run(RunAsync);
        private async Task RunAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    string request;
                    while (!cancellation.IsCancellationRequested && (request = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        string response = await ExecuteAsync(request).ConfigureAwait(false);
                        await writer.WriteLineAsync(response).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(100, cancellation.Token).ConfigureAwait(false); }
            }
        }
        private Task<string> ExecuteAsync(string request)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    using var document = JsonDocument.Parse(request);
                    string command = document.RootElement.TryGetProperty("command", out var c) ? c.GetString() : null;
                    JsonElement payload = document.RootElement.TryGetProperty("payload", out var p) ? p : default;
                    object result = window.ExecuteAutomation(command, payload);
                    completion.SetResult(JsonSerializer.Serialize(new { ok = true, result }, json));
                }
                catch (Exception error)
                {
                    completion.SetResult(JsonSerializer.Serialize(new { ok = false, error = error.Message }, json));
                }
            }));
            return completion.Task;
        }
        public void Dispose()
        {
            cancellation.Cancel();
            try { loop?.Wait(500); } catch { }
            cancellation.Dispose();
        }
    }
}
