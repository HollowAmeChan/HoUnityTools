using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: HoFace.Console <command> [payload-json]");
    return 2;
}

string command = args[0];
string payload = args.Length > 1 ? args[1] : "{}";
using var pipe = new NamedPipeClientStream(".", "HoFaceStudio", PipeDirection.InOut, PipeOptions.Asynchronous);
try
{
    await pipe.ConnectAsync(1500);
}
catch (Exception error)
{
    Console.Error.WriteLine("HoFaceStudio is not running: " + error.Message);
    return 3;
}
using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
using (JsonDocument.Parse(payload)) { }
await writer.WriteLineAsync(JsonSerializer.Serialize(new { command, payload = JsonDocument.Parse(payload).RootElement }));
string response = await reader.ReadLineAsync() ?? "{\"ok\":false,\"error\":\"empty response\"}";
Console.WriteLine(response);
using var result = JsonDocument.Parse(response);
return result.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() ? 0 : 1;
