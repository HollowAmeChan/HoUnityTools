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
                nameof(ReplayLoaded), nameof(RecordingStatus), nameof(DocumentName) })
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
                if (session.ReplayPlaying) session.PauseReplay(); else session.PlayReplay();
                RefreshTransport();
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void ResetLayoutClicked(object sender, RoutedEventArgs e) => ResetLayout();
        private void ResetLayout()
        {
            StructureColumn.Width = new GridLength(240);
            PlotColumn.Width = new GridLength(.95, GridUnitType.Star);
            InspectorColumn.Width = new GridLength(1.05, GridUnitType.Star);
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
                if (double.IsFinite(structure) && double.IsFinite(plot) && structure >= 180 && structure <= 500 && plot >= .1 && plot <= .9)
                {
                    StructureColumn.Width = new GridLength(structure);
                    PlotColumn.Width = new GridLength(plot, GridUnitType.Star);
                    InspectorColumn.Width = new GridLength(1 - plot, GridUnitType.Star);
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
                    plotRatio = PlotColumn.ActualWidth / total
                }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
