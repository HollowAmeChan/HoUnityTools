using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        public ObservableCollection<ControlPointRow> ControlPoints { get; } = new ObservableCollection<ControlPointRow>();
        public ObservableCollection<ModifierRow> ModifierRows { get; } = new ObservableCollection<ModifierRow>();
        public ObservableCollection<ModifierStepRow> ModifierStepRows { get; } = new ObservableCollection<ModifierStepRow>();
        public string[] ModifierKindOptions { get; } = new[] { "平滑", "延迟", "维持", "果冻" };
        public int ValueCount => ValueRows.Count;
        public int NodeCount => NodeRows.Count;
        public bool HasValueSelection => selectedValueRow != null;
        public bool HasNodeSelection => selectedNode != null;
        public bool HasPoseSelection => selectedNode?.type == "pose";
        public bool HasSelection => HasValueSelection || HasNodeSelection;
        public string SelectionTitle => CenterTab == 2 ? "原始输入 · Value · IR" :
            CenterTab == 3 ? "Value + IR JSON" : selectedValueRow?.parameter ??
            (string.IsNullOrWhiteSpace(selectedNode?.name) ? selectedNode?.id : selectedNode.name) ?? "未选择";
        public string SelectionKind => CenterTab == 2 ? "REALTIME / 三层参数监控" :
            CenterTab == 3 ? "SOURCE / VALUE + IR JSON" :
            HasValueSelection ? "VALUE / 响应曲线" : selectedNode?.type?.ToUpperInvariant() ?? "作品编辑器";
        public bool CanFitPlot => CenterTab < 2;
        public string WeightSectionTitle => selectedNode?.type == "pose" ? "姿势权重" :
            selectedNode?.type == "blend1d" ? "子项阈值" : selectedNode?.type == "blend2d" ? "控制点坐标" : "子项权重";
        public bool HasAxisX => selectedNode?.type == "blend1d" || selectedNode?.type == "blend2d";
        public bool HasAxisY => selectedNode?.type == "blend2d";
        public bool HasWeightSelection => selectedNode?.type == "direct";
        private string draftName = "", draftExpression = "", draftAxisX = "", draftAxisY = "";
        private int selectedModifierIndex = -1, selectedModifierStepIndex = -1;
        private bool loadingModifier;
        private string modifierKindDraft = "", modifierSecondsDraft = "0";
        public int SelectedModifierIndex
        {
            get => selectedModifierIndex;
            set
            {
                if (selectedModifierIndex == value) return;
                selectedModifierIndex = value;
                RefreshModifierDraft();
            }
        }
        public int SelectedModifierStepIndex
        {
            get => selectedModifierStepIndex;
            set
            {
                if (selectedModifierStepIndex == value) return;
                selectedModifierStepIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeleteModifierStep));
            }
        }
        public string ModifierKindDraft
        {
            get => modifierKindDraft;
            private set { if (modifierKindDraft != value) { modifierKindDraft = value; OnPropertyChanged(); } }
        }
        public string ModifierSecondsDraft
        {
            get => modifierSecondsDraft;
            set { if (modifierSecondsDraft != value) { modifierSecondsDraft = value; OnPropertyChanged(); } }
        }
        private string modifierStrengthDraft = "0";
        private string modifierFrequencyDraft = "6";
        private string modifierDampingDraft = "0.35";
        public string ModifierStrengthDraft
        {
            get => modifierStrengthDraft;
            set { if (modifierStrengthDraft != value) { modifierStrengthDraft = value; OnPropertyChanged(); } }
        }
        public string ModifierFrequencyDraft
        {
            get => modifierFrequencyDraft;
            set { if (modifierFrequencyDraft != value) { modifierFrequencyDraft = value; OnPropertyChanged(); } }
        }
        public string ModifierDampingDraft
        {
            get => modifierDampingDraft;
            set { if (modifierDampingDraft != value) { modifierDampingDraft = value; OnPropertyChanged(); } }
        }
        private HoFaceModifier SelectedModifier =>
            selectedValueRow?.modifiers != null && selectedModifierIndex >= 0 && selectedModifierIndex < selectedValueRow.modifiers.Count
                ? selectedValueRow.modifiers[selectedModifierIndex] : null;
        public bool HasSelectedModifier => SelectedModifier != null;
        public Visibility ModifierEmptyVisibility => HasValueSelection && ModifierRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedModifierVisibility => HasSelectedModifier ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedModifierSecondsVisibility => SelectedModifier?.kind is HoFaceModifierKind.Steps or HoFaceModifierKind.Jelly
            ? Visibility.Collapsed : HasSelectedModifier ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedModifierStepsVisibility => SelectedModifier?.kind == HoFaceModifierKind.Steps
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedModifierJellyVisibility => SelectedModifier?.kind == HoFaceModifierKind.Jelly
            ? Visibility.Visible : Visibility.Collapsed;
        public string ModifierOrderSummary => HasSelectedModifier
            ? (selectedModifierIndex + 1) + " / " + ModifierRows.Count : "";
        public bool CanMoveModifierUp => HasSelectedModifier && selectedModifierIndex > 0;
        public bool CanMoveModifierDown => HasSelectedModifier && selectedModifierIndex < ModifierRows.Count - 1;
        public bool CanDeleteModifierStep => SelectedModifier?.kind == HoFaceModifierKind.Steps &&
            selectedModifierStepIndex >= 0 && selectedModifierStepIndex < (SelectedModifier.steps?.Count ?? 0);
        private bool loadingInspector;
        public bool HasPropertyDraft => !loadingInspector && HasSelection &&
            (DraftName != (selectedValueRow?.parameter ?? selectedNode?.name ?? "") ||
            HasValueSelection && DraftExpression != (selectedValueRow.expression ?? "") ||
            HasAxisX && DraftAxisX != (selectedNode.parameterX ?? "") || HasAxisY && DraftAxisY != (selectedNode.parameterY ?? ""));
        public bool CanEditGraph => HasDocument && !HasTextDraft && !HasPropertyDraft;
        public bool CanEditProperties => HasSelection && !HasTextDraft;
        public string DraftName { get => draftName; set { draftName = value; PropertyDraftChanged(); OnPropertyChanged(); } }
        public string DraftExpression { get => draftExpression; set { draftExpression = value; PropertyDraftChanged(); OnPropertyChanged(); } }
        public string DraftAxisX { get => draftAxisX; set { draftAxisX = value; PropertyDraftChanged(); OnPropertyChanged(); } }
        public string DraftAxisY { get => draftAxisY; set { draftAxisY = value; PropertyDraftChanged(); OnPropertyChanged(); } }
        private void PropertyDraftChanged()
        {
            if (loadingInspector) return;
            foreach (string name in new[] { nameof(HasPropertyDraft), nameof(CanEditGraph), nameof(CanEditProperties), nameof(HasUnsavedChanges), nameof(HeaderStatus) })
                OnPropertyChanged(name);
            CommandManager.InvalidateRequerySuggested();
        }
        private void RefreshInspector()
        {
            loadingInspector = true;
            DraftName = selectedValueRow?.parameter ?? selectedNode?.name ?? "";
            DraftExpression = selectedValueRow?.expression ?? "";
            DraftAxisX = selectedNode?.parameterX ?? "";
            DraftAxisY = selectedNode?.parameterY ?? "";
            CurveRows.Clear();
            ControlPoints.Clear();
            var keys = selectedValueRow?.curve?.keys;
            if (keys != null)
                for (int i = 0; i < keys.Length; i++) CurveRows.Add(new CurveRow(i, keys[i]));
            if (HasAxisX)
                for (int i = 0; i < selectedNode.children.Count; i++)
                {
                    var child = selectedNode.children[i];
                    ControlPoints.Add(new ControlPointRow(i, NodeAlias(child.node), child.node,
                        HasAxisY ? child.x : child.threshold, child.y));
                }
            SelectedControlPointIndex = -1;
            if (selectedPoseWeightIndex >= InspectorWeights.Count) SelectedPoseWeightIndex = -1;
            ControlPointXColumn.Header = HasAxisY ? "X" : "阈值";
            ControlPointYColumn.Visibility = HasAxisY ? Visibility.Visible : Visibility.Collapsed;
            loadingInspector = false;
            RefreshModifierRows();
            if (Plot.SelectedIndex >= 0 && Plot.SelectedIndex < CurveRows.Count) CurveGrid.SelectedItem = CurveRows[Plot.SelectedIndex];
            PropertyDraftChanged();
            foreach (string name in new[] { nameof(HasValueSelection), nameof(HasNodeSelection), nameof(HasPoseSelection), nameof(HasSelection),
                nameof(SelectionTitle), nameof(SelectionKind), nameof(WeightSectionTitle), nameof(HasAxisX), nameof(HasAxisY),
                nameof(HasWeightSelection), nameof(BlendChildrenTitle), nameof(PoseWeightEmptyVisibility), nameof(PoseWeightEmptyText),
                nameof(CanAddPoseRule), nameof(CanAddBlendRule), nameof(CanDeleteSelectedNode), nameof(ShowIrGraphActions) })
                OnPropertyChanged(name);
        }
        private void RefreshModifierRows()
        {
            ModifierRows.Clear();
            if (selectedValueRow?.modifiers != null)
                for (int i = 0; i < selectedValueRow.modifiers.Count; i++)
                    ModifierRows.Add(new ModifierRow(i, selectedValueRow.modifiers[i]));
            if (ModifierRows.Count == 0) selectedModifierIndex = -1;
            else if (selectedModifierIndex < 0 || selectedModifierIndex >= ModifierRows.Count) selectedModifierIndex = 0;
            OnPropertyChanged(nameof(SelectedModifierIndex));
            RefreshModifierDraft();
        }
        private void RefreshModifierDraft()
        {
            loadingModifier = true;
            var modifier = SelectedModifier;
            ModifierKindDraft = modifier == null ? "" : ModifierLabel(modifier.kind);
            ModifierSecondsDraft = modifier == null ? "0" :
                modifier.seconds.ToString("G7", CultureInfo.InvariantCulture);
            ModifierStrengthDraft = modifier == null ? "0" : modifier.strength.ToString("G7", CultureInfo.InvariantCulture);
            ModifierFrequencyDraft = modifier == null ? "6" : modifier.frequency.ToString("G7", CultureInfo.InvariantCulture);
            ModifierDampingDraft = modifier == null ? "0.35" : modifier.damping.ToString("G7", CultureInfo.InvariantCulture);
            ModifierStepRows.Clear();
            if (modifier?.steps != null)
                for (int i = 0; i < modifier.steps.Count; i++)
                    ModifierStepRows.Add(new ModifierStepRow(i, modifier.steps[i]));
            if (selectedModifierStepIndex >= ModifierStepRows.Count) selectedModifierStepIndex = -1;
            loadingModifier = false;
            foreach (string name in new[] { nameof(HasSelectedModifier), nameof(ModifierEmptyVisibility),
                nameof(SelectedModifierVisibility), nameof(SelectedModifierSecondsVisibility),
                nameof(SelectedModifierStepsVisibility), nameof(SelectedModifierJellyVisibility),
                nameof(ModifierOrderSummary), nameof(CanMoveModifierUp),
                nameof(CanMoveModifierDown), nameof(CanDeleteModifierStep),
                nameof(SelectedModifierStepIndex) })
                OnPropertyChanged(name);
        }
        private void ReloadEditorsKeepingModifier(int modifierIndex, int stepIndex = -1)
        {
            ReloadEditors();
            SelectedModifierIndex = modifierIndex >= 0 && modifierIndex < ModifierRows.Count ? modifierIndex : -1;
            SelectedModifierStepIndex = stepIndex >= 0 && stepIndex < ModifierStepRows.Count ? stepIndex : -1;
        }
        private void TextDraftChanged()
        {
            if (loadingText) return;
            OnPropertyChanged(nameof(HasTextDraft));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HeaderStatus));
            OnPropertyChanged(nameof(CanEditGraph));
            OnPropertyChanged(nameof(CanEditProperties));
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
            if (HasPropertyDraft) throw new InvalidOperationException("属性有待应用编辑。请先执行 applyProperties 或 revertProperties。");
        }
        private void UndoDocument()
        {
            try { CommitPropertyDraft(); SyncText(); document.Undo(); ReloadEditors(); }
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
        private void FitClicked(object sender, RoutedEventArgs e) => ActivePlot.Fit();
        private void ApplyPropertiesClicked(object sender, RoutedEventArgs e)
        {
            try { CommitPropertyDraft(); ReloadEditors(); }
            catch (Exception error) { RecoverFailedEdit(); ShowError(error.Message); }
        }
        private void CommitPropertyDraft()
        {
                if (!HasPropertyDraft) return;
                if (HasTextDraft) throw new InvalidOperationException("请先应用或还原 JSON 文本。");
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
                if (HasValueSelection) RememberValueSelection();
                ResolveSelection();
                PropertyDraftChanged();
        }
        private void ReloadEditorsKeepingDraft()
        {
            ResolveSelection();
            if (!HasTextDraft && !HasPropertyDraft) ReloadEditors();
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
        private static string ModifierLabel(HoFaceModifierKind kind) => kind switch
        {
            HoFaceModifierKind.Smooth => "平滑",
            HoFaceModifierKind.Delay => "延迟",
            HoFaceModifierKind.Steps => "维持",
            HoFaceModifierKind.Jelly => "果冻",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        private static HoFaceModifierKind ParseModifierKind(string label) => label switch
        {
            "平滑" or "smooth" => HoFaceModifierKind.Smooth,
            "延迟" or "delay" => HoFaceModifierKind.Delay,
            "维持" or "steps" => HoFaceModifierKind.Steps,
            "果冻" or "jelly" => HoFaceModifierKind.Jelly,
            _ => throw new ArgumentException("修饰器类型必须是平滑、延迟、维持或果冻。")
        };
        private void AddModifierClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (selectedValueRow == null || sender is not Button button) return;
                var kind = ParseModifierKind(button.Tag?.ToString());
                var modifier = new HoFaceModifier
                {
                    kind = kind,
                    seconds = kind == HoFaceModifierKind.Smooth ? .08f : kind == HoFaceModifierKind.Delay ? .05f : 0,
                    strength = kind == HoFaceModifierKind.Jelly ? .5f : 0f,
                    steps = kind == HoFaceModifierKind.Steps
                        ? new List<HoFaceStep> { new HoFaceStep { trigger = .5f, target = 1, threshold = .05f } }
                        : new List<HoFaceStep>()
                };
                int outputIndex = selectedOutputIndex;
                document.Edit((value, ir) =>
                {
                    value.outputs[outputIndex].modifiers ??= new List<HoFaceModifier>();
                    value.outputs[outputIndex].modifiers.Add(modifier);
                });
                int modifierIndex = selectedValueRow.modifiers.Count - 1;
                ReloadEditorsKeepingModifier(modifierIndex, kind == HoFaceModifierKind.Steps ? 0 : -1);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void DeleteModifierClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (SelectedModifier == null) return;
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex;
                document.Edit((value, ir) => value.outputs[outputIndex].modifiers.RemoveAt(modifierIndex));
                ReloadEditorsKeepingModifier(Math.Min(modifierIndex, selectedValueRow.modifiers.Count - 1));
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void MoveModifierClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (sender is not Button button || SelectedModifier == null) return;
                int direction = button.Tag?.ToString() == "up" ? -1 : 1;
                int from = selectedModifierIndex, to = from + direction;
                if (to < 0 || to >= selectedValueRow.modifiers.Count) return;
                int outputIndex = selectedOutputIndex;
                document.Edit((value, ir) =>
                {
                    var chain = value.outputs[outputIndex].modifiers;
                    var modifier = chain[from];
                    chain.RemoveAt(from);
                    chain.Insert(to, modifier);
                });
                ReloadEditorsKeepingModifier(to, selectedModifierStepIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void ModifierKindChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loadingModifier || sender is not ComboBox combo || SelectedModifier == null) return;
            try
            {
                EnsureNoDraft();
                var kind = ParseModifierKind(combo.SelectedItem?.ToString());
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex;
                document.Edit((value, ir) =>
                {
                    var modifier = value.outputs[outputIndex].modifiers[modifierIndex];
                    modifier.kind = kind;
                    if (kind == HoFaceModifierKind.Steps)
                    {
                        modifier.steps ??= new List<HoFaceStep>();
                        if (modifier.steps.Count == 0)
                            modifier.steps.Add(new HoFaceStep { trigger = .5f, target = 1, threshold = .05f });
                    }
                });
                ReloadEditorsKeepingModifier(modifierIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void ModifierSecondsLostFocus(object sender, RoutedEventArgs e)
        {
            if (loadingModifier || SelectedModifier == null ||
                SelectedModifier.kind is HoFaceModifierKind.Steps or HoFaceModifierKind.Jelly) return;
            try
            {
                EnsureNoDraft();
                float seconds = ParseNumber(ModifierSecondsDraft);
                if (seconds < 0) throw new ArgumentException("时长不能为负数。");
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex;
                document.Edit((value, ir) => value.outputs[outputIndex].modifiers[modifierIndex].seconds = seconds);
                ReloadEditorsKeepingModifier(modifierIndex, selectedModifierStepIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void ModifierJellyLostFocus(object sender, RoutedEventArgs e)
        {
            if (loadingModifier || SelectedModifier == null || SelectedModifier.kind != HoFaceModifierKind.Jelly) return;
            if (sender is not FrameworkElement element) return;
            string field = element.Tag?.ToString();
            try
            {
                EnsureNoDraft();
                float entered = ParseNumber(field switch
                {
                    "strength" => ModifierStrengthDraft,
                    "frequency" => ModifierFrequencyDraft,
                    _ => ModifierDampingDraft
                });
                if (field == "strength" && (entered < 0f || entered > 1f))
                    throw new ArgumentException("果冻强度必须在 0…1 之间。");
                if (field == "frequency" && (entered < 0.1f || entered > 30f))
                    throw new ArgumentException("果冻频率必须在 0.1…30 Hz 之间。");
                if (field == "damping" && (entered < 0f || entered > 4f))
                    throw new ArgumentException("果冻阻尼必须在 0…4 之间。");
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex;
                document.Edit((value, ir) =>
                {
                    var modifier = value.outputs[outputIndex].modifiers[modifierIndex];
                    if (field == "strength") modifier.strength = entered;
                    else if (field == "frequency") modifier.frequency = entered;
                    else modifier.damping = entered;
                });
                ReloadEditorsKeepingModifier(modifierIndex, selectedModifierStepIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void AddModifierStepClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (SelectedModifier?.kind != HoFaceModifierKind.Steps) return;
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex;
                int stepIndex = SelectedModifier.steps?.Count ?? 0;
                document.Edit((value, ir) =>
                {
                    var modifier = value.outputs[outputIndex].modifiers[modifierIndex];
                    modifier.steps ??= new List<HoFaceStep>();
                    float trigger = modifier.steps.Count == 0 ? .5f : modifier.steps[modifier.steps.Count - 1].trigger + .1f;
                    modifier.steps.Add(new HoFaceStep { trigger = trigger, target = 1, threshold = .05f });
                });
                ReloadEditorsKeepingModifier(modifierIndex, stepIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void DeleteModifierStepClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (SelectedModifier?.kind != HoFaceModifierKind.Steps ||
                    selectedModifierStepIndex < 0 || selectedModifierStepIndex >= SelectedModifier.steps.Count) return;
                int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex, stepIndex = selectedModifierStepIndex;
                document.Edit((value, ir) => value.outputs[outputIndex].modifiers[modifierIndex].steps.RemoveAt(stepIndex));
                ReloadEditorsKeepingModifier(modifierIndex);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void ModifierStepCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not ModifierStepRow row) return;
            string property = e.Column.SortMemberPath;
            string text = (e.EditingElement as TextBox)?.Text;
            int outputIndex = selectedOutputIndex, modifierIndex = selectedModifierIndex, stepIndex = row.Index;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureNoDraft();
                    float number = ParseNumber(text);
                    if ((property == "Hold" || property == "Threshold" || property == "EnterSeconds" || property == "ExitSeconds") &&
                        number < 0) throw new ArgumentException("保持、回差、进入和退出时长不能为负数。");
                    document.Edit((value, ir) =>
                    {
                        var steps = value.outputs[outputIndex].modifiers[modifierIndex].steps;
                        var step = steps[stepIndex];
                        switch (property)
                        {
                            case "Trigger":
                                if (stepIndex > 0 && steps[stepIndex - 1].trigger >= number ||
                                    stepIndex + 1 < steps.Count && steps[stepIndex + 1].trigger <= number)
                                    throw new ArgumentException("维持步骤按触发值严格递增排列。");
                                step.trigger = number; break;
                            case "Target": step.target = number; break;
                            case "Hold": step.hold = number; break;
                            case "Threshold": step.threshold = number; break;
                            case "EnterSeconds": step.enterSeconds = number; break;
                            case "ExitSeconds": step.exitSeconds = number; break;
                            default: throw new ArgumentException("未知维持步骤属性。");
                        }
                        steps[stepIndex] = step;
                    });
                    ReloadEditorsKeepingModifier(modifierIndex, stepIndex);
                }
                catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
            }));
        }
        private void ControlPointSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ControlPointRow row) BlendPlot.SelectIndex(row.Index);
        }
        private void ControlPointCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not ControlPointRow row) return;
            string nodeId = selectedNodeId;
            string property = e.Column.SortMemberPath == "Y" ? "y" : HasAxisY ? "x" : "threshold";
            string text = (e.EditingElement as TextBox)?.Text;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { EnsureNoDraft(); EditIrChild(nodeId, row.NodeId, -1, property, ParseNumber(text)); }
                catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
            }));
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
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out float value) || !float.IsFinite(value))
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
        public sealed class ControlPointRow
        {
            public int Index { get; }
            public string ChildId { get; }
            public string NodeId { get; }
            public string X { get; set; }
            public string Y { get; set; }
            public ControlPointRow(int index, string childName, string nodeId, float x, float y)
            {
                Index = index; ChildId = childName; NodeId = nodeId;
                X = x.ToString("G7", CultureInfo.InvariantCulture);
                Y = y.ToString("G7", CultureInfo.InvariantCulture);
            }
        }
        public sealed class ModifierRow
        {
            public int Index { get; }
            public string Display { get; }
            public ModifierRow(int index, HoFaceModifier modifier)
            {
                Index = index;
                if (modifier == null) { Display = (index + 1) + " · 无效修饰器"; return; }
                string kind = ModifierLabel(modifier.kind);
                string detail = modifier.kind switch
                {
                    HoFaceModifierKind.Steps => (modifier.steps?.Count ?? 0) + " 步",
                    HoFaceModifierKind.Jelly => "强度 " + modifier.strength.ToString("0.##", CultureInfo.InvariantCulture)
                        + " · " + modifier.frequency.ToString("0.##", CultureInfo.InvariantCulture) + " Hz",
                    _ => modifier.seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s"
                };
                Display = (index + 1) + " · " + kind + " · " + detail;
            }
        }
        public sealed class ModifierStepRow
        {
            public int Index { get; }
            public string Trigger { get; set; }
            public string Target { get; set; }
            public string Hold { get; set; }
            public string Threshold { get; set; }
            public string EnterSeconds { get; set; }
            public string ExitSeconds { get; set; }
            public ModifierStepRow(int index, HoFaceStep step)
            {
                Index = index;
                Trigger = Format(step.trigger);
                Target = Format(step.target);
                Hold = Format(step.hold);
                Threshold = Format(step.threshold);
                EnterSeconds = Format(step.enterSeconds);
                ExitSeconds = Format(step.exitSeconds);
            }
            private static string Format(float value) => value.ToString("G7", CultureInfo.InvariantCulture);
        }
    }
}
