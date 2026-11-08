using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public partial class MainWindow
    {
        private ICollectionView rawMonitorView, valueMonitorView, irMonitorView;
        private string monitorFilter = "";
        private int monitorStageTab;
        private AuthoringMonitorFrame latestMonitorFrame;
        public ObservableCollection<MonitorParameterRow> RawMonitorRows { get; } = new ObservableCollection<MonitorParameterRow>();
        public ObservableCollection<MonitorParameterRow> ValueMonitorRows { get; } = new ObservableCollection<MonitorParameterRow>();
        public ObservableCollection<MonitorParameterRow> IrMonitorRows { get; } = new ObservableCollection<MonitorParameterRow>();
        public ICollectionView RawMonitorView => rawMonitorView;
        public ICollectionView ValueMonitorView => valueMonitorView;
        public ICollectionView IrMonitorView => irMonitorView;
        public string MonitorFilter
        {
            get => monitorFilter;
            set
            {
                monitorFilter = value ?? "";
                rawMonitorView?.Refresh(); valueMonitorView?.Refresh(); irMonitorView?.Refresh();
                OnPropertyChanged();
                NotifyMonitorState();
            }
        }
        public string MonitorStatus => latestMonitorFrame == null ? "等待 VTS 数据" :
            latestMonitorFrame.Tracked
                ? (session.ReplayPlaying ? "回放" : "实时") + " · " + latestMonitorFrame.Time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s"
                : "未追踪 · 保留最后一帧参数";
        public int RawMonitorCount => RawMonitorView?.Cast<object>().Count() ?? 0;
        public int ValueMonitorCount => ValueMonitorView?.Cast<object>().Count() ?? 0;
        public int IrMonitorCount => IrMonitorView?.Cast<object>().Count() ?? 0;
        public bool HasRawMonitorOverrides => session.RawOverrides.Count != 0;
        public bool HasValueMonitorOverrides => session.ValueOverrides.Count != 0;
        public bool HasIrMonitorOverrides => session.IrOverrides.Count != 0;
        public int MonitorStageTab
        {
            get => monitorStageTab;
            private set
            {
                if (monitorStageTab == value) return;
                monitorStageTab = value;
                OnPropertyChanged(nameof(IsRawMonitorTab));
                OnPropertyChanged(nameof(IsValueMonitorTab));
                OnPropertyChanged(nameof(IsIrMonitorTab));
            }
        }
        public bool IsRawMonitorTab => MonitorStageTab == 0;
        public bool IsValueMonitorTab => MonitorStageTab == 1;
        public bool IsIrMonitorTab => MonitorStageTab == 2;
        public Visibility MonitorWorkspaceVisibility => CenterTab == 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RawMonitorEmptyVisibility => RawMonitorCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ValueMonitorEmptyVisibility => ValueMonitorCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IrMonitorEmptyVisibility => IrMonitorCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        public string RawMonitorEmptyText => RawMonitorRows.Count == 0 ? "连接 VTS 后显示原始输入" : "没有匹配的参数";
        public string ValueMonitorEmptyText => HasDocument ? "没有匹配的参数" : "打开作品后显示 Value 参数";
        public string IrMonitorEmptyText => HasDocument ? "没有匹配的参数" : "打开作品后显示 IR 权重";
        public Visibility NoDocumentMonitorVisibility => HasDocument ? Visibility.Collapsed : Visibility.Visible;
        public bool CanConnect => session.Running || Mode == "RawForward" || HasDocument;
        public bool CanRecord => HasDocument || Mode == "RawForward";
        public bool CanUseReplay => HasDocument || Mode == "RawForward";

        private void MonitorNavigationClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int tab) && tab >= 0 && tab <= 3)
                CenterTab = tab;
        }

        private void MonitorStageNavigationClicked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button && int.TryParse(button.Tag?.ToString(), out int stage) && stage >= 0 && stage <= 2)
            {
                MonitorStageTab = stage;
                CenterTab = 2;
            }
        }

        private void InitializeMonitorViews()
        {
            rawMonitorView = CollectionViewSource.GetDefaultView(RawMonitorRows);
            valueMonitorView = CollectionViewSource.GetDefaultView(ValueMonitorRows);
            irMonitorView = CollectionViewSource.GetDefaultView(IrMonitorRows);
            foreach (var view in new[] { rawMonitorView, valueMonitorView, irMonitorView })
                view.Filter = item => item is MonitorParameterRow row &&
                    (string.IsNullOrWhiteSpace(MonitorFilter) ||
                     (row.Label + " " + row.Key).IndexOf(MonitorFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            OnPropertyChanged(nameof(RawMonitorView)); OnPropertyChanged(nameof(ValueMonitorView));
            OnPropertyChanged(nameof(IrMonitorView));
            NotifyMonitorState();
        }

        private void ResetMonitorRows()
        {
            RawMonitorRows.Clear(); ValueMonitorRows.Clear(); IrMonitorRows.Clear();
            latestMonitorFrame = null;
            BlendPlot.ClearLivePoint();
            if (document.Value != null)
            {
                foreach (var input in document.Value.inputs)
                {
                    if (input == null || string.IsNullOrWhiteSpace(input.expression) ||
                        !HoFaceExpression.TryParse(input.expression, out var expression, out _)) continue;
                    var names = new System.Collections.Generic.List<string>();
                    expression.CollectVariables(names);
                    foreach (string name in names.Distinct(StringComparer.Ordinal))
                        if (IsDisplayableRawParameter(name))
                            EnsureMonitorRow(RawMonitorRows, MonitorStage.Raw, name, name, 0);
                }
                foreach (var output in document.Value.outputs)
                {
                    if (output == null || string.IsNullOrWhiteSpace(output.parameter)) continue;
                    EnsureMonitorRow(ValueMonitorRows, MonitorStage.Value, output.parameter, output.parameter, output.defaultValue);
                }
                foreach (var target in document.Ir.targets)
                    EnsureMonitorRow(IrMonitorRows, MonitorStage.Ir, target.id, target.id, 0, target.min, target.max);
            }
            foreach (var pair in session.RawOverrides)
                if (IsDisplayableRawParameter(pair.Key))
                    EnsureMonitorRow(RawMonitorRows, MonitorStage.Raw, pair.Key, pair.Key, pair.Value).SetPinnedValue(pair.Value);
            foreach (var pair in session.ValueOverrides)
                ValueMonitorRows.FirstOrDefault(x => x.Key == pair.Key)?.SetPinnedValue(pair.Value);
            foreach (var pair in session.IrOverrides)
                IrMonitorRows.FirstOrDefault(x => x.Key == pair.Key)?.SetPinnedValue(pair.Value);
            NotifyMonitorState();
        }

        private static string ValueOutputKey(string parameter)
        {
            const string prefix = "ARKit/";
            if (!parameter.StartsWith(prefix, StringComparison.Ordinal)) return parameter;
            string name = parameter.Substring(prefix.Length);
            int channel = HoFaceTrackingChannels.IndexOf(name);
            return channel >= 0 ? HoFaceTrackingChannels.Names[channel] : name;
        }

        private static MonitorParameterRow EnsureMonitorRow(ObservableCollection<MonitorParameterRow> rows,
            MonitorStage stage, string key, string label, float initial, float minimum = -1, float maximum = 1)
        {
            var row = rows.FirstOrDefault(x => x.Key == key);
            if (row != null) return row;
            row = new MonitorParameterRow(stage, key, label, initial, minimum, maximum);
            rows.Add(row);
            return row;
        }

        private void UpdateRealtimeMonitor(AuthoringMonitorFrame frame)
        {
            latestMonitorFrame = frame;
            UpdateMonitorStage(RawMonitorRows, DisplayableRaw(frame.Raw), DisplayableRaw(frame.RawEffective),
                DisplayableRaw(session.RawOverrides), MonitorStage.Raw);
            UpdateValueMonitor(frame.Values, frame.ValuesEffective);
            UpdateMonitorStage(IrMonitorRows, frame.Weights, frame.WeightsEffective, session.IrOverrides, MonitorStage.Ir);
            RefreshBlendPlotLiveState();
            NotifyMonitorState();
        }

        private void RefreshBlendPlotLiveState()
        {
            var node = BlendPlot.Node;
            if (node == null || (node.type != "blend1d" && node.type != "blend2d") || !HasDocument)
            {
                BlendPlot.ClearLivePoint();
                return;
            }

            if (latestMonitorFrame == null || latestMonitorFrame.ValuesEffective == null ||
                latestMonitorFrame.ValuesEffective.Count == 0)
            {
                BlendPlot.ClearLivePoint();
                return;
            }
            var values = latestMonitorFrame.ValuesEffective;

            float Read(string name)
            {
                if (!string.IsNullOrWhiteSpace(name) && values.TryGetValue(name, out float value) && float.IsFinite(value))
                    return value;
                var parameter = document.Ir.parameters.FirstOrDefault(x => x.name == name);
                return parameter?.defaultValue ?? 0;
            }

            BlendPlot.SetLivePoint(
                Read(node.parameterX),
                node.type == "blend2d" ? Read(node.parameterY) : 0,
                session.GetEffectiveChildWeights(node.id),
                latestMonitorFrame?.Tracked ?? false);
        }

        private static bool IsDisplayableRawParameter(string key) =>
            key != HoVtsPacket.TimestampKey && key != HoVtsPacket.FaceFoundKey && key != HoVtsPacket.HotkeyKey;

        private static System.Collections.Generic.IReadOnlyDictionary<string, float> DisplayableRaw(
            System.Collections.Generic.IReadOnlyDictionary<string, float> values)
        {
            if (values == null) return null;
            var result = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var pair in values)
                if (IsDisplayableRawParameter(pair.Key)) result[pair.Key] = pair.Value;
            return result;
        }

        private void UpdateValueMonitor(
            System.Collections.Generic.IReadOnlyDictionary<string, float> live,
            System.Collections.Generic.IReadOnlyDictionary<string, float> effective)
        {
            foreach (var row in ValueMonitorRows)
            {
                string key = ValueOutputKey(row.Key);
                float? liveValue = live != null && live.TryGetValue(key, out float source) ? source : (float?)null;
                float? effectiveValue = effective != null && effective.TryGetValue(key, out float result) ? result : (float?)null;
                float? pinned = session.ValueOverrides.TryGetValue(row.Key, out float fixedValue) ? fixedValue : (float?)null;
                if (IsManuallyOverridden(key)) pinned = parameterOverrides[key];
                row.Update(liveValue, effectiveValue, pinned);
            }
        }

        private static void UpdateMonitorStage(ObservableCollection<MonitorParameterRow> rows,
            System.Collections.Generic.IReadOnlyDictionary<string, float> live,
            System.Collections.Generic.IReadOnlyDictionary<string, float> effective,
            System.Collections.Generic.IReadOnlyDictionary<string, float> overrides,
            MonitorStage stage)
        {
            var keys = (live?.Keys ?? Array.Empty<string>())
                .Concat(effective?.Keys ?? Array.Empty<string>())
                .Concat(overrides?.Keys ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).ToArray();
            foreach (string key in keys) EnsureMonitorRow(rows, stage, key, key, 0);
            foreach (var row in rows)
            {
                float? liveValue = live != null && live.TryGetValue(row.Key, out float source) ? source : (float?)null;
                float? effectiveValue = effective != null && effective.TryGetValue(row.Key, out float result) ? result : (float?)null;
                float? pinned = overrides != null && overrides.TryGetValue(row.Key, out float fixedValue) ? fixedValue : (float?)null;
                row.Update(liveValue, effectiveValue, pinned);
            }
        }

        private void SetMonitorOverrideClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not MonitorParameterRow row) return;
            float? value = button.Tag?.ToString() switch
            {
                "-1" => -1,
                "0" => 0,
                "1" => 1,
                _ => null
            };
            session.SetMonitorOverride(row.Stage, row.Key, value);
            row.SetPinnedValue(value);
            liveStatus = "临时覆写 · 等待下一帧";
            OnPropertyChanged(nameof(LiveStatus));
            NotifyMonitorState();
        }

        private void ClearMonitorOverridesClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), out MonitorStage stage)) return;
            session.ClearMonitorOverrides(stage);
            foreach (var row in RowsFor(stage)) row.SetPinnedValue(null);
            liveStatus = "临时覆写已清除";
            OnPropertyChanged(nameof(LiveStatus));
            NotifyMonitorState();
        }

        private System.Collections.Generic.IEnumerable<MonitorParameterRow> RowsFor(MonitorStage stage) =>
            stage == MonitorStage.Raw ? RawMonitorRows :
            stage == MonitorStage.Value ? ValueMonitorRows : IrMonitorRows;

        private void NotifyMonitorState()
        {
            foreach (string property in new[] { nameof(MonitorStatus), nameof(RawMonitorCount),
                nameof(ValueMonitorCount), nameof(IrMonitorCount), nameof(HasRawMonitorOverrides),
                nameof(HasValueMonitorOverrides), nameof(HasIrMonitorOverrides), nameof(RawMonitorEmptyVisibility),
                nameof(ValueMonitorEmptyVisibility), nameof(IrMonitorEmptyVisibility), nameof(RawMonitorEmptyText),
                nameof(ValueMonitorEmptyText), nameof(IrMonitorEmptyText), nameof(NoDocumentMonitorVisibility), nameof(CanConnect),
                nameof(CanRecord), nameof(CanUseReplay) })
                OnPropertyChanged(property);
        }

        private static MonitorStage ReadMonitorStage(JsonElement payload)
        {
            string value = RequiredString(payload, "stage");
            if (string.Equals(value, "raw", StringComparison.OrdinalIgnoreCase)) return MonitorStage.Raw;
            if (string.Equals(value, "value", StringComparison.OrdinalIgnoreCase)) return MonitorStage.Value;
            if (string.Equals(value, "ir", StringComparison.OrdinalIgnoreCase)) return MonitorStage.Ir;
            throw new ArgumentException("stage 必须是 raw、value 或 ir。");
        }

        private object ReadMonitor()
        {
            object Rows(MonitorStage stage) => RowsFor(stage).Select(x => new
            {
                key = x.Key, label = x.Label, live = x.LiveValue, value = x.Value,
                minimum = x.Minimum, maximum = x.Maximum, fresh = x.IsFresh,
                overridden = x.IsOverridden, overrideValue = x.OverrideValue
            }).ToArray();
            return new
            {
                status = MonitorStatus,
                source = latestMonitorFrame == null ? "idle" :
                    session.ReplayPlaying ? "replay" : session.Running ? "vts" : "idle",
                mode = Mode, hasDocument = HasDocument,
                raw = Rows(MonitorStage.Raw), values = Rows(MonitorStage.Value), weights = Rows(MonitorStage.Ir)
            };
        }

        private object SetMonitorOverrideFromAutomation(JsonElement payload)
        {
            MonitorStage stage = ReadMonitorStage(payload);
            string key = RequiredString(payload, "parameter");
            if (stage != MonitorStage.Raw && !HasDocument)
                throw new InvalidOperationException("value 与 IR 阶段的临时覆写需要先打开作品。");
            float? value = RequiredString(payload, "mode") switch
            {
                "auto" => null,
                "-1" => -1,
                "0" => 0,
                "1" => 1,
                var mode => throw new ArgumentException("mode 必须是 auto、-1、0 或 1；收到：" + mode)
            };
            var row = RowsFor(stage).FirstOrDefault(x => x.Key == key);
            if (row == null)
            {
                if (stage != MonitorStage.Raw || !IsDisplayableRawParameter(key))
                    throw new ArgumentException("此阶段没有该参数：" + key);
                row = EnsureMonitorRow(RawMonitorRows, stage, key, key, value ?? 0);
            }
            session.SetMonitorOverride(stage, key, value);
            row.SetPinnedValue(value);
            NotifyMonitorState();
            return ReadMonitor();
        }

        private object ClearMonitorOverridesFromAutomation(JsonElement payload)
        {
            MonitorStage? stage = payload.TryGetProperty("stage", out _) ? ReadMonitorStage(payload) : null;
            session.ClearMonitorOverrides(stage);
            foreach (var row in stage.HasValue ? RowsFor(stage.Value) :
                RawMonitorRows.Concat(ValueMonitorRows).Concat(IrMonitorRows))
                row.SetPinnedValue(null);
            NotifyMonitorState();
            return ReadMonitor();
        }
    }
}
