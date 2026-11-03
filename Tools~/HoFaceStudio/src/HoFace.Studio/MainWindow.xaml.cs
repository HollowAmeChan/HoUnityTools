using System;
using System.Collections.ObjectModel;
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
        public ObservableCollection<RuleRow> ValueRows { get; } = new ObservableCollection<RuleRow>();
        public ObservableCollection<NodeRow> NodeRows { get; } = new ObservableCollection<NodeRow>();
        public ObservableCollection<WeightRow> InspectorWeights { get; } = new ObservableCollection<WeightRow>();
        public event PropertyChangedEventHandler PropertyChanged;
        public string ValuePath { get => document.ValuePath; set { if (document.ValuePath != value) { OnPropertyChanged(); } } }
        public string IrPath { get => document.IrPath; set { if (document.IrPath != value) { OnPropertyChanged(); } } }
        public string TargetName { get; set; } = "Blender / Unity 预览目标";
        public string Mode { get => mode; set { mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); } }
        public string ValueFilter { get => valueFilter; set { valueFilter = value ?? ""; RefreshRows(); OnPropertyChanged(); } }
        public string IrFilter { get => irFilter; set { irFilter = value ?? ""; RefreshRows(); OnPropertyChanged(); } }
        public int CenterTab { get => centerTab; set { centerTab = value; OnPropertyChanged(); } }
        public RuleRow SelectedValueRow
        {
            get => ValueRows.FirstOrDefault(x => x.Row == selectedValueRow);
            set { selectedValueRow = value?.Row; selectedNode = null; RefreshPlot(); OnPropertyChanged(); OnPropertyChanged(nameof(InspectorTitle)); }
        }
        public NodeRow SelectedNodeRow
        {
            get => NodeRows.FirstOrDefault(x => x.Node == selectedNode);
            set { selectedNode = value?.Node; selectedValueRow = null; RefreshPlot(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedValueRow)); }
        }
        public string HeaderStatus => document.Value == null ? "未打开文档" : (document.IsDirty ? "● 有未保存编辑" : "已保存");
        public string Status => document.Value == null ? "先打开一对 .hoface.value.json + .hoface.ir.json" : "就绪 · " + Mode;
        public string InputStatus => "输入：" + session.TransportStatus;
        public string PhoneIp { get => phoneIp; set { phoneIp = value; OnPropertyChanged(); } }
        public string PhonePort { get => phonePort; set { phonePort = value; OnPropertyChanged(); } }
        public string LocalPort { get => localPort; set { localPort = value; OnPropertyChanged(); } }
        public string LiveStatus => liveStatus;
        public string TargetStatus => TargetName;
        public string InspectorTitle => selectedValueRow != null ? "value 行" : selectedNode != null ? "IR 节点" : "检查器";
        public string InspectorSummary => selectedValueRow != null ? selectedValueRow.parameter : selectedNode != null ? selectedNode.id : "选择左侧规则或 IR 节点";
        public bool WeightsReadOnly => selectedNode == null;
        public string InspectorName { get => selectedValueRow?.parameter ?? selectedNode?.name ?? ""; set { if (selectedValueRow != null) selectedValueRow.parameter = value; else if (selectedNode != null) selectedNode.name = value; OnPropertyChanged(); } }
        public string InspectorExpression { get => selectedValueRow?.expression ?? (selectedNode?.type + " · " + selectedNode?.parameterX + " / " + selectedNode?.parameterY) ?? ""; set { if (selectedValueRow != null) selectedValueRow.expression = value; OnPropertyChanged(); } }
        public string Diagnostics { get; private set; } = "Blender：HoFaceStudio 执行 value + IR，仅写入表情权重。\nUnity：只转发 raw，value / LookAt 由 Unity 消费。";
        public string PlotHint => selectedValueRow != null ? "曲线点/切线可拖动；双击添加点；滚轮缩放；右键平移。"
            : selectedNode == null ? "选择一个 IR 节点查看参数。" : selectedNode.type == "pose"
            ? "修改右侧形态键权重。" : selectedNode.type == "direct"
            ? "修改右侧子项权重；组合按 IR 归一化设置求值。" : "拖动或编辑右侧控制点坐标；预览游标是临时输入。";
        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => ApplyDarkWindowChrome();
            DataContext = this;
            automation = new HoFaceAutomationServer(this);
            automation.Start();
            Plot.SizeChanged += (s, e) => Plot.Fit();
            session.StatusChanged += status => Dispatcher.Invoke(() =>
            {
                liveStatus = status;
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            session.PreviewUpdated += (values, weights) => Dispatcher.Invoke(() =>
            {
                liveStatus = "VTS " + session.Frames + " 帧 · value " + values.Count + " · 权重 " + weights.Count;
                if (session.Preview.Running)
                    session.Preview.Send(values, weights, true, session.Frames / 60.0);
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            session.RawFrameForward += frame => Dispatcher.Invoke(() =>
            {
                liveStatus = "裸值已收到 " + session.Frames + " 帧 · 等待角色桥接";
                OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(InputStatus));
            });
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            pollTimer.Tick += (s, e) => session.Poll();
            pollTimer.Start();
            Closing += OnClosing;
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (document.IsDirty)
            {
                var answer = MessageBox.Show("当前有未保存的 value / IR 编辑，是否保存？", "HoFaceStudio", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Cancel) e.Cancel = true;
                else if (answer == MessageBoxResult.Yes) try { SaveDocument(); } catch (Exception error) { MessageBox.Show(error.Message); e.Cancel = true; }
            }
            if (!e.Cancel) automation.Dispose();
        }
        private void OpenClicked(object sender, RoutedEventArgs e) => OpenDocument();
        private void SaveClicked(object sender, RoutedEventArgs e) => TrySave();
        private void UndoClicked(object sender, RoutedEventArgs e) { document.Undo(); ReloadEditors(); }
        private void RedoClicked(object sender, RoutedEventArgs e) { document.Redo(); ReloadEditors(); }
        private void ModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
            OnPropertyChanged(nameof(Status));
        }
        private void ConnectClicked(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PhonePort, out int phone) || !int.TryParse(LocalPort, out int local))
            {
                MessageBox.Show("端口必须是整数。", "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            try
            {
                session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
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
        private void SaveCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.Value != null && document.IsDirty;
        private void UndoExecuted(object sender, ExecutedRoutedEventArgs e) { document.Undo(); ReloadEditors(); }
        private void UndoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanUndo;
        private void RedoExecuted(object sender, ExecutedRoutedEventArgs e) { document.Redo(); ReloadEditors(); }
        private void RedoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = document.CanRedo;
        private void WeightsCellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != System.Windows.Controls.DataGridEditAction.Commit || e.Row.Item is not WeightRow row) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!float.TryParse(row.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value)) { ReloadEditors(); return; }
                try
                {
                    if (row.Kind == "pose") EditIrPoseWeight(row.NodeId, row.TargetId, value);
                    else EditIrChild(row.NodeId, row.ChildId, -1, row.Kind, value);
                }
                catch (Exception error) { MessageBox.Show(error.Message, "IR 编辑失败", MessageBoxButton.OK, MessageBoxImage.Warning); ReloadEditors(); }
            }));
        }
        private void OpenDocument()
        {
            var value = new Microsoft.Win32.OpenFileDialog { Filter = "HoFace value (*.hoface.value.json;*.hoface.json)|*.hoface.value.json;*.hoface.json|JSON|*.json" };
            if (value.ShowDialog() != true) return;
            var ir = new Microsoft.Win32.OpenFileDialog { Filter = "HoFace IR (*.hoface.ir.json)|*.hoface.ir.json|JSON|*.json" };
            if (ir.ShowDialog() != true) return;
            try { document.Open(value.FileName, ir.FileName); session.Configure(document); ReloadEditors(); }
            catch (Exception error) { MessageBox.Show(error.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void TrySave() { try { SaveDocument(); } catch (Exception error) { MessageBox.Show(error.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void SaveDocument() { SyncText(); document.Save(); ReloadEditors(); }
        private void SyncText()
        {
            if (document.Value == null) return;
            if (CenterTab == 1 && !string.IsNullOrWhiteSpace(ValueText.Text) && !string.IsNullOrWhiteSpace(IrText.Text))
                document.ApplyText(ValueText.Text, IrText.Text);
        }
        private void ReloadEditors()
        {
            ValueText.Text = document.Value == null ? "" : HoFaceValueDocument.Write(document.Value);
            IrText.Text = document.Ir == null ? "" : document.Ir.Write();
            RefreshRows(); RefreshPlot();
            OnPropertyChanged(nameof(ValuePath)); OnPropertyChanged(nameof(IrPath)); OnPropertyChanged(nameof(HeaderStatus));
            OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(InspectorTitle)); OnPropertyChanged(nameof(InspectorSummary));
        }
        private void RefreshRows()
        {
            ValueRows.Clear();
            NodeRows.Clear();
            if (document.Value != null)
                foreach (var row in document.Value.outputs.Where(x => string.IsNullOrWhiteSpace(ValueFilter) || (x.parameter + " " + x.expression).IndexOf(ValueFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    ValueRows.Add(new RuleRow(row));
            if (document.Ir != null)
                foreach (var node in document.Ir.nodes.Where(x => string.IsNullOrWhiteSpace(IrFilter)
                    || (x.id + " " + x.name + " " + x.type + " " + x.parameterX + " " + x.parameterY).IndexOf(IrFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    NodeRows.Add(new NodeRow(node));
            OnPropertyChanged(nameof(SelectedValueRow));
            OnPropertyChanged(nameof(SelectedNodeRow));
        }
        private void RefreshPlot()
        {
            Plot.Rule = selectedValueRow;
            Plot.Node = selectedNode;
            Plot.Fit(); Plot.InvalidateVisual();
            InspectorWeights.Clear();
            if (selectedValueRow != null)
                InspectorWeights.Add(new WeightRow("expression", selectedValueRow.expression));
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
        }
        private void PlotEditBeginning() { editBefore ??= document.Snapshot(); }
        private void PlotEditChanged() { Plot.InvalidateVisual(); OnPropertyChanged(nameof(HeaderStatus)); }
        private void PlotEditCommitted() { if (editBefore != null) { document.Remember(editBefore); editBefore = null; } OnPropertyChanged(nameof(HeaderStatus)); }
        private void PlotSelectionChanged() { OnPropertyChanged(nameof(InspectorName)); OnPropertyChanged(nameof(InspectorExpression)); }
        private void PlotPreviewChanged(double x, double y) { Plot.CursorX = x; Plot.CursorY = y; Plot.InvalidateVisual(); }
        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public object ExecuteAutomation(string command, JsonElement payload)
        {
            switch (command)
            {
                case "status":
                    return new
                    {
                        app = "HoFaceStudio",
                        mode = Mode,
                        dirty = document.IsDirty,
                        valuePath = document.ValuePath,
                        irPath = document.IrPath,
                        status = Status,
                        input = InputStatus,
                        target = TargetStatus,
                        selectedValue = selectedValueRow?.parameter,
                        selectedNode = selectedNode?.id,
                        frames = session.Frames,
                        preview = session.PreviewStatus,
                        recording = session.Recording,
                        recordedFrames = session.RecordedFrames,
                        replayLoaded = session.ReplayLoaded,
                        replayPlaying = session.ReplayPlaying,
                        replayIndex = session.ReplayIndex
                    };
                case "screenshot":
                    return new { path = CaptureScreenshot(ReadString(payload, "path")) };
                case "open":
                    OpenPaths(RequiredString(payload, "valuePath"), RequiredString(payload, "irPath"));
                    return ExecuteAutomation("status", default);
                case "save":
                    SaveDocument(); return ExecuteAutomation("status", default);
                case "undo":
                    document.Undo(); ReloadEditors(); return ExecuteAutomation("status", default);
                case "redo":
                    document.Redo(); ReloadEditors(); return ExecuteAutomation("status", default);
                case "mode":
                    Mode = RequiredString(payload, "value") == "RawForward" ? "RawForward" : "Preview";
                    session.SetMode(Mode == "RawForward" ? AuthoringMode.RawForward : AuthoringMode.Preview);
                    return ExecuteAutomation("status", default);
                case "selectValue":
                    SelectedValueRow = ValueRows.FirstOrDefault(x => x.parameter == RequiredString(payload, "parameter"));
                    return ExecuteAutomation("status", default);
                case "selectNode":
                    SelectedNodeRow = NodeRows.FirstOrDefault(x => x.Node.id == RequiredString(payload, "id"));
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
                    PlotPreviewChanged(ReadDouble(payload, "x"), ReadDouble(payload, "y"));
                    return ExecuteAutomation("status", default);
                case "vtsStart":
                    StartVtsFromPayload(payload); return ExecuteAutomation("status", default);
                case "vtsStop":
                    session.Stop(); return ExecuteAutomation("status", default);
                case "previewStart":
                    session.Preview.Start(); return ExecuteAutomation("status", default);
                case "previewStop":
                    session.Preview.Stop(); return ExecuteAutomation("status", default);
                case "recordStart":
                    session.StartRecording(RequiredString(payload, "path"), document.ValuePath, document.IrPath);
                    return ExecuteAutomation("status", default);
                case "recordStop":
                    session.StopRecording(); return ExecuteAutomation("status", default);
                case "replayOpen":
                    session.OpenReplay(RequiredString(payload, "path")); return ExecuteAutomation("status", default);
                case "replayPlay":
                    session.PlayReplay(); return ExecuteAutomation("status", default);
                case "replayPause":
                    session.PauseReplay(); return ExecuteAutomation("status", default);
                case "replaySeek":
                    session.SeekReplay(ReadDouble(payload, "time")); return ExecuteAutomation("status", default);
                case "text":
                    document.ApplyText(RequiredString(payload, "value"), RequiredString(payload, "ir"));
                    ReloadEditors(); return ExecuteAutomation("status", default);
                default: throw new InvalidOperationException("未知 HoFaceStudio 命令：" + command);
            }
        }

        private void OpenPaths(string valuePath, string irPath)
        {
            document.Open(valuePath, irPath);
            session.Configure(document);
            ReloadEditors();
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
            var row = document.Value.outputs.FirstOrDefault(x => x.parameter == parameter);
            if (row == null || index < 0 || row.curve == null || index >= row.curve.keys.Length)
                throw new InvalidOperationException("value 曲线行或点位不存在。");
            var keys = row.curve.keys;
            var key = keys[index];
            if (payload.TryGetProperty("time", out var time)) key.time = time.GetSingle();
            if (payload.TryGetProperty("value", out var value)) key.value = value.GetSingle();
            if (payload.TryGetProperty("inTangent", out var inTangent)) key.inTangent = inTangent.GetSingle();
            if (payload.TryGetProperty("outTangent", out var outTangent)) key.outTangent = outTangent.GetSingle();
            keys[index] = key;
            document.Edit((profile, ir) => row.curve.keys = keys);
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
            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
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
        private static string ReadString(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        private static int ReadInt(JsonElement payload, string name, int fallback) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.TryGetInt32(out int result) ? result : fallback;
        private static float ReadFloat(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.TryGetSingle(out float result)
                ? result : throw new ArgumentException("缺少数值参数：" + name);
        private static double ReadDouble(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.TryGetDouble(out double result)
                ? result : throw new ArgumentException("缺少数值参数：" + name);
        public sealed class RuleRow
        {
            public HoFaceOutput Row { get; }
            public string parameter => Row.parameter;
            public string Display => Row.parameter + " = " + Row.expression;
            public RuleRow(HoFaceOutput row) { Row = row; }
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
        public sealed class NodeRow
        {
            public HoFaceIrNode Node { get; }
            public string Display => Node.id + " · " + Node.type + (string.IsNullOrEmpty(Node.name) ? "" : " · " + Node.name);
            public NodeRow(HoFaceIrNode node) { Node = node; }
        }
    }
}
