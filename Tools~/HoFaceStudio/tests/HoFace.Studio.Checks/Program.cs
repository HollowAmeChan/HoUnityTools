using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
            string legacyProfilePath = Path.Combine(root, "Editor", "FaceTracking", "Profiles", "ho-iPhoneVTS.hoface.json");
            if (File.Exists(legacyProfilePath))
            {
                Check(HoFaceValueDocument.TryParse(File.ReadAllText(legacyProfilePath), out var legacyProfile, out string legacyError),
                    "legacy HoFace profile parses for modifier migration: " + legacyError);
                int legacyModifierCount = legacyProfile.outputs.Sum(row => row.modifiers?.Count ?? 0);
                Check(legacyModifierCount > 0, "legacy HoFace profile contains ordered rule modifiers");
                string migratedValue = HoFaceValueDocument.Write(legacyProfile);
                Check(HoFaceValueDocument.TryParse(migratedValue, out var reloadedProfile, out string reloadError),
                    "migrated value document reloads with modifiers: " + reloadError);
                Check(reloadedProfile.outputs.Sum(row => row.modifiers?.Count ?? 0) == legacyModifierCount,
                    "legacy modifier chains survive new value-document serialization");
            }
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
            Check(window.NodeRows.Count == 5 && window.IrTreeRoots.Count == 1 &&
                window.IrTreeRoots[0].NodeId == "mouth" && window.IrTreeRoots[0].Children.Count == 4,
                "IR navigation is a root-reachable hierarchy with child poses nested under the Blend Tree");
            Check(window.IrTreeRoots[0].Name == "嘴部" && window.IrTreeRoots[0].Children[1].Name == "张嘴",
                "IR tree displays aliases instead of rule IDs");
            var irWithOrphan = JsonNode.Parse(Result("document").GetProperty("irText").GetString());
            irWithOrphan["nodes"].AsArray().Add(JsonNode.Parse(
                "{\"id\":\"unusedPose\",\"name\":\"未引用\",\"type\":\"pose\",\"weights\":[]}"));
            Call("text", new
            {
                value = Result("document").GetProperty("valueText").GetString(),
                ir = irWithOrphan.ToJsonString()
            });
            Check(window.NodeRows.Count == 5 && window.OrphanIrNodeCount == 1 &&
                window.IrTreeRoots[0].Children.Count == 4,
                "unreferenced IR nodes are hidden from the Blend Tree and identified separately");
            typeof(MainWindow).GetMethod("ClearOrphanRulesClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { null, new RoutedEventArgs() });
            Check(window.OrphanIrNodeCount == 0 &&
                Result("document").GetProperty("ir").GetProperty("nodes").GetArrayLength() == 5,
                "orphan cleanup removes rules outside root and preserves reachable tree");
            Call("undo");
            Check(window.OrphanIrNodeCount == 1, "orphan cleanup is undoable");
            Call("redo");
            Check(window.OrphanIrNodeCount == 0, "orphan cleanup can be redone");
            Call("selectNode", new { id = "rest" });
            Check(window.InspectorWeights.Count == 0 &&
                window.FindName("AddPoseWeightButton") is Button,
                "an empty Pose shows no synthetic choices, only a free-text target editor");
            window.PoseTargetDraft = "freeFormTarget";
            window.PoseWeightDraft = "0.35";
            ((Button)window.FindName("AddPoseWeightButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("AddPoseWeightButton")));
            Check(window.InspectorWeights.Count == 1 &&
                window.InspectorWeights[0].TargetId == "freeFormTarget",
                "Pose target IDs can be typed and saved without a target picker");
            Check(Result("document").GetProperty("ir").GetProperty("targets").EnumerateArray()
                .Any(x => x.GetProperty("id").GetString() == "freeFormTarget"),
                "typing a new Pose target creates its declared IR output");
            ((DataGrid)window.FindName("WeightsGrid")).SelectedIndex = 0;
            ((Button)window.FindName("DeletePoseWeightButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("DeletePoseWeightButton")));
            Check(window.InspectorWeights.Count == 0 &&
                !Result("document").GetProperty("ir").GetProperty("targets").EnumerateArray()
                    .Any(x => x.GetProperty("id").GetString() == "freeFormTarget"),
                "deleting the last stored Pose weight prunes its unused output target");
            Call("selectNode", new { id = "open" });
            Check(window.InspectorWeights.Count == 1 &&
                window.InspectorWeights[0].TargetId == "jawOpen",
                "Pose inspector only lists targets actually saved by that rule");
            typeof(MainWindow).GetMethod("EditPoseWeightTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { "open", "jawOpen", "Free/TextTarget" });
            Check(window.InspectorWeights.Single().TargetId == "Free/TextTarget",
                "editing a Pose target field commits the typed target ID");
            Call("undo");
            Check(window.InspectorWeights.Single().TargetId == "jawOpen",
                "typed Pose target edits participate in the document undo stack");
            var createdRule = Result("addIrRule", new { parentId = "mouth", type = "pose", name = "测试姿势" });
            string createdPoseId = createdRule.GetProperty("created").GetString();
            Check(window.IrTreeRoots[0].Children.Any(x => x.NodeId == createdPoseId && x.Name == "测试姿势"),
                "CLI rule creation adds an aliased Pose beneath the selected Blend Tree");
            Call("deleteIrRule", new { id = createdPoseId });
            Check(window.NodeRows.Count == 5, "deleting a rule removes its unreferenced node");
            var nestedRule = Result("addIrRule", new { parentId = "mouth", type = "blend2d", name = "嵌套混合" });
            string nestedId = nestedRule.GetProperty("created").GetString();
            Check(window.NodeRows.Count == 7 && window.NodeRows.Any(x => x.Node.id == nestedId),
                "nested Blend Tree creation adds a parent and its starter Pose");
            string nestedPoseId = Result("list").GetProperty("nodes").EnumerateArray()
                .First(x => x.GetProperty("id").GetString() == nestedId)
                .GetProperty("children")[0].GetProperty("node").GetString();
            Call("selectNode", new { id = nestedPoseId });
            Check(window.IrTreeRoots[0].Children.Any(x => x.NodeId == nestedId && x.IsExpanded &&
                x.Children.Any(child => child.NodeId == nestedPoseId && child.IsSelected)),
                "selecting a nested rule expands its parent path in the Blend Tree");
            Call("deleteIrRule", new { id = nestedId });
            Check(window.NodeRows.Count == 5, "deleting a nested Blend Tree prunes its orphaned starter Pose");
            Call("selectNode", new { id = "mouth" });
            ((Button)window.FindName("AddPoseRuleButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("AddPoseRuleButton")));
            Check(window.NodeRows.Count == 6 && window.SelectedNodeRow?.Node.type == "pose",
                "tree toolbar adds a Pose rule as a child of the selected Blend Tree");
            ((Button)window.FindName("DeleteSelectedRuleButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("DeleteSelectedRuleButton")));
            Check(window.NodeRows.Count == 5 && window.SelectedNodeRow?.Node.id == "mouth",
                "tree toolbar deletes the selected rule and returns selection to root");
            ((Button)window.FindName("AddPoseRuleButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("AddPoseRuleButton")));
            Check(window.ControlPoints.Count == 0 && window.SelectedNodeRow?.Node.type == "pose",
                "the Blend Tree toolbar adds and selects a Pose rule");
            Call("selectNode", new { id = "mouth" });
            Check(window.ControlPoints.Count == 5, "new Pose rule is stored as a new Blend Tree child");
            ((DataGrid)window.FindName("ControlPointGrid")).SelectedIndex = 4;
            ((Button)window.FindName("DeleteControlPointButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.FindName("DeleteControlPointButton")));
            Check(window.ControlPoints.Count == 4 && window.NodeRows.Count == 5,
                "deleting a blend point removes its unused child rule");
            Call("view", new { centerTab = 0 });
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
            Call("selectValue", new { parameter = "Mouth/Open" });
            var modifierChain = new object[]
            {
                new { kind = "smooth", seconds = .08, steps = Array.Empty<object>() },
                new
                {
                    kind = "steps", seconds = 0,
                    steps = new[] { new { trigger = .5, target = 1, hold = .2, threshold = .05, enterSeconds = .1, exitSeconds = .05 } }
                }
            };
            Call("setValueModifiers", new { parameter = "Mouth/Open", modifiers = modifierChain });
            var listedModifiers = Result("list").GetProperty("outputs").EnumerateArray()
                .First(x => x.GetProperty("parameter").GetString() == "Mouth/Open").GetProperty("modifiers");
            Check(listedModifiers.GetArrayLength() == 2 &&
                listedModifiers[0].GetProperty("kind").GetString() == "smooth" &&
                listedModifiers[1].GetProperty("steps")[0].GetProperty("enterSeconds").GetSingle() == .1f,
                "CLI reads and writes the ordered modifier chain and full hold-step fields");
            Check(window.ModifierRows.Count == 2 && window.FindName("ModifierList") is ListBox &&
                window.FindName("ModifierStepsGrid") is DataGrid,
                "selected value rules expose modifier-chain and hold-step editors");
            window.SelectedModifierIndex = 1;
            Check(window.ModifierKindDraft == "维持" && window.ModifierStepRows.Count == 1,
                "modifier selection loads its editable step details");
            Reject("setValueModifiers", new
            {
                parameter = "Mouth/Open",
                modifiers = new[] { new { kind = "steps", seconds = 0, steps = new[]
                {
                    new { trigger = .8, target = 1, hold = 0, threshold = 0, enterSeconds = 0, exitSeconds = 0 },
                    new { trigger = .5, target = 0, hold = 0, threshold = 0, enterSeconds = 0, exitSeconds = 0 }
                } } }
            }, "hold-step triggers must be strictly increasing");
            Call("setValueModifiers", new { parameter = "Mouth/Open", modifiers = Array.Empty<object>() });
            var addSmooth = (Button)window.FindName("AddSmoothModifierButton");
            addSmooth.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, addSmooth));
            Check(window.ModifierRows.Count == 1 && window.ModifierRows[0].Display.Contains("平滑"),
                "modifier add button creates an undoable smooth stage (rows=" + window.ModifierRows.Count +
                ", enabled=" + addSmooth.IsEnabled + ", selected=" + window.HasValueSelection +
                ", draft=" + window.HasPropertyDraft + ", error=" + window.Diagnostics + ")");
            var addDelay = (Button)window.FindName("AddDelayModifierButton");
            addDelay.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, addDelay));
            Check(window.ModifierRows.Count == 2 && window.ModifierRows[1].Display.Contains("延迟"),
                "modifier chain supports a second ordered delay stage");
            var moveModifierUp = (Button)window.FindName("MoveModifierUpButton");
            Check(window.CanMoveModifierUp, "modifier chain enables moving a later stage upward (selected=" +
                window.SelectedModifierIndex + ", count=" + window.ModifierRows.Count +
                ", has=" + window.HasSelectedModifier + ")");
            typeof(MainWindow).GetMethod("MoveModifierClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { moveModifierUp, new RoutedEventArgs() });
            Check(window.ModifierRows[0].Display.Contains("延迟") && window.SelectedModifierIndex == 0,
                "modifier move controls change evaluation order (first=" + window.ModifierRows[0].Display +
                ", selected=" + window.SelectedModifierIndex + ", enabled=" + moveModifierUp.IsEnabled +
                ", error=" + window.Diagnostics + ")");
            addSmooth.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, addSmooth));
            Check(window.ModifierRows.Count == 3 && window.ModifierRows[2].Display.Contains("平滑"),
                "modifier chain preserves repeated kinds as independent stages");
            var deleteModifier = (Button)window.FindName("DeleteModifierButton");
            typeof(MainWindow).GetMethod("DeleteModifierClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { deleteModifier, new RoutedEventArgs() });
            Check(window.ModifierRows.Count == 2, "modifier delete button removes only the selected stage");
            var addSteps = (Button)window.FindName("AddStepsModifierButton");
            addSteps.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, addSteps));
            Check(window.ModifierKindDraft == "维持" && window.ModifierStepRows.Count == 1,
                "modifier chain can create an editable hold-step stage");
            for (int i = 0; i < 3; i++)
                typeof(MainWindow).GetMethod("DeleteModifierClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { deleteModifier, new RoutedEventArgs() });
            Check(window.ModifierRows.Count == 0, "modifier chain removes stages independently");
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
            Check(window.ControlPoints[0].ChildId == "闭嘴",
                "Blend Tree child table uses Pose aliases while keeping stable node IDs internally");
            window.IrFilter = "张嘴";
            Check(window.VisibleIrNodeCount == 3 && window.IrTreeRoots[0].Children.Count == 2,
                "Blend Tree search retains matching descendants with their root path");
            window.IrFilter = "";
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
            var capabilities = Result("capabilities");
            Check(capabilities.GetProperty("protocolVersion").GetInt32() == 5 &&
                capabilities.GetProperty("commands").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "addIrRule") &&
                capabilities.GetProperty("commands").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "deleteIrChild"),
                "CLI advertises hierarchical IR rule editing in protocol v5");
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
