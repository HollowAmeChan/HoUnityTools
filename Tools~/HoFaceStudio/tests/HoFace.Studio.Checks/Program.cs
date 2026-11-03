using System;
using System.IO;
using System.Text.Json;
using HoFace.Studio;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            string samples = Path.Combine(root, "Tools~", "HoFaceStudio", "samples");
            var application = new App();
            application.InitializeComponent();
            var window = new MainWindow(false);
            object Call(string command, object payload = null)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload ?? new { }));
                return window.ExecuteAutomation(command, json.RootElement);
            }
            JsonElement State() => JsonSerializer.SerializeToElement(Call("status"));
            Call("open", new { valuePath = Path.Combine(samples, "Mouth.hoface.value.json"), irPath = Path.Combine(samples, "Mouth.hoface.ir.json") });
            Call("selectValue", new { parameter = "Mouth/Open" });
            Call("setValueCurveKey", new { parameter = "Mouth/Open", index = 1, value = .73 });
            Check(State().GetProperty("dirty").GetBoolean(), "curve edit is dirty");
            Call("selectValue", new { parameter = "Mouth/Form" });
            Call("undo");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form", "undo preserves the current value selection");
            Check(!State().GetProperty("dirty").GetBoolean(), "curve undo restores saved document");
            Call("redo");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form", "redo preserves selection");
            Call("selectNode", new { id = "mouth" });
            Call("setIrChild", new { nodeId = "mouth", childId = "open", property = "y", value = .82 });
            Call("undo");
            Check(State().GetProperty("selectedNode").GetString() == "mouth", "IR undo preserves stable node id");
            Check(State().GetProperty("selectedValue").ValueKind == JsonValueKind.Null, "IR selection does not activate a value row");
            window.IrFilter = "smile";
            Check(State().GetProperty("selectedNode").GetString() == "mouth", "filtering does not destroy the active selection");
            window.IrFilter = "";
            Check(window.SelectedNodeRow?.Node.id == "mouth", "clearing filter restores highlighted row");
            window.StructureTab = 0;
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form", "tabs remember independent selections");
            window.StructureTab = 1;
            Check(State().GetProperty("selectedNode").GetString() == "mouth", "IR tab restores its own selection");
            var model = new AuthoringDocument();
            model.Open(Path.Combine(samples, "Mouth.hoface.value.json"), Path.Combine(samples, "Mouth.hoface.ir.json"));
            var before = model.Snapshot();
            try { model.Edit((value, ir) => { value.outputs[0].expression = "mutated"; throw new ArgumentException("expected"); }); }
            catch (ArgumentException) { }
            Check(model.Snapshot().Value == before.Value, "failed edit rolls back");
            Check(!model.CanUndo, "failed edit adds no history");
            Console.WriteLine("Passed " + checks + " Studio interaction checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException(label);
        checks++;
    }
}
