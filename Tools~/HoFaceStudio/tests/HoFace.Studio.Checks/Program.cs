using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
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
            string sampleValuePath = Path.GetFullPath(Path.Combine(samples, "Mouth.hoface.value.json"));
            string sampleIrPath = Path.GetFullPath(Path.Combine(samples, "Mouth.hoface.ir.json"));
            Check(((Button)window.FindName("BrowseValueFileButton")).IsEnabled, "value browse button works before opening a document");
            Check(((Button)window.FindName("BrowseIrFileButton")).IsEnabled, "IR browse button works before opening a document");
            window.ChooseDocumentFilePath(true, sampleValuePath);
            window.Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            Check(!window.HasDocument && window.HasPendingFilePaths, "selecting one file waits for its pair");
            Check(((TextBox)window.FindName("ValueFilePathBox")).Text == sampleValuePath, "value chooser fills the visible path immediately");
            window.ChooseDocumentFilePath(false, sampleIrPath);
            window.Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            Check(window.HasDocument && !window.HasPendingFilePaths, "selecting the second JSON validates and loads the pair");
            Check(((TextBox)window.FindName("IrFilePathBox")).Text == sampleIrPath, "IR chooser fills the visible path");
            var reversePicker = new MainWindow(false);
            reversePicker.ChooseDocumentFilePath(false, sampleIrPath);
            Check(!reversePicker.HasDocument && reversePicker.IrFilePath == sampleIrPath, "IR can be chosen first independently");
            reversePicker.ChooseDocumentFilePath(true, sampleValuePath);
            Check(reversePicker.HasDocument && reversePicker.ValuePath == sampleValuePath, "value selection completes an IR-first pair");
            object Call(string command, object payload = null)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload ?? new { }));
                return window.ExecuteAutomation(command, json.RootElement);
            }
            JsonElement State() => JsonSerializer.SerializeToElement(Call("status"));
            JsonElement Result(string command, object payload = null) => JsonSerializer.SerializeToElement(Call(command, payload));
            void Reject(string command, object payload, string label)
            {
                bool rejected = false;
                try { Call(command, payload); } catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is FormatException) { rejected = true; }
                Check(rejected, label);
            }
            Call("open", new { valuePath = Path.Combine(samples, "Mouth.hoface.value.json"), irPath = Path.Combine(samples, "Mouth.hoface.ir.json") });
            Check(((StackPanel)window.FindName("PropertiesPanel")).IsEnabled, "native property controls enable after opening");
            Call("selectValue", new { parameter = "Mouth/Open" });
            Call("setValueCurveKey", new { parameter = "Mouth/Open", index = 1, value = .73 });
            Check(State().GetProperty("dirty").GetBoolean(), "curve edit is dirty");
            window.ChooseDocumentFilePath(true, sampleValuePath);
            Check(State().GetProperty("dirty").GetBoolean() && State().GetProperty("canUndo").GetBoolean(), "choosing the same file does not reload or lose edit history");
            bool fileReplacementBlocked = false;
            try { window.ChooseDocumentFilePath(false, sampleValuePath); }
            catch (InvalidOperationException) { fileReplacementBlocked = true; }
            Check(fileReplacementBlocked && window.IrPath == sampleIrPath, "independent file replacement protects unsaved edits");
            Call("selectValue", new { parameter = "Mouth/Form" });
            Call("undo");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form", "undo preserves the current value selection");
            Check(!State().GetProperty("dirty").GetBoolean(), "curve undo restores saved document");
            Call("redo");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form", "redo preserves selection");
            Call("selectNode", new { id = "mouth" });
            Check(window.ControlPoints.Count == 4, "2D control points use one row per child");
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
            Call("selectValue", new { parameter = "Mouth/Open" });
            string stableText = Result("document").GetProperty("valueText").GetString();
            Reject("setValueCurveKey", new { parameter = "Mouth/Open", index = 1, time = 0 }, "duplicate curve coordinates fail");
            Check(Result("document").GetProperty("valueText").GetString() == stableText, "invalid curve edit is atomic");
            Reject("selectValue", new { parameter = "missing" }, "missing selection fails explicitly");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Open", "invalid selection preserves current row");
            Call("addValueCurveKey", new { parameter = "Mouth/Open", time = .5, value = .4 });
            Check(Result("selection").GetProperty("curve").GetArrayLength() == 3, "curve insertion is readable");
            Call("selectPoint", new { index = 1 });
            Check(State().GetProperty("selectedPoint").GetInt32() == 1, "point selection is inspectable");
            Call("deleteValueCurveKey", new { parameter = "Mouth/Open", index = 1 });
            Check(Result("selection").GetProperty("curve").GetArrayLength() == 2, "curve deletion is readable");
            Call("undo");
            Check(Result("selection").GetProperty("curve").GetArrayLength() == 3, "curve deletion is undoable");
            Call("undo");
            Check(Result("selection").GetProperty("curve").GetArrayLength() == 2, "curve insertion is undoable");
            Call("setValueExpression", new { parameter = "Mouth/Open", expression = "jawOpen * 0.5" });
            Check(Result("selection").GetProperty("expression").GetString() == "jawOpen * 0.5", "expression API and inspector share the model");
            Call("undo");
            window.DraftExpression = "jawOpen * 0.4";
            Check(window.HasPropertyDraft && window.HasUnsavedChanges, "property drafts participate in dirty state");
            Call("applyProperties");
            Check(!window.HasPropertyDraft && Result("selection").GetProperty("expression").GetString() == "jawOpen * 0.4", "property draft applies once");
            Call("undo");
            window.DraftExpression = "this is not valid ???";
            Reject("applyProperties", new { }, "invalid expression rejected");
            Check(window.DraftExpression == "this is not valid ???", "invalid expression remains editable");
            Check(window.HasPropertyDraft, "failed property apply keeps the draft marked pending");
            Call("revertProperties");
            Check(!window.HasPropertyDraft, "property draft can be reverted");
            string validText = Result("document").GetProperty("valueText").GetString();
            Call("draftText", new { value = "{" });
            Check(window.HasTextDraft && window.HasUnsavedChanges, "text draft participates in dirty state");
            Reject("applyText", new { }, "invalid JSON cannot apply");
            Reject("undo", new { }, "undo does not silently destroy JSON drafts");
            Reject("setValueCurveKey", new { parameter = "Mouth/Open", index = 1, value = .2 }, "graph mutations protect JSON drafts");
            Check(Result("document").GetProperty("valueText").GetString() == validText, "invalid JSON preserves applied model");
            Call("revertText");
            Check(!window.HasTextDraft, "text draft reverts");
            Call("view", new { centerTab = 0, structureWidth = 220, plotRatio = .4, width = 1024, height = 640 });
            var selectionBefore = Result("selection");
            Call("setValueCurveKey", new { parameter = "Mouth/Open", index = 1, value = .8 });
            Call("undo");
            Check(Result("selection").GetProperty("viewport").ToString() == selectionBefore.GetProperty("viewport").ToString(), "undo preserves graph viewport");
            Reject("view", new { plotRatio = double.MaxValue }, "view validation rejects unreasonable ratios");
            Reject("mode", new { value = "typo" }, "invalid output mode cannot silently switch");
            Call("selectNode", new { id = "mouth" });
            Call("cursor", new { x = 1, y = 1 });
            var diagnostics = Result("diagnostics");
            Check(diagnostics.GetProperty("manualPreview").GetBoolean(), "cursor computes manual preview");
            Check(diagnostics.GetProperty("weights").GetProperty("jawOpen").GetSingle() > .99f, "cursor drives actual IR weights");
            Check(diagnostics.GetProperty("weights").GetProperty("mouthSmileLeft").GetSingle() > .99f, "manual 2D preview uses both axes");
            Check(!State().GetProperty("textDraft").GetBoolean(), "manual preview does not create a draft");
            Call("clearPreview");
            Check(!Result("diagnostics").GetProperty("manualPreview").GetBoolean(), "temporary preview can be cleared");
            var evaluation = Result("evaluate", new { raw = new { JawOpen = 1f, MouthSmileLeft = 1f, MouthSmileRight = 1f } });
            Check(evaluation.GetProperty("values").GetProperty("Mouth/Open").GetSingle() > 0, "diagnostic raw evaluation shares value core");
            Check(!evaluation.GetProperty("appliedToLiveSession").GetBoolean(), "diagnostic evaluation declares its transient scope");
            using var rawSink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Call("mode", new { value = "RawForward", rawForwardPort = ((IPEndPoint)rawSink.Client.LocalEndPoint).Port });
            Reject("previewStart", new { }, "RawForward cannot enable Blender output");
            Call("mode", new { value = "Preview" });
            Call("setNode", new { id = "mouth", name = "嘴部测试" });
            Check(window.SelectionTitle == "嘴部测试", "node rename refreshes selection label");
            Call("undo");
            Check(window.SelectionTitle == "嘴部", "node rename undo refreshes inspector");
            Reject("setNode", new { id = "mouth", parameterX = "missing" }, "invalid parameter axis is rejected");
            Check(Result("list").GetProperty("nodes").EnumerateArray().First(x => x.GetProperty("id").GetString() == "mouth")
                .GetProperty("parameterX").GetString() == "Mouth/Form", "invalid axis edit rolls back");
            Check(((TextBox)window.FindName("ValueText")).VerticalContentAlignment == System.Windows.VerticalAlignment.Top, "JSON editors align at top");
            Call("view", new { showEditor = false });
            Check(!window.ShowEditor, "middle editor can collapse for focused property editing");
            Call("view", new { showEditor = true });
            Check(window.ShowEditor, "middle editor can restore");
            Call("selectValue", new { parameter = "Mouth/Form" });
            var reorder = Result("document");
            var valueNode = JsonNode.Parse(reorder.GetProperty("valueText").GetString());
            valueNode["outputs"] = new JsonArray(valueNode["outputs"].AsArray().Select(x => x.DeepClone()).Reverse().ToArray());
            Call("text", new { value = valueNode.ToJsonString(), ir = reorder.GetProperty("irText").GetString() });
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form" && State().GetProperty("selectedValueIndex").GetInt32() == 0, "text reorder preserves logical selected row");
            Call("undo");
            Check(State().GetProperty("selectedValue").GetString() == "Mouth/Form" && State().GetProperty("selectedValueIndex").GetInt32() == 1, "undo reorder preserves logical selected row");
            Check(Result("capabilities").GetProperty("commands").GetArrayLength() >= 40, "CLI discovers complete command contract");
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
