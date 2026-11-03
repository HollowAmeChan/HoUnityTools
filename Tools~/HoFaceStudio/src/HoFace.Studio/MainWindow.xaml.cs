using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        public string InspectorName { get => selectedValueRow?.parameter ?? selectedNode?.name ?? ""; set { if (selectedValueRow != null) selectedValueRow.parameter = value; else if (selectedNode != null) selectedNode.name = value; OnPropertyChanged(); } }
        public string InspectorExpression { get => selectedValueRow?.expression ?? (selectedNode?.type + " · " + selectedNode?.parameterX + " / " + selectedNode?.parameterY) ?? ""; set { if (selectedValueRow != null) selectedValueRow.expression = value; OnPropertyChanged(); } }
        public string Diagnostics { get; private set; } = "默认制作模式会在外部执行 value + IR。\n切换到裸值转发后才会把原始字典交给角色接收器。";
        public string PlotHint => selectedValueRow != null ? "双击空白添加曲线点；拖点或切线；滚轮缩放；右键平移。" : "IR 图形编辑将在选择 IR 节点后显示。";
        public MainWindow()
        {
            InitializeComponent(); DataContext = this;
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
                    session.Preview.Send(values, weights, document.Ir.targets, true, session.Frames / 60.0);
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
            if (!document.IsDirty) return;
            var answer = MessageBox.Show("当前有未保存的 value / IR 编辑，是否保存？", "HoFaceStudio", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) try { SaveDocument(); } catch (Exception error) { MessageBox.Show(error.Message); e.Cancel = true; }
        }
        private void OpenClicked(object sender, RoutedEventArgs e) => OpenDocument();
        private void SaveClicked(object sender, RoutedEventArgs e) => TrySave();
        private void UndoClicked(object sender, RoutedEventArgs e) { document.Undo(); ReloadEditors(); }
        private void RedoClicked(object sender, RoutedEventArgs e) { document.Redo(); ReloadEditors(); }
        private void ModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { OnPropertyChanged(nameof(Status)); }
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
                foreach (var w in selectedNode.weights) InspectorWeights.Add(new WeightRow(w.target, w.value.ToString("0.###")));
            OnPropertyChanged(nameof(InspectorName)); OnPropertyChanged(nameof(InspectorExpression)); OnPropertyChanged(nameof(InspectorSummary)); OnPropertyChanged(nameof(InspectorTitle)); OnPropertyChanged(nameof(PlotHint));
        }
        private void PlotEditBeginning() { editBefore ??= document.Snapshot(); }
        private void PlotEditChanged() { Plot.InvalidateVisual(); OnPropertyChanged(nameof(HeaderStatus)); }
        private void PlotEditCommitted() { if (editBefore != null) { document.Remember(editBefore); editBefore = null; } OnPropertyChanged(nameof(HeaderStatus)); }
        private void PlotSelectionChanged() { OnPropertyChanged(nameof(InspectorName)); OnPropertyChanged(nameof(InspectorExpression)); }
        private void PlotPreviewChanged(double x, double y) { Plot.CursorX = x; Plot.CursorY = y; Plot.InvalidateVisual(); }
        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
            public WeightRow(string name, string value) { Name = name; Value = value; }
        }
        public sealed class NodeRow
        {
            public HoFaceIrNode Node { get; }
            public string Display => Node.id + " · " + Node.type + (string.IsNullOrEmpty(Node.name) ? "" : " · " + Node.name);
            public NodeRow(HoFaceIrNode node) { Node = node; }
        }
    }
}
