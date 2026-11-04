using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HoFace.Automation;
using HoFace.Studio;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

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
            Check(window.CanConnect == false, "Preview mode still requires a work document");
            window.Mode = "RawForward";
            Check(window.CanConnect, "RawForward can connect VTS without a work document");
            window.Mode = "Preview";
            using (var rawOutputReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var rawSession = new AuthoringSession())
            {
                rawOutputReceiver.Client.ReceiveTimeout = 1000;
                rawSession.RawForwardPort = ((IPEndPoint)rawOutputReceiver.Client.LocalEndPoint).Port;
                rawSession.SetMode(AuthoringMode.RawForward);
                rawSession.Start(new HoFace.Transport.VtsIphoneOptions
                { PhoneAddress = "127.0.0.1", PhonePort = 9, LocalPort = FreeUdpPort() });
                SendVtsFrame(phone, rawSession.Receiver.LocalPort, .65f, .2f, .4f);
                rawSession.Poll();
                IPEndPoint packetSource = new IPEndPoint(IPAddress.Any, 0);
                using var forwarded = JsonDocument.Parse(rawOutputReceiver.Receive(ref packetSource));
                Check(forwarded.RootElement.GetProperty("values").GetProperty("JawOpen").GetSingle() == .65f,
                    "RawForward connects and forwards VTS with no document");
            }
            Check(window.Icon != null, "studio window loads the product icon resource");
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
            Check(window.RawMonitorRows.Count == 3 && window.ValueMonitorRows.Count == 2 && window.IrMonitorRows.Count == 3,
                "monitor preloads all three layers from the work document");
            using (var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var liveSession = window.SessionForChecks;
                liveSession.Start(new HoFace.Transport.VtsIphoneOptions
                { PhoneAddress = "127.0.0.1", PhonePort = 9, LocalPort = FreeUdpPort() });
                SendVtsFrame(phone, liveSession.Receiver.LocalPort, .7f, .4f, .6f);
                liveSession.Poll();
                Near(window.RawMonitorRows.First(x => x.Key == "JawOpen").Value, .7f, "monitor shows source wire input");
                Near(window.ValueMonitorRows.First(x => x.Key == "Mouth/Open").Value, .7f, "monitor shows value output");
                Near(window.ValueMonitorRows.First(x => x.Key == "Mouth/Form").Value, .5f, "monitor evaluates another value output");
                Near(window.IrMonitorRows.First(x => x.Key == "jawOpen").Value, .625f, "monitor shows final IR output");
                Check(window.CenterTab == 0, "curve view remains the initial work view");
                Call("selectValue", new { parameter = "Mouth/Open" });
                Check(window.CenterTab == 0, "selecting a value output switches to the curve view");
                Call("selectNode", new { id = "mouth" });
                Check(window.CenterTab == 1, "selecting an IR node switches to the blend view");

                liveSession.SetMonitorOverride(MonitorStage.Value, "Mouth/Open", 1);
                SendVtsFrame(phone, liveSession.Receiver.LocalPort, .3f, .2f, .4f);
                liveSession.Poll();
                var valueRow = window.ValueMonitorRows.First(x => x.Key == "Mouth/Open");
                Near(valueRow.LiveValue, .3f, "value monitor retains the live reading beside an override");
                Near(valueRow.Value, 1, "value-stage override reaches the value meter");
                float liveJawBeforeIrPin = window.IrMonitorRows.First(x => x.Key == "jawOpen").LiveValue;
                Check(Math.Abs(liveJawBeforeIrPin - 1) > .01f,
                    "IR live monitor evaluates the physical raw/value pipeline, not pinned value output");
                Near(window.IrMonitorRows.First(x => x.Key == "jawOpen").Value, 1,
                    "effective IR pipeline observes the upstream value override");
                liveSession.SetMonitorOverride(MonitorStage.Ir, "jawOpen", -1);
                SendVtsFrame(phone, liveSession.Receiver.LocalPort, .3f, .2f, .4f);
                liveSession.Poll();
                var weightRow = window.IrMonitorRows.First(x => x.Key == "jawOpen");
                Near(weightRow.LiveValue, liveJawBeforeIrPin, "IR override does not corrupt the independent live snapshot");
                Near(weightRow.Value, -1, "IR-stage override replaces the final output weight");
                Near(window.SessionForChecks.LiveWeights["jawOpen"], liveJawBeforeIrPin, "IR published override does not mutate evaluator live values");
                Near(window.SessionForChecks.LiveUnclampedWeights["jawOpen"], liveJawBeforeIrPin,
                    "IR published override does not mutate the independent live unclamped snapshot");
                liveSession.SetMonitorOverride(MonitorStage.Value, "Mouth/Open", null);
                liveSession.SetMonitorOverride(MonitorStage.Raw, "JawOpen", 1);
                SendVtsFrame(phone, liveSession.Receiver.LocalPort, .15f, .2f, .4f);
                liveSession.Poll();
                Near(window.RawMonitorRows.First(x => x.Key == "JawOpen").LiveValue, .15f, "raw meter keeps the physical reading");
                Near(window.RawMonitorRows.First(x => x.Key == "JawOpen").Value, 1, "raw-stage override reaches the next processing layer");
                Near(window.SessionForChecks.LiveValues["Mouth/Open"], .15f,
                    "live value snapshot evaluates unmodified physical input");
                Near(window.SessionForChecks.Values["Mouth/Open"], 1,
                    "raw-stage pin enters the effective value evaluator before expression evaluation");
                Call("view", new { centerTab = 2 });
                var monitor = Result("monitor");
                Check(monitor.GetProperty("raw").GetArrayLength() == 3 &&
                    monitor.GetProperty("values").GetArrayLength() == 2 &&
                    monitor.GetProperty("weights").GetArrayLength() == 3,
                    "monitor CLI reads raw wire values and all processed/output stages");
                Check(window.CenterTab == 2 && window.RawMonitorCount == 3,
                    "three-stage realtime monitor tab is available (" + window.CenterTab + ", " + window.RawMonitorCount + ")");
                var monitorWorkspace = (System.Windows.Controls.Grid)window.FindName("MonitorWorkspace");
                Check(monitorWorkspace.Visibility == System.Windows.Visibility.Visible,
                    "realtime tab expands into the full-width monitor workspace");
                var monitorTemplate = (DataTemplate)window.FindResource("MonitorParameterTemplate");
                var meterTemplate = (System.Windows.Controls.Border)monitorTemplate.LoadContent();
                Check(meterTemplate.Height == 26 && meterTemplate.Width == 184,
                    "monitor meters stay compact at 26 pixels");
                Check(monitorTemplate.Triggers.OfType<Trigger>().Any(trigger => trigger.SourceName == "MeterArea" &&
                    trigger.Setters.OfType<Setter>().Any(setter => setter.TargetName == "OverrideActions" &&
                        Equals(setter.Value, Visibility.Visible))),
                    "override actions appear only while hovering a meter");
                var rawItems = (ItemsControl)window.FindName("RawMonitorItems");
                Check(rawItems.ItemsPanel.LoadContent() is System.Windows.Controls.WrapPanel,
                    "each monitor page densely wraps meters across the available width");
                var rawPage = (System.Windows.Controls.Border)window.FindName("RawMonitorPage");
                var valuePage = (System.Windows.Controls.Border)window.FindName("ValueMonitorPage");
                var irPage = (System.Windows.Controls.Border)window.FindName("IrMonitorPage");
                var valueTab = (ToggleButton)window.FindName("ValueMonitorTab");
                valueTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, valueTab));
                window.UpdateLayout();
                Check(window.CenterTab == 2 && valuePage.Visibility == Visibility.Visible &&
                    rawPage.Visibility == Visibility.Collapsed && irPage.Visibility == Visibility.Collapsed,
                    "realtime processing is a separate page from input and output");
                var irTab = (ToggleButton)window.FindName("IrMonitorTab");
                irTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, irTab));
                window.UpdateLayout();
                Check(irPage.Visibility == Visibility.Visible && valuePage.Visibility == Visibility.Collapsed,
                    "realtime output opens as its own page");
                var rawTab = (ToggleButton)window.FindName("RawMonitorTab");
                rawTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, rawTab));
                window.UpdateLayout();
                Check(rawPage.Visibility == Visibility.Visible && irPage.Visibility == Visibility.Collapsed,
                    "realtime input opens as its own page");
                window.MonitorFilter = "Mouth/Open";
                Check(window.RawMonitorCount == 0 && window.ValueMonitorCount == 1 && window.IrMonitorCount == 0,
                    "one search filters all three preview panes and refreshes counts");
                Check(window.RawMonitorEmptyText == "没有匹配的参数" &&
                    window.ValueMonitorEmptyVisibility == Visibility.Collapsed,
                    "search results show a useful empty state only where needed");
                window.MonitorFilter = "";
                window.CenterTab = 0;
                window.UpdateLayout();
                Check(monitorWorkspace.Visibility == System.Windows.Visibility.Collapsed,
                    "monitor workspace closes when returning to an editor tab");
                window.CenterTab = 2;
                Call("clearMonitorOverrides");
                liveSession.Stop();
            }
            Check(HoFaceValueDocument.TryParse(File.ReadAllText(Path.Combine(samples, "Mouth.hoface.value.json")),
                out var downstreamProfile, out string parseError), parseError);
            downstreamProfile.outputs.Add(new HoFaceOutput
            {
                parameter = "Mouth/Derived",
                expression = "out(\"Mouth/Open\") * 2",
                defaultValue = 0
            });
            downstreamProfile.outputs.Add(new HoFaceOutput
            {
                parameter = "Mouth/Open",
                expression = "JawOpen",
                defaultValue = 0
            });
            var downstreamLiveSession = new HoFaceValueSession(downstreamProfile, 1);
            var downstreamEffectiveSession = new HoFaceValueSession(downstreamProfile, 1);
            downstreamEffectiveSession.Evaluator.SetOverrides(null, new System.Collections.Generic.Dictionary<string, float>
            {
                ["Mouth/Open"] = 1
            });
            var rawFrame = new System.Collections.Generic.Dictionary<string, float>
            {
                ["JawOpen"] = .3f, ["MouthSmileLeft"] = 0, ["MouthSmileRight"] = 0
            };
            downstreamLiveSession.Evaluate(new System.Collections.Generic.Dictionary<string, float>(rawFrame), 0);
            downstreamEffectiveSession.Evaluate(new System.Collections.Generic.Dictionary<string, float>(rawFrame), 0);
            Near(downstreamLiveSession.Values["Mouth/Derived"], .6f,
                "unoverridden downstream out reference retains the physical authoring value");
            Near(downstreamEffectiveSession.Values["Mouth/Derived"], 2,
                "downstream out reference observes the upstream value pin");
            Near(downstreamLiveSession.Values["Mouth/Open"], .3f,
                "unoverridden evaluator keeps the physical output value");
            Near(downstreamEffectiveSession.Values["Mouth/Open"], 1,
                "later duplicate exported rows preserve the pinned last-row output");
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
            Reject("setMonitorOverride", new { stage = "value", parameter = "Mouth/Open", mode = "0" },
                "monitor override protects unapplied JSON drafts");
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
    private static void Near(float actual, float expected, string label)
    {
        Check(Math.Abs(actual - expected) < .0001f, label + " (expected " + expected + ", got " + actual + ")");
    }
    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }
    private static void SendVtsFrame(UdpClient sender, int port, float jaw, float smileLeft, float smileRight)
    {
        string json = "{\"Timestamp\":1,\"FaceFound\":true,\"BlendShapes\":["
            + "{\"k\":\"JawOpen\",\"v\":" + jaw.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
            + "{\"k\":\"MouthSmileLeft\",\"v\":" + smileLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
            + "{\"k\":\"MouthSmileRight\",\"v\":" + smileRight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]}";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
    }
}
