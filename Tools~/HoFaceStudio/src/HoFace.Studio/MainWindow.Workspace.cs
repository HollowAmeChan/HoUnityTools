using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace HoFace.Studio
{
    public partial class MainWindow
    {
        private bool showFiles, showConnection;
        private string chosenValuePath, chosenIrPath;
        public string ValueFilePath => chosenValuePath ?? document.ValuePath;
        public string IrFilePath => chosenIrPath ?? document.IrPath;
        public bool HasPendingFilePaths => chosenValuePath != null || chosenIrPath != null;
        private void NotifyFilePaths()
        {
            OnPropertyChanged(nameof(ValueFilePath)); OnPropertyChanged(nameof(IrFilePath));
            OnPropertyChanged(nameof(HasPendingFilePaths)); OnPropertyChanged(nameof(HeaderStatus));
        }
        private void BrowseValueFileClicked(object sender, RoutedEventArgs e) => BrowseDocumentFile(true);
        private void BrowseIrFileClicked(object sender, RoutedEventArgs e) => BrowseDocumentFile(false);
        private void BrowseDocumentFile(bool valueFile)
        {
            string current = valueFile ? ValueFilePath : IrFilePath;
            string other = valueFile ? IrFilePath : ValueFilePath;
            string availablePath = current ?? other;
            string directory = string.IsNullOrWhiteSpace(availablePath) ? null : Path.GetDirectoryName(availablePath);
            var dialog = new OpenFileDialog
            {
                Title = valueFile ? "选择 value JSON 文件" : "选择 IR JSON 文件",
                Filter = valueFile
                    ? "HoFace value (*.hoface.value.json;*.hoface.json)|*.hoface.value.json;*.hoface.json|JSON (*.json)|*.json"
                    : "HoFace IR (*.hoface.ir.json)|*.hoface.ir.json|JSON (*.json)|*.json",
                InitialDirectory = Directory.Exists(directory) ? directory : "",
                CheckFileExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            if (string.Equals(Path.GetFullPath(dialog.FileName), current, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (!ConfirmReplaceDocument()) return;
                ChooseDocumentFilePath(valueFile, dialog.FileName, true);
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        public void ChooseDocumentFilePath(bool valueFile, string path, bool discardChanges = false)
        {
            string selected = Path.GetFullPath(path);
            if (!File.Exists(selected)) throw new FileNotFoundException("所选 JSON 文件不存在。", selected);
            string current = valueFile ? ValueFilePath : IrFilePath;
            if (string.Equals(selected, current, StringComparison.OrdinalIgnoreCase)) return;
            if (HasUnsavedChanges && !discardChanges)
                throw new InvalidOperationException("作品有未保存编辑，请先保存再更换文件。");
            string valuePath = valueFile ? selected : ValueFilePath;
            string irPath = valueFile ? IrFilePath : selected;
            if (!string.IsNullOrWhiteSpace(valuePath) && !string.IsNullOrWhiteSpace(irPath))
            {
                OpenPaths(valuePath, irPath);
            }
            else
            {
                if (valueFile) chosenValuePath = selected; else chosenIrPath = selected;
                NotifyFilePaths();
            }
            ShowFiles = true;
        }
        private bool showEditor = true;
        private double preferredPlotRatio = .475;
        public bool ShowEditor
        {
            get => showEditor;
            set
            {
                if (showEditor == value) return;
                if (!value)
                {
                    double total = PlotColumn.ActualWidth + InspectorColumn.ActualWidth;
                    if (total > 0) preferredPlotRatio = PlotColumn.ActualWidth / total;
                    PlotColumn.MinWidth = 0; PlotColumn.Width = new GridLength(0);
                    EditorSplitterColumn.Width = new GridLength(0);
                    InspectorColumn.Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    PlotColumn.MinWidth = 280;
                    PlotColumn.Width = new GridLength(preferredPlotRatio, GridUnitType.Star);
                    InspectorColumn.Width = new GridLength(1 - preferredPlotRatio, GridUnitType.Star);
                    EditorSplitterColumn.Width = new GridLength(8);
                }
                showEditor = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorVisibilityAction));
            }
        }
        public string EditorVisibilityAction => ShowEditor ? "收起编辑区" : "展开编辑区";
        private bool persistWorkspace;
        private DateTime lastTransportRefresh;
        public bool ShowFiles { get => showFiles; set { showFiles = value; OnPropertyChanged(); } }
        public bool ShowConnection { get => showConnection; set { showConnection = value; OnPropertyChanged(); } }
        public bool HasDocument => document.Value != null;
        public bool CanPreview => HasDocument && Mode == "Preview";
        public bool ReplayLoaded => session.ReplayLoaded;
        public string DocumentName => document.ValuePath == null ? "" : Path.GetFileName(document.ValuePath).Replace(".hoface.value.json", "");
        public string ConnectionAction => session.Running ? "断开 VTS" : "连接 VTS";
        public string PreviewAction => session.Preview.Running ? "停止发送" : "发送预览";
        public string RecordAction => session.Recording ? "停止录制" : "开始录制";
        public string ReplayAction => session.ReplayPlaying ? "暂停" : "播放";
        public string RecordingStatus => session.Recording ? "● 录制中 · " + session.RecordedFrames + " 帧" :
            session.ReplayLoaded ? (session.ReplayPlaying ? "回放中" : "回放已就绪") + " · 帧 " + session.ReplayIndex : "";
        public string OutputStatus => Mode == "RawForward" ? session.RawForwardStatus : session.PreviewStatus;
        private void RefreshTransport()
        {
            foreach (string name in new[] { nameof(HasDocument), nameof(CanPreview), nameof(ConnectionAction),
                nameof(PreviewAction), nameof(OutputStatus), nameof(RecordAction), nameof(ReplayAction),
                nameof(ReplayLoaded), nameof(RecordingStatus), nameof(DocumentName), nameof(InputStatus) })
                OnPropertyChanged(name);
        }
        private void ConnectionToggleClicked(object sender, RoutedEventArgs e)
        {
            if (session.Running) StopClicked(sender, e); else ConnectClicked(sender, e);
            RefreshTransport();
        }
        private void PreviewToggleClicked(object sender, RoutedEventArgs e)
        {
            if (!CanPreview) return;
            if (session.Preview.Running) StopPreviewClicked(sender, e); else PreviewClicked(sender, e);
            RefreshTransport();
        }
        private void RecordToggleClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (session.Recording) session.StopRecording();
                else
                {
                    if (session.ReplayPlaying) throw new InvalidOperationException("请先暂停回放，再录制实时输入。");
                    var dialog = new SaveFileDialog { Filter = "HoFace 录制 (*.jsonl)|*.jsonl", FileName = "face-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl" };
                    if (dialog.ShowDialog(this) != true) return;
                    session.StartRecording(dialog.FileName, document.ValuePath, document.IrPath);
                }
                RefreshTransport();
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void OpenReplayClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "HoFace 录制 (*.jsonl)|*.jsonl" };
            if (dialog.ShowDialog(this) != true) return;
            try { session.OpenReplay(dialog.FileName); RefreshTransport(); }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void ReplayToggleClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (session.ReplayPlaying) session.PauseReplay();
                else
                {
                    session.StopRecording();
                    if (session.Running) session.Stop();
                    session.PlayReplay();
                }
                RefreshTransport();
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void ResetLayoutClicked(object sender, RoutedEventArgs e) => ResetLayout();
        private void ToggleEditorClicked(object sender, RoutedEventArgs e) => ShowEditor = !ShowEditor;
        private void SetPlotRatio(double ratio)
        {
            preferredPlotRatio = ratio;
            if (!ShowEditor) return;
            PlotColumn.Width = new GridLength(ratio, GridUnitType.Star);
            InspectorColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }
        private void ResetLayout()
        {
            StructureColumn.Width = new GridLength(240);
            ShowEditor = true; SetPlotRatio(.475);
        }
        private static string WorkspacePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoFaceStudio", "workspace.json");
        private void LoadWorkspace()
        {
            if (!persistWorkspace || !File.Exists(WorkspacePath)) return;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(WorkspacePath));
                var state = json.RootElement;
                double structure = state.GetProperty("structure").GetDouble();
                double plot = state.GetProperty("plotRatio").GetDouble();
                if (double.IsFinite(structure) && double.IsFinite(plot) && structure >= 180 && structure <= 500 && plot > 0 && plot < 1)
                {
                    StructureColumn.Width = new GridLength(structure);
                    SetPlotRatio(plot);
                    UpdateLayout();
                    if (state.TryGetProperty("editorVisible", out var visible)) ShowEditor = visible.GetBoolean();
                }
            }
            catch (Exception error) when (error is IOException || error is JsonException ||
                error is InvalidOperationException || error is KeyNotFoundException || error is UnauthorizedAccessException)
            { ResetLayout(); }
        }
        private void SaveWorkspace()
        {
            if (!persistWorkspace) return;
            try
            {
                double total = PlotColumn.ActualWidth + InspectorColumn.ActualWidth;
                if (total <= 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(WorkspacePath));
                File.WriteAllText(WorkspacePath, JsonSerializer.Serialize(new
                {
                    structure = StructureColumn.ActualWidth,
                    plotRatio = ShowEditor ? PlotColumn.ActualWidth / total : preferredPlotRatio,
                    editorVisible = ShowEditor
                }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
