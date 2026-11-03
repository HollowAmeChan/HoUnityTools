using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public partial class MainWindow
    {
        public ObservableCollection<CurveRow> CurveRows { get; } = new ObservableCollection<CurveRow>();
        public int ValueCount => ValueRows.Count;
        public int NodeCount => NodeRows.Count;
        public bool HasValueSelection => selectedValueRow != null;
        public bool HasNodeSelection => selectedNode != null;
        public bool HasSelection => HasValueSelection || HasNodeSelection;
        public string SelectionTitle => selectedValueRow?.parameter ??
            (string.IsNullOrWhiteSpace(selectedNode?.name) ? selectedNode?.id : selectedNode.name) ?? "未选择";
        public string SelectionKind => HasValueSelection ? "VALUE / 响应曲线" : selectedNode?.type?.ToUpperInvariant() ?? "作品编辑器";
        public string WeightSectionTitle => selectedNode?.type == "pose" ? "姿势权重" :
            selectedNode?.type == "blend1d" ? "子项阈值" : selectedNode?.type == "blend2d" ? "控制点坐标" : "子项权重";
        public bool HasAxisX => selectedNode?.type == "blend1d" || selectedNode?.type == "blend2d";
        public bool HasAxisY => selectedNode?.type == "blend2d";
        private string draftName = "", draftExpression = "", draftAxisX = "", draftAxisY = "";
        public string DraftName { get => draftName; set { draftName = value; OnPropertyChanged(); } }
        public string DraftExpression { get => draftExpression; set { draftExpression = value; OnPropertyChanged(); } }
        public string DraftAxisX { get => draftAxisX; set { draftAxisX = value; OnPropertyChanged(); } }
        public string DraftAxisY { get => draftAxisY; set { draftAxisY = value; OnPropertyChanged(); } }
        private void RefreshInspector()
        {
            DraftName = selectedValueRow?.parameter ?? selectedNode?.name ?? "";
            DraftExpression = selectedValueRow?.expression ?? "";
            DraftAxisX = selectedNode?.parameterX ?? "";
            DraftAxisY = selectedNode?.parameterY ?? "";
            CurveRows.Clear();
            var keys = selectedValueRow?.curve?.keys;
            if (keys != null)
                for (int i = 0; i < keys.Length; i++) CurveRows.Add(new CurveRow(i, keys[i]));
            foreach (string name in new[] { nameof(HasValueSelection), nameof(HasNodeSelection), nameof(HasSelection),
                nameof(SelectionTitle), nameof(SelectionKind), nameof(WeightSectionTitle), nameof(HasAxisX), nameof(HasAxisY) })
                OnPropertyChanged(name);
        }
        private void TextDraftChanged()
        {
            if (loadingText) return;
            OnPropertyChanged(nameof(HasTextDraft));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HeaderStatus));
            CommandManager.InvalidateRequerySuggested();
        }
        private void ShowError(string message)
        {
            Diagnostics = message;
            OnPropertyChanged(nameof(Diagnostics));
        }
        private bool ConfirmReplaceDocument()
        {
            if (!HasUnsavedChanges) return true;
            var answer = MessageBox.Show("保存当前作品后再打开其他文件？", "未保存的编辑",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes) SaveDocument();
            return true;
        }
        private void EnsureNoDraft()
        {
            if (HasTextDraft) throw new InvalidOperationException("JSON 文本有待应用编辑。请先执行 applyText 或 revertText。");
        }
        private void UndoDocument()
        {
            try { SyncText(); document.Undo(); ReloadEditors(); }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void RedoDocument()
        {
            try { EnsureNoDraft(); document.Redo(); ReloadEditors(); }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void ApplyTextClicked(object sender, RoutedEventArgs e)
        {
            try { SyncText(); ReloadEditors(); }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void RevertTextClicked(object sender, RoutedEventArgs e) => ReloadEditors();
        private void FitClicked(object sender, RoutedEventArgs e) => Plot.Fit();
        private void ApplyPropertiesClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (!HasSelection) return;
                string name = DraftName, expression = DraftExpression, axisX = DraftAxisX, axisY = DraftAxisY;
                document.Edit((value, ir) =>
                {
                    if (HasValueSelection)
                    {
                        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("参数名称不能为空。");
                        value.outputs[selectedOutputIndex].parameter = name.Trim();
                        value.outputs[selectedOutputIndex].expression = expression;
                    }
                    else
                    {
                        var node = ir.nodes.First(x => x.id == selectedNodeId);
                        node.name = name;
                        if (HasAxisX) node.parameterX = axisX;
                        if (HasAxisY) node.parameterY = axisY;
                    }
                });
                ReloadEditors();
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void ReloadEditorsKeepingDraft()
        {
            if (!HasTextDraft) ReloadEditors();
        }
        private void CurveCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not CurveRow row) return;
            string text = (e.EditingElement as TextBox)?.Text;
            string property = e.Column.SortMemberPath;
            int outputIndex = selectedOutputIndex, keyIndex = row.Index;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureNoDraft();
                    document.Edit((value, ir) =>
                    {
                        var curve = value.outputs[outputIndex].curve;
                        var keys = curve.keys;
                        var key = keys[keyIndex];
                        float number = ParseNumber(text, property == "InTangent" || property == "OutTangent");
                        switch (property)
                        {
                            case "Time": key.time = number; break;
                            case "Value": key.value = number; break;
                            case "InTangent": key.inTangent = number; break;
                            case "OutTangent": key.outTangent = number; break;
                            default: throw new ArgumentException("未知曲线属性。");
                        }
                        if (keys.Where((_, i) => i != keyIndex).Any(k => Math.Abs(k.time - key.time) < 1e-5))
                            throw new ArgumentException("曲线点的输入坐标不能重复。");
                        keys[keyIndex] = key; curve.keys = keys;
                    });
                    ReloadEditors();
                }
                catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
            }));
        }
        private void AddCurvePointClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (selectedValueRow == null) return;
                document.Edit((value, ir) =>
                {
                    var row = value.outputs[selectedOutputIndex];
                    var keys = row.curve?.keys ?? Array.Empty<HoFaceCurveKey>();
                    float time = keys.Length == 0 ? 0 : keys.Last().time + 1;
                    if (keys.Length >= 2)
                    {
                        int widest = Enumerable.Range(0, keys.Length - 1)
                            .OrderByDescending(i => keys[i + 1].time - keys[i].time).First();
                        time = (keys[widest].time + keys[widest + 1].time) * .5f;
                    }
                    var list = keys.ToList();
                    list.Add(new HoFaceCurveKey(time, HoFaceCurve.Transfer(row.curve, time), 1, 1));
                    row.curve = new HoFaceCurveData(list.ToArray());
                });
                ReloadEditors();
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void DeleteCurvePointClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                int index = Plot.SelectedIndex;
                if (selectedValueRow?.curve == null || index < 0) return;
                if (selectedValueRow.curve.length <= 2) throw new InvalidOperationException("至少保留两个曲线点。");
                document.Edit((value, ir) =>
                {
                    var curve = value.outputs[selectedOutputIndex].curve;
                    curve.keys = curve.keys.Where((_, i) => i != index).ToArray();
                });
                ReloadEditors();
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        private void CurveSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is CurveRow row) Plot.SelectIndex(row.Index);
        }
        private int FindOutputIndex(string parameter, int requestedIndex)
        {
            if (document.Value == null) throw new InvalidOperationException("请先打开作品。");
            if (requestedIndex >= 0)
            {
                if (requestedIndex >= document.Value.outputs.Count || document.Value.outputs[requestedIndex].parameter != parameter)
                    throw new ArgumentException("outputIndex 与参数名称不匹配。");
                return requestedIndex;
            }
            var matches = document.Value.outputs.Select((row, index) => new { row, index }).Where(x => x.row.parameter == parameter).ToArray();
            if (matches.Length != 1) throw new ArgumentException(matches.Length == 0 ? "参数不存在：" + parameter :
                "参数有多行，请提供 outputIndex：" + parameter);
            return matches[0].index;
        }
        private static float FiniteFloat(JsonElement element, string name)
        {
            if (!element.TryGetSingle(out float value) || !float.IsFinite(value))
                throw new ArgumentException(name + " 必须是有限数值。");
            return value;
        }
        private static float ReadTangent(JsonElement element) =>
            element.ValueKind == JsonValueKind.String ? ParseNumber(element.GetString(), true) : FiniteFloat(element, "tangent");
        private static float ParseNumber(string text, bool allowInfinity = false)
        {
            if (allowInfinity && (text == "Infinity" || text == "+Infinity")) return float.PositiveInfinity;
            if (allowInfinity && text == "-Infinity") return float.NegativeInfinity;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new ArgumentException("请输入有效数值（小数使用 .）。");
            return value;
        }
        public sealed class CurveRow
        {
            public int Index { get; }
            public string Time { get; set; }
            public string Value { get; set; }
            public string InTangent { get; set; }
            public string OutTangent { get; set; }
            public CurveRow(int index, HoFaceCurveKey key)
            {
                Index = index; Time = Format(key.time); Value = Format(key.value);
                InTangent = Format(key.inTangent); OutTangent = Format(key.outTangent);
            }
            private static string Format(float value) => value.ToString("G7", CultureInfo.InvariantCulture);
        }
    }
}
