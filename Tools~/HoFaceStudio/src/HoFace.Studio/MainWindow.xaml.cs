using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using HoFace.Transport;

namespace HoFace.Studio
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly AuthoringDocument document = new AuthoringDocument();
        private readonly AuthoringSession session = new AuthoringSession();
        private readonly DispatcherTimer pollTimer;
        private readonly HoFaceAutomationServer automation;
        private AuthoringSnapshot editBefore;
        private string mode = "Preview";
        private string valueFilter = "";
        private string irFilter = "";
        private int centerTab;
        private string phoneIp = "127.0.0.1";
        private string phonePort = "21412";
        private string localPort = "21413";
        private string liveStatus = "VTS 未连接";
        private HoFaceOutput selectedValueRow;
        private HoFaceIrNode selectedNode;
        private bool refreshingRows, loadingText;
        private int structureTab, selectedOutputIndex = -1;
        private string selectedOutputParameter;
        private int selectedOutputOccurrence;
        private string selectedNodeId;
        private string textValueBaseline = "", textIrBaseline = "";
        public int StructureTab
        {
            get => structureTab;
            set
            {
                if (structureTab == value) return;
                try { CommitPropertyDraft(); } catch (Exception error) { ShowError(error.Message); OnPropertyChanged(); return; }
                structureTab = value;
                ResolveSelection();
                RefreshPlot(true);
                OnPropertyChanged();
            }
        }
        public bool HasTextDraft => !loadingText &&
            (ValueText.Text != textValueBaseline || IrText.Text != textIrBaseline);
        public bool HasUnsavedChanges => HasTextDraft || HasPropertyDraft || document.IsDirty;
        public ObservableCollection<RuleRow> ValueRows { get; } = new ObservableCollection<RuleRow>();
        public ObservableCollection<NodeRow> NodeRows { get; } = new ObservableCollection<NodeRow>();
        public ObservableCollection<WeightRow> InspectorWeights { get; } = new ObservableCollection<WeightRow>();
        public event PropertyChangedEventHandler PropertyChanged;
        public string ValuePath => document.ValuePath;
        public string IrPath => document.IrPath;
        public string Mode
        {
            get => mode;
            set
            {
                if (value != "Preview" && value != "RawForward") throw new ArgumentException("mode 必须为 Preview 或 RawForward。");
                mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status));
            }
        }
        public string ValueFilter { get => valueFilter; set { valueFilter = value ?? ""; RefreshRows(); OnPropertyChanged(); } }
        public string IrFilter { get => irFilter; set { irFilter = value ?? ""; RefreshRows(); OnPropertyChanged(); } }
        public int CenterTab
        {
            get => centerTab;
            set
            {
                if (centerTab == value) return;
                if (value == 1)
                {
                    try { CommitPropertyDraft(); }
                    catch (Exception error) { ShowError(error.Message); OnPropertyChanged(); return; }
                }
                if (centerTab == 1 && HasTextDraft)
                {
                    try { SyncText(); ReloadEditors(); }
                    catch (Exception error)
                    {
                        ShowError(error.Message);
                        Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(CenterTab))));
                        return;
                    }
                }
                centerTab = value; OnPropertyChanged();
            }
        }
        public RuleRow SelectedValueRow
        {
            get => ValueRows.FirstOrDefault(x => x.Row == selectedValueRow);
            set
            {
                if (refreshingRows || value == null) return;
                try { CommitPropertyDraft(); } catch (Exception error) { ShowError(error.Message); OnPropertyChanged(); return; }
                selectedOutputIndex = value.Index;
                RememberValueSelection();
                structureTab = 0;
                ResolveSelection(); RefreshPlot(true);
                OnPropertyChanged(); OnPropertyChanged(nameof(StructureTab));
            }
        }
        public NodeRow SelectedNodeRow
        {
            get => NodeRows.FirstOrDefault(x => x.Node == selectedNode);
            set
            {
                if (refreshingRows || value == null) return;
                try { CommitPropertyDraft(); } catch (Exception error) { ShowError(error.Message); OnPropertyChanged(); return; }
                selectedNodeId = value.Node.id;
                structureTab = 1;
                ResolveSelection(); RefreshPlot(true);
                OnPropertyChanged(); OnPropertyChanged(nameof(StructureTab));
            }
        }
        public string HeaderStatus => HasPendingFilePaths ? "等待配对文件" : document.Value == null ? "未打开作品" : (HasTextDraft ? "文本待应用" : HasPropertyDraft ? "属性待应用" : document.IsDirty ? "● 未保存" : "已保存");
        public string Status => document.Value == null ? "先打开一对 .hoface.value.json + .hoface.ir.json" : "就绪 · " + Mode;
        public string InputStatus => session.ReplayPlaying ? "回放 · 帧 " + session.ReplayIndex : "输入：" + session.TransportStatus;
        public string PhoneIp { get => phoneIp; set { phoneIp = value; OnPropertyChanged(); } }
        public string PhonePort { get => phonePort; set { phonePort = value; OnPropertyChanged(); } }
        public string LocalPort { get => localPort; set { localPort = value; OnPropertyChanged(); } }
        public string LiveStatus => liveStatus;
        public string TargetStatus => OutputStatus;
        public string InspectorTitle => selectedValueRow != null ? "value 行" : selectedNode != null ? "IR 节点" : "检查器";
        public string InspectorSummary => selectedValueRow != null ? selectedValueRow.parameter : selectedNode != null ? selectedNode.id : "选择左侧规则或 IR 节点";
        public bool WeightsReadOnly => selectedNode == null;
        public string InspectorName => selectedValueRow?.parameter ?? selectedNode?.name ?? "";
        public string InspectorExpression => selectedValueRow?.expression ?? selectedNode?.parameterX ?? "";
        public string Diagnostics { get; private set; } = "";
        public string PlotHint => selectedValueRow != null ? "曲线点/切线可拖动；双击添加点；滚轮缩放；右键平移。"
            : selectedNode == null ? "选择一个 IR 节点查看参数。" : selectedNode.type == "pose"
            ? "修改右侧形态键权重。" : selectedNode.type == "direct"
            ? "修改右侧子项权重；组合按 IR 归一化设置求值。" : "拖动或编辑右侧控制点坐标；预览游标是临时输入。";
        public MainWindow() : this(true) { }
        public MainWindow(bool startAutomation)
        {
            InitializeComponent();
            persistWorkspace = startAutomation && Environment.GetEnvironmentVariable("HOFACE_STUDIO_NO_WORKSPACE") != "1";
            Loaded += (_, _) => LoadWorkspace();
            SourceInitialized += (_, _) => ApplyDarkWindowChrome();
            DataContext = this;
            if (startAutomation)
            {
                automation = new HoFaceAutomationServer(this);
                automation.Start();
            }
            ValueText.TextChanged += (_, _) => TextDraftChanged();
            IrText.TextChanged += (_, _) => TextDraftChanged();
            session.StatusChanged += status => Dispatcher.Invoke(() =>
            {
                liveStatus = status;
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            session.RawForward.StatusChanged += status => Dispatcher.Invoke(() =>
            {
                liveStatus = status;
                OnPropertyChanged(nameof(LiveStatus));
            });
            session.PreviewUpdated += (values, weights, tracked) => Dispatcher.Invoke(() =>
            {
                if (parameterOverrides != null) ApplyParameterOverrides(values);
                string inputLabel = session.ReplayPlaying ? "回放 " + session.ReplayIndex + " 帧" : "VTS " + session.Frames + " 帧";
                liveStatus = tracked ? inputLabel + " · value " + values.Count + " · 权重 " + weights.Count : inputLabel + " · 未追踪";
                if (HasManualPreview) liveStatus = "手动覆盖 · " + inputLabel;
                if (session.Preview.Running)
                    session.Preview.Send(manualValues ?? values, manualWeights ?? weights, HasManualPreview || tracked, session.Frames / 60.0);
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            session.RawFrameForward += frame => Dispatcher.Invoke(() =>
            {
                liveStatus = "VTS raw " + session.Frames + " 帧 · " + session.RawForward.Status;
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            pollTimer.Tick += (s, e) =>
            {
                session.Poll();
                if ((DateTime.UtcNow - lastTransportRefresh).TotalMilliseconds >= 250)
                {
                    lastTransportRefresh = DateTime.UtcNow; RefreshTransport();
                }
            };
            if (startAutomation) pollTimer.Start();
            Closing += OnClosing;
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (HasUnsavedChanges && !closingWithoutPrompt)
            {
                var answer = MessageBox.Show("当前有未保存的 value / IR 编辑，是否保存？", "HoFaceStudio", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Cancel) e.Cancel = true;
                else if (answer == MessageBoxResult.Yes) try { SaveDocument(); } catch (Exception error) { MessageBox.Show(error.Message); e.Cancel = true; }
            }
            if (!e.Cancel)
            {
                SaveWorkspace();
                automation?.Dispose();
                pollTimer.Stop();
                session.Dispose();
            }
        }
        private void OpenClicked(object sender, RoutedEventArgs e) => OpenDocument();
        private void SaveClicked(object sender, RoutedEventArgs e) => TrySave();
        private void UndoClicked(object sender, RoutedEventArgs e) => UndoDocument();
        private void RedoClicked(object sender, RoutedEventArgs e) => RedoDocument();
        private void ModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (HasManualPreview) ClearManualPreview();
            session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
            OnPropertyChanged(nameof(Status));
            RefreshTransport();
        }
        private void ConnectClicked(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PhonePort, out int phone) || !int.TryParse(LocalPort, out int local) ||
                phone < 1 || phone > 65535 || local < 1 || local > 65535)
            {
                ShowError("端口必须是 1–65535 的整数。"); return;
            }
            try
            {
                session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
                session.PauseReplay();
                session.Start(new VtsIphoneOptions { PhoneAddress = PhoneIp, PhonePort = phone, LocalPort = local });
                OnPropertyChanged(nameof(InputStatus)); OnPropertyChanged(nameof(LiveStatus));
            }
            catch (Exception error) { MessageBox.Show(error.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void StopClicked(object sender, RoutedEventArgs e)
        {
            session.Stop(); OnPropertyChanged(nameof(InputStatus)); OnPropertyChanged(nameof(LiveStatus));
        }
        private void PreviewClicked(object sender, RoutedEventArgs e)
        {
            try { session.Preview.Start(); OnPropertyChanged(nameof(LiveStatus)); }
            catch (Exception error) { MessageBox.Show(error.Message, "预览失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void StopPreviewClicked(object sender, RoutedEventArgs e)
        {
            session.Preview.Stop(); OnPropertyChanged(nameof(LiveStatus));
        }
        private void OpenExecuted(object sender, ExecutedRoutedEventArgs e) => OpenDocument();
        private void SaveExecuted(object sender, ExecutedRoutedEventArgs e) => TrySave();
        private void SaveCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.Value != null && HasUnsavedChanges;
        private void UndoExecuted(object sender, ExecutedRoutedEventArgs e) => UndoDocument();
        private void UndoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanUndo;
        private void RedoExecuted(object sender, ExecutedRoutedEventArgs e) => RedoDocument();
        private void RedoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanRedo;
        private void WeightsCellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != System.Windows.Controls.DataGridEditAction.Commit || e.Row.Item is not WeightRow row) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureNoDraft();
                    float value = ParseNumber(row.Value);
                    if (row.Kind == "pose") EditIrPoseWeight(row.NodeId, row.TargetId, value);
                    else EditIrChild(row.NodeId, row.ChildId, -1, row.Kind, value);
                }
                catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
            }));
        }
        private void OpenDocument()
        {
            var value = new Microsoft.Win32.OpenFileDialog { Filter = "HoFace value (*.hoface.value.json;*.hoface.json)|*.hoface.value.json;*.hoface.json|JSON|*.json" };
            if (value.ShowDialog() != true) return;
            const string suffix = ".hoface.value.json";
            string irPath = value.FileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.FileName.Substring(0, value.FileName.Length - suffix.Length) + ".hoface.ir.json" : null;
            if (irPath == null || !File.Exists(irPath))
            {
                var ir = new Microsoft.Win32.OpenFileDialog { Filter = "HoFace IR (*.hoface.ir.json)|*.hoface.ir.json|JSON|*.json",
                    InitialDirectory = Path.GetDirectoryName(value.FileName) };
                if (ir.ShowDialog(this) != true) return;
                irPath = ir.FileName;
            }
            try
            {
                if (!ConfirmReplaceDocument()) return;
                OpenPaths(value.FileName, irPath);
            }
            catch (Exception error) { MessageBox.Show(error.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void TrySave() { try { SaveDocument(); } catch (Exception error) { MessageBox.Show(error.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void SaveDocument() { CommitPropertyDraft(); SyncText(); document.Save(); ReloadEditors(); }
        private void SyncText()
        {
            if (document.Value == null) return;
            if (HasTextDraft)
                document.ApplyText(ValueText.Text, IrText.Text);
        }
        private void ReloadEditors()
        {
            loadingText = true;
            textValueBaseline = document.Value == null ? "" : HoFaceValueDocument.Write(document.Value);
            textIrBaseline = document.Ir == null ? "" : document.Ir.Write();
            ValueText.Text = textValueBaseline;
            IrText.Text = textIrBaseline;
            loadingText = false;
            ResolveSelection();
            RefreshRows(); RefreshPlot();
            if (document.Value != null) session.Configure(document);
            Diagnostics = "";
            OnPropertyChanged(nameof(Diagnostics)); OnPropertyChanged(nameof(HasTextDraft));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            Title = (document.IsDirty ? "● " : "") + (document.ValuePath == null ? "" : Path.GetFileName(document.ValuePath).Replace(".hoface.value.json", "") + " · ") + "HoFaceStudio";
            OnPropertyChanged(nameof(ValuePath)); OnPropertyChanged(nameof(IrPath)); OnPropertyChanged(nameof(HeaderStatus));
            NotifyFilePaths();
            OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(InspectorTitle)); OnPropertyChanged(nameof(InspectorSummary));
            RefreshTransport();
            if (HasManualPreview && Mode == "Preview")
            {
                if (selectedValueRow != null && parameterOverrides.ContainsKey(selectedValueRow.parameter))
                    parameterOverrides[selectedValueRow.parameter] = HoFaceCurve.Transfer(selectedValueRow.curve, (float)Plot.CursorX);
                ApplyParameterOverrides(session.Values);
                if (session.Preview.Running) session.Preview.Send(manualValues, manualWeights, true, 0);
                liveStatus = "手动预览 · 临时参数覆盖";
                OnPropertyChanged(nameof(LiveStatus));
            }
        }
        private void RefreshRows()
        {
            refreshingRows = true;
            try
            {
                var rules = document.Value?.outputs.Select((row, index) => new RuleRow(row, index))
                    .Where(x => string.IsNullOrWhiteSpace(ValueFilter) || x.Display.IndexOf(ValueFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray() ?? Array.Empty<RuleRow>();
                var nodes = document.Ir?.nodes.Where(x => string.IsNullOrWhiteSpace(IrFilter)
                    || (x.id + " " + x.name + " " + x.type + " " + x.parameterX + " " + x.parameterY).IndexOf(IrFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(x => new NodeRow(x)).ToArray() ?? Array.Empty<NodeRow>();
                Reconcile(ValueRows, rules, x => x.Index.ToString(), (old, next) => old.Update(next.Row));
                Reconcile(NodeRows, nodes, x => x.Node.id, (old, next) => old.Update(next.Node));
                OnPropertyChanged(nameof(SelectedValueRow));
                OnPropertyChanged(nameof(SelectedNodeRow));
                OnPropertyChanged(nameof(ValueCount)); OnPropertyChanged(nameof(NodeCount));
            }
            finally { refreshingRows = false; }
        }
        private void ResolveSelection()
        {
            if (document.Value != null && selectedOutputParameter != null)
            {
                var matches = document.Value.outputs.Select((row, index) => new { row, index })
                    .Where(x => x.row.parameter == selectedOutputParameter).ToArray();
                if (selectedOutputOccurrence < matches.Length) selectedOutputIndex = matches[selectedOutputOccurrence].index;
                else if (selectedOutputIndex >= 0 && selectedOutputIndex < document.Value.outputs.Count) RememberValueSelection();
            }
            selectedValueRow = structureTab == 0 && selectedOutputIndex >= 0 && document.Value != null &&
                selectedOutputIndex < document.Value.outputs.Count ? document.Value.outputs[selectedOutputIndex] : null;
            selectedNode = structureTab == 1 ? document.Ir?.nodes.FirstOrDefault(x => x.id == selectedNodeId) : null;
        }
        private void RememberValueSelection()
        {
            if (document.Value == null || selectedOutputIndex < 0 || selectedOutputIndex >= document.Value.outputs.Count)
            { selectedOutputParameter = null; selectedOutputOccurrence = 0; return; }
            selectedOutputParameter = document.Value.outputs[selectedOutputIndex].parameter;
            selectedOutputOccurrence = document.Value.outputs.Take(selectedOutputIndex).Count(x => x.parameter == selectedOutputParameter);
        }
        private static void Reconcile<T>(ObservableCollection<T> target, T[] items,
            Func<T, string> key, Action<T, T> update)
        {
            for (int i = 0; i < items.Length; i++)
            {
                int found = -1;
                for (int j = i; j < target.Count; j++) if (key(target[j]) == key(items[i])) { found = j; break; }
                if (found < 0) target.Insert(i, items[i]);
                else { if (found != i) target.Move(found, i); update(target[i], items[i]); }
            }
            while (target.Count > items.Length) target.RemoveAt(target.Count - 1);
        }
        private void RefreshPlot(bool newSelection = false)
        {
            Plot.Rule = selectedValueRow;
            Plot.Node = selectedNode;
            if (newSelection) { Plot.ClearSelection(); Plot.Fit(); }
            Plot.ValidateSelection(); Plot.InvalidateVisual();
            InspectorWeights.Clear();
            if (selectedNode != null)
            {
                if (selectedNode.type == "pose")
                    foreach (var target in document.Ir.targets)
                    {
                        var weight = selectedNode.weights.FirstOrDefault(x => x.target == target.id);
                        InspectorWeights.Add(new WeightRow(target.id,
                            (weight?.value ?? 0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "pose", selectedNode.id, target.id));
                    }
                else if (selectedNode.type == "direct")
                    foreach (var child in selectedNode.children)
                        InspectorWeights.Add(new WeightRow(child.node,
                            child.weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "weight", selectedNode.id, childId: child.node));
                else if (selectedNode.type == "blend1d")
                    foreach (var child in selectedNode.children)
                        InspectorWeights.Add(new WeightRow(child.node,
                            child.threshold.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "threshold", selectedNode.id, childId: child.node));
                else if (selectedNode.type == "blend2d")
                    foreach (var child in selectedNode.children)
                    {
                        InspectorWeights.Add(new WeightRow(child.node + " · X",
                            child.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "x", selectedNode.id, childId: child.node));
                        InspectorWeights.Add(new WeightRow(child.node + " · Y",
                            child.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "y", selectedNode.id, childId: child.node));
                    }
            }
            OnPropertyChanged(nameof(InspectorName)); OnPropertyChanged(nameof(InspectorExpression)); OnPropertyChanged(nameof(InspectorSummary)); OnPropertyChanged(nameof(InspectorTitle)); OnPropertyChanged(nameof(PlotHint));
            OnPropertyChanged(nameof(WeightsReadOnly));
            RefreshInspector();
        }
        private void PlotEditBeginning() { editBefore ??= document.Snapshot(); }
        private void PlotEditChanged() { Plot.InvalidateVisual(); OnPropertyChanged(nameof(HeaderStatus)); }
        private void PlotEditCommitted()
        {
            if (editBefore == null) return;
            var before = editBefore; editBefore = null;
            try { document.Remember(before); ReloadEditors(); }
            catch (Exception error) { ReloadEditors(); ShowError(error.Message); }
        }
        private void PlotEditCanceled()
        {
            if (editBefore == null) return;
            document.CancelEdit(editBefore); editBefore = null; ReloadEditors();
        }
        private void PlotSelectionChanged()
        {
            if (Plot.SelectedIndex >= 0 && Plot.SelectedIndex < CurveRows.Count)
                CurveGrid.SelectedItem = CurveRows[Plot.SelectedIndex];
            if (Plot.SelectedIndex >= 0 && Plot.SelectedIndex < ControlPoints.Count)
                ControlPointGrid.SelectedItem = ControlPoints[Plot.SelectedIndex];
            OnPropertyChanged(nameof(InspectorName)); OnPropertyChanged(nameof(InspectorExpression));
        }
        private void PlotPreviewChanged(double x, double y)
        {
            try { PreviewAtCursor(x, y); }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public object ExecuteAutomation(string command, JsonElement payload)
        {
            try { return ExecuteAutomationCore(command, payload); }
            catch { RecoverFailedEdit(); throw; }
        }
        private void RecoverFailedEdit()
        {
            ResolveSelection(); RefreshRows();
            Plot.Rule = selectedValueRow; Plot.Node = selectedNode;
            Plot.ValidateSelection(); Plot.InvalidateVisual();
            PropertyDraftChanged();
        }
        private object ExecuteAutomationCore(string command, JsonElement payload)
        {
            var definition = HoFace.Automation.AutomationContract.Commands.FirstOrDefault(x => x.Name == command);
            if (definition == null) throw new ArgumentException("未知 HoFaceStudio 命令：" + command);
            if (payload.ValueKind == JsonValueKind.Undefined) payload = JsonSerializer.SerializeToElement(new { });
            if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("payload 必须是 JSON 对象。");
            if (definition.RequiresDocument && !HasDocument) throw new InvalidOperationException("请先打开作品。");
            if (editBefore != null && !definition.ReadOnly) throw new InvalidOperationException("图形拖动尚未结束，请先提交或取消拖动。");
            if (command.StartsWith("set", StringComparison.Ordinal) || command == "text" ||
                command == "addValueCurveKey" || command == "deleteValueCurveKey") EnsureNoDraft();
            switch (command)
            {
                case "capabilities": return HoFace.Automation.AutomationContract.Describe();
                case "document": return ReadDocument();
                case "list": return ReadItems(ReadString(payload, "filter"));
                case "selection": return ReadSelection();
                case "diagnostics": return ReadDiagnostics();
                case "view": return ConfigureView(payload);
                case "selectPoint": Plot.SelectIndex(ReadInt(payload, "index", -1)); return ReadSelection();
                case "setValueExpression": SetValueExpression(payload); break;
                case "setNode": SetNode(payload); break;
                case "addValueCurveKey": AddValueCurveKey(payload); break;
                case "deleteValueCurveKey": DeleteValueCurveKey(payload); break;
                case "draftText":
                    if (HasPropertyDraft) throw new InvalidOperationException("请先应用或还原属性草稿。");
                    if (payload.TryGetProperty("value", out var valueDraft)) ValueText.Text = valueDraft.GetString();
                    if (payload.TryGetProperty("ir", out var irDraft)) IrText.Text = irDraft.GetString();
                    CenterTab = 1; break;
                case "applyText": SyncText(); ReloadEditors(); break;
                case "revertText": ReloadEditors(); break;
                case "applyProperties": CommitPropertyDraft(); ReloadEditors(); break;
                case "revertProperties": RefreshInspector(); break;
                case "evaluate": return EvaluateRaw(payload);
                case "clearPreview":
                    ClearManualPreview(); break;
                case "shutdown":
                    if (HasUnsavedChanges && !ReadBool(payload, "discardChanges"))
                        throw new InvalidOperationException("作品有未保存编辑；保存后关闭，或明确设置 discardChanges。");
                    closingWithoutPrompt = true;
                    shutdownRequested = true;
                    return new { closing = true };
                case "status":
                    return new
                    {
                        app = "HoFaceStudio",
                        protocolVersion = HoFace.Automation.AutomationContract.Version,
                        hasDocument = HasDocument,
                        mode = Mode,
                        dirty = document.IsDirty,
                        textDraft = HasTextDraft,
                        propertyDraft = HasPropertyDraft,
                        canUndo = document.CanUndo,
                        canRedo = document.CanRedo,
                        valuePath = document.ValuePath,
                        irPath = document.IrPath,
                        status = Status,
                        input = InputStatus,
                        target = TargetStatus,
                        selectedValue = selectedValueRow?.parameter,
                        selectedValueIndex = selectedValueRow == null ? (int?)null : selectedOutputIndex,
                        selectedNode = selectedNode?.id,
                        selectedPoint = Plot.SelectedIndex,
                        structureTab = StructureTab,
                        centerTab = CenterTab,
                        manualPreview = manualWeights != null,
                        frames = session.Frames,
                        preview = session.PreviewStatus,
                        rawForward = session.RawForwardStatus,
                        recording = session.Recording,
                        recordedFrames = session.RecordedFrames,
                        replayLoaded = session.ReplayLoaded,
                        replayPlaying = session.ReplayPlaying,
                        replayIndex = session.ReplayIndex
                    };
                case "screenshot":
                    return new { path = CaptureScreenshot(ReadString(payload, "path")) };
                case "open":
                    if (HasUnsavedChanges && !ReadBool(payload, "discardChanges"))
                        throw new InvalidOperationException("作品有未保存编辑；请先保存，或明确设置 discardChanges。");
                    OpenPaths(RequiredString(payload, "valuePath"), RequiredString(payload, "irPath"));
                    return ExecuteAutomation("status", default);
                case "save":
                    CommitPropertyDraft(); SyncText(); document.Save(ReadString(payload, "valuePath"), ReadString(payload, "irPath"));
                    ReloadEditors(); return ExecuteAutomation("status", default);
                case "undo":
                    EnsureNoDraft(); document.Undo(); ReloadEditors(); return ExecuteAutomation("status", default);
                case "redo":
                    EnsureNoDraft(); document.Redo(); ReloadEditors(); return ExecuteAutomation("status", default);
                case "mode":
                    string nextMode = RequiredString(payload, "value");
                    if (nextMode != "Preview" && nextMode != "RawForward") throw new ArgumentException("mode 必须为 Preview 或 RawForward。");
                    int rawPort = ReadInt(payload, "rawForwardPort", session.RawForwardPort);
                    if (rawPort < 1 || rawPort > 65535) throw new ArgumentException("rawForwardPort 必须为 1–65535。");
                    if (session.Mode == AuthoringMode.RawForward && rawPort != session.RawForwardPort) session.SetMode(AuthoringMode.Preview);
                    session.RawForwardPort = rawPort;
                    Mode = nextMode;
                    ClearManualPreview();
                    session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
                    return ExecuteAutomation("status", default);
                case "selectValue":
                    int outputIndex = FindOutputIndex(RequiredString(payload, "parameter"), ReadInt(payload, "outputIndex", -1));
                    CommitPropertyDraft();
                    ValueFilter = "";
                    SelectedValueRow = ValueRows.First(x => x.Index == outputIndex);
                    return ExecuteAutomation("status", default);
                case "selectNode":
                    string nodeId = RequiredString(payload, "id");
                    if (!document.Ir.nodes.Any(x => x.id == nodeId)) throw new ArgumentException("节点不存在：" + nodeId);
                    CommitPropertyDraft();
                    IrFilter = ""; SelectedNodeRow = NodeRows.First(x => x.Node.id == nodeId);
                    return ExecuteAutomation("status", default);
                case "setValueCurveKey":
                    SetValueCurveKey(payload); return ExecuteAutomation("status", default);
                case "setIrChild":
                    SetIrChild(payload); return ExecuteAutomation("status", default);
                case "setPoseWeight":
                    SetPoseWeight(payload); return ExecuteAutomation("status", default);
                case "setDirectWeight":
                    SetDirectWeight(payload); return ExecuteAutomation("status", default);
                case "cursor":
                    PreviewAtCursor(ReadDouble(payload, "x"), payload.TryGetProperty("y", out _) ? ReadDouble(payload, "y") : 0);
                    return ReadDiagnostics();
                case "vtsStart":
                    StartVtsFromPayload(payload); return ExecuteAutomation("status", default);
                case "vtsStop":
                    session.Stop(); return ExecuteAutomation("status", default);
                case "previewStart":
                    if (!CanPreview) throw new InvalidOperationException("只有 Preview 模式支持 Blender 权重发送。");
                    session.Preview.Start(); return ExecuteAutomation("status", default);
                case "previewStop":
                    session.Preview.Stop(); return ExecuteAutomation("status", default);
                case "recordStart":
                    if (session.ReplayPlaying) throw new InvalidOperationException("请先暂停回放，再录制实时输入。");
                    session.StartRecording(RequiredString(payload, "path"), document.ValuePath, document.IrPath);
                    return ExecuteAutomation("status", default);
                case "recordStop":
                    session.StopRecording(); return ExecuteAutomation("status", default);
                case "replayOpen":
                    session.OpenReplay(RequiredString(payload, "path")); return ExecuteAutomation("status", default);
                case "replayPlay":
                    session.StopRecording();
                    if (session.Running) session.Stop();
                    session.PlayReplay(); return ExecuteAutomation("status", default);
                case "replayPause":
                    session.PauseReplay(); return ExecuteAutomation("status", default);
                case "replaySeek":
                    session.SeekReplay(ReadDouble(payload, "time")); return ExecuteAutomation("status", default);
                case "text":
                    document.ApplyText(RequiredString(payload, "value"), RequiredString(payload, "ir"));
                    ReloadEditors(); return ExecuteAutomation("status", default);
                default: throw new ArgumentException("未实现命令：" + command);
            }
            RefreshTransport();
            return ExecuteAutomation("status", default);
        }

        private void OpenPaths(string valuePath, string irPath)
        {
            document.Open(valuePath, irPath);
            chosenValuePath = null; chosenIrPath = null;
            ClearManualPreview();
            selectedOutputIndex = document.Value.outputs.Count > 0 ? 0 : -1;
            RememberValueSelection();
            selectedNodeId = null;
            structureTab = 0;
            OnPropertyChanged(nameof(StructureTab));
            ReloadEditors();
            Plot.Fit();
        }

        private void ApplyDarkWindowChrome()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
                int captionColor = 0x00201A15; // COLORREF for #151A20.
                int textColor = 0x00ECE7E2; // COLORREF for #E2E7EC.
                DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
                DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void SetValueCurveKey(JsonElement payload)
        {
            string parameter = RequiredString(payload, "parameter");
            int index = ReadInt(payload, "index", -1);
            int outputIndex = FindOutputIndex(parameter, ReadInt(payload, "outputIndex", -1));
            var row = document.Value.outputs[outputIndex];
            if (row == null || index < 0 || row.curve == null || index >= row.curve.keys.Length)
                throw new InvalidOperationException("value 曲线行或点位不存在。");
            document.Edit((profile, ir) =>
            {
                var keys = profile.outputs[outputIndex].curve.keys;
                var key = keys[index];
                if (payload.TryGetProperty("time", out var time)) key.time = FiniteFloat(time, "time");
                if (payload.TryGetProperty("value", out var value)) key.value = FiniteFloat(value, "value");
                if (payload.TryGetProperty("inTangent", out var inTangent)) key.inTangent = ReadTangent(inTangent);
                if (payload.TryGetProperty("outTangent", out var outTangent)) key.outTangent = ReadTangent(outTangent);
                if (keys.Where((_, i) => i != index).Any(k => Math.Abs(k.time - key.time) < 1e-5))
                    throw new ArgumentException("曲线点的输入坐标不能重复。");
                keys[index] = key;
                profile.outputs[outputIndex].curve.keys = keys;
            });
            ReloadEditors();
        }

        private void SetIrChild(JsonElement payload)
        {
            EditIrChild(RequiredString(payload, "nodeId"), ReadString(payload, "childId"),
                ReadInt(payload, "index", -1), ReadString(payload, "property"), ReadFloat(payload, "value"));
        }

        private void SetPoseWeight(JsonElement payload)
        {
            EditIrPoseWeight(RequiredString(payload, "nodeId"), RequiredString(payload, "targetId"), ReadFloat(payload, "value"));
        }

        private void EditIrPoseWeight(string nodeId, string targetId, float value)
        {
            var before = SnapshotDocument();
            AuthoringDocument.Parse(before.Value, before.Ir, out _, out var graph);
            var node = graph.nodes.FirstOrDefault(x => x.id == nodeId && x.type == "pose");
            if (node == null || !graph.targets.Any(x => x.id == targetId))
                throw new InvalidOperationException("Pose 节点或目标不存在。");
            var weight = node.weights.FirstOrDefault(x => x.target == targetId);
            if (weight == null) node.weights.Add(new HoFaceIrWeight { target = targetId, value = value });
            else weight.value = value;
            var errors = graph.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            document.ApplyText(before.Value, graph.Write());
            ReloadEditors();
        }

        private void SetDirectWeight(JsonElement payload)
        {
            string nodeId = RequiredString(payload, "nodeId");
            string childId = RequiredString(payload, "childId");
            float value = ReadFloat(payload, "value");
            EditIrChild(nodeId, childId, -1, "weight", value);
        }

        private void EditIrChild(string nodeId, string childId, int index, string property, float value)
        {
            var before = SnapshotDocument();
            AuthoringDocument.Parse(before.Value, before.Ir, out _, out var graph);
            var node = graph.nodes.FirstOrDefault(x => x.id == nodeId);
            if (node == null) throw new InvalidOperationException("IR 节点不存在。");
            var child = !string.IsNullOrEmpty(childId)
                ? node.children.FirstOrDefault(x => x.node == childId)
                : index >= 0 && index < node.children.Count ? node.children[index] : null;
            if (child == null) throw new InvalidOperationException("IR 子项不存在。");
            switch (property)
            {
                case "x": child.x = value; break;
                case "y": child.y = value; break;
                case "threshold": child.threshold = value; break;
                case "weight": child.weight = value; break;
                default: throw new ArgumentException("无效 IR 子项属性：" + property);
            }
            var errors = graph.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            document.ApplyText(before.Value, graph.Write());
            ReloadEditors();
        }

        private AuthoringSnapshot SnapshotDocument() => new AuthoringSnapshot
        {
            Value = HoFaceValueDocument.Write(document.Value),
            Ir = document.Ir.Write()
        };

        private void StartVtsFromPayload(JsonElement payload)
        {
            string ip = ReadString(payload, "phoneIp") ?? PhoneIp;
            int phone = ReadInt(payload, "phonePort", int.Parse(PhonePort));
            int local = ReadInt(payload, "localPort", int.Parse(LocalPort));
            if (phone < 1 || phone > 65535 || local < 1 || local > 65535)
                throw new ArgumentException("端口范围为 1–65535。");
            session.PauseReplay();
            session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
            session.Start(new VtsIphoneOptions { PhoneAddress = ip, PhonePort = phone, LocalPort = local });
        }

        private string CaptureScreenshot(string requestedPath)
        {
            string path = string.IsNullOrWhiteSpace(requestedPath)
                ? Path.Combine(AppContext.BaseDirectory, ".user", "screenshots",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png")
                : requestedPath;
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            UpdateLayout();
            var visual = Content as FrameworkElement ?? (FrameworkElement)this;
            int width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            encoder.Save(stream);
            return Path.GetFullPath(path);
        }

        private static string RequiredString(JsonElement payload, string name)
        {
            string value = ReadString(payload, name);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("缺少参数：" + name);
            return value;
        }
        private static string ReadString(JsonElement payload, string name)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.String) throw new ArgumentException(name + " 必须是字符串。");
            return value.GetString();
        }
        private static int ReadInt(JsonElement payload, string name, int fallback)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result)) throw new ArgumentException(name + " 必须是整数。");
            return result;
        }
        private static float ReadFloat(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float result) && float.IsFinite(result)
                ? result : throw new ArgumentException("缺少数值参数：" + name);
        private static double ReadDouble(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result) && double.IsFinite(result)
                ? result : throw new ArgumentException("缺少数值参数：" + name);
        public sealed class RuleRow : INotifyPropertyChanged
        {
            public HoFaceOutput Row { get; private set; }
            public int Index { get; }
            public string parameter => Row.parameter;
            public string Expression => Row.expression;
            public string Name => Row.parameter;
            public string Display => Row.parameter + " = " + Row.expression;
            public RuleRow(HoFaceOutput row, int index = 0) { Row = row; Index = index; }
            public event PropertyChangedEventHandler PropertyChanged;
            public void Update(HoFaceOutput row)
            {
                Row = row;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
            }
        }
        public sealed class WeightRow
        {
            public string Name { get; }
            public string Value { get; set; }
            public string Kind { get; }
            public string NodeId { get; }
            public string TargetId { get; }
            public string ChildId { get; }
            public WeightRow(string name, string value, string kind = "", string nodeId = "", string targetId = "", string childId = "")
            {
                Name = name; Value = value; Kind = kind; NodeId = nodeId; TargetId = targetId; ChildId = childId;
            }
        }
        public sealed class NodeRow : INotifyPropertyChanged
        {
            public HoFaceIrNode Node { get; private set; }
            public string Name => string.IsNullOrWhiteSpace(Node.name) ? Node.id : Node.name;
            public string Detail => Node.id + " · " + Node.type;
            public string Display => Node.id + " · " + Node.type + (string.IsNullOrEmpty(Node.name) ? "" : " · " + Node.name);
            public NodeRow(HoFaceIrNode node) { Node = node; }
            public event PropertyChangedEventHandler PropertyChanged;
            public void Update(HoFaceIrNode node)
            {
                Node = node;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
            }
        }
    }
}
