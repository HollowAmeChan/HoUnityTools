using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public partial class MainWindow
    {
        private Dictionary<string, float> manualValues, manualWeights;
        private Dictionary<string, float> parameterOverrides;
        public bool HasManualPreview => parameterOverrides != null;
        private void ClearManualPreview()
        {
            parameterOverrides = null; manualValues = null; manualWeights = null;
            Plot.CursorX = Plot.CursorY = 0; Plot.InvalidateVisual();
            if (session.Preview.Running) session.Preview.Send(session.Values, session.Weights, false, 0);
            liveStatus = session.TransportStatus;
            OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(HasManualPreview));
        }
        private void ClearManualPreviewClicked(object sender, RoutedEventArgs e) => ClearManualPreview();
        private void ApplyParameterOverrides(IReadOnlyDictionary<string, float> current)
        {
            if (parameterOverrides == null) return;
            var values = current == null ? new Dictionary<string, float>() : new Dictionary<string, float>(current);
            foreach (var parameter in document.Ir.parameters)
                if (!values.ContainsKey(parameter.name)) values.Add(parameter.name, parameter.defaultValue);
            foreach (var pair in parameterOverrides) values[pair.Key] = pair.Value;
            var evaluator = new HoFaceIrEvaluator(document.Ir);
            evaluator.Evaluate(values);
            manualValues = values; manualWeights = new Dictionary<string, float>(evaluator.Values);
        }
        private bool closingWithoutPrompt;
        private bool shutdownRequested;
        internal void AutomationResponseSent()
        {
            if (shutdownRequested) Close();
        }
        private object ReadDocument()
        {
            var snapshot = document.Snapshot();
            using var value = JsonDocument.Parse(snapshot.Value);
            using var ir = JsonDocument.Parse(snapshot.Ir);
            return new
            {
                value = value.RootElement.Clone(), ir = ir.RootElement.Clone(),
                valueText = snapshot.Value, irText = snapshot.Ir, textDraft = HasTextDraft,
                draftValueText = HasTextDraft ? ValueText.Text : null,
                draftIrText = HasTextDraft ? IrText.Text : null
            };
        }
        private object ReadItems(string filter)
        {
            bool Match(string text) => string.IsNullOrWhiteSpace(filter) || text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            return new
            {
                inputs = document.Value.inputs.Select((row, index) => new
                {
                    index, parameter = row.parameter, expression = row.expression,
                    defaultValue = row.defaultValue, curve = ReadCurve(row)
                }).Where(x => Match(x.parameter + " " + x.expression)).ToArray(),
                outputs = document.Value.outputs.Select((row, index) => new
                {
                    index, parameter = row.parameter, expression = row.expression,
                    defaultValue = row.defaultValue, curve = ReadCurve(row)
                }).Where(x => Match(x.parameter + " " + x.expression)).ToArray(),
                nodes = document.Ir.nodes.Where(n => Match(n.id + " " + n.name + " " + n.type + " " + n.parameterX + " " + n.parameterY))
                    .Select(n => new { id = n.id, name = n.name, type = n.type, parameterX = n.parameterX,
                        parameterY = n.parameterY, children = n.children.Select(c => new { node = c.node, x = c.x, y = c.y,
                            threshold = c.threshold, weight = c.weight }).ToArray(),
                        weights = n.weights.Select(w => new { target = w.target, value = w.value }).ToArray() }).ToArray(),
                parameters = document.Ir.parameters.Select(p => new { p.name, p.defaultValue, p.min, p.max }).ToArray(),
                targets = document.Ir.targets.Select(t => new { t.id, t.min, t.max }).ToArray()
            };
        }
        private static object[] ReadCurve(HoFaceOutput row) =>
            (row.curve?.keys ?? Array.Empty<HoFaceCurveKey>()).Select((k, i) => (object)new
            {
                index = i, time = k.time, value = k.value,
                inTangent = TangentJson(k.inTangent), outTangent = TangentJson(k.outTangent)
            }).ToArray();
        private static object TangentJson(float value) => float.IsInfinity(value) ? (object)(value > 0 ? "Infinity" : "-Infinity") : value;
        private object ReadSelection()
        {
            return new
            {
                kind = HasValueSelection ? "value" : HasNodeSelection ? "node" : null,
                parameter = selectedValueRow?.parameter,
                outputIndex = HasValueSelection ? (int?)selectedOutputIndex : null,
                expression = selectedValueRow?.expression,
                nodeId = selectedNode?.id,
                nodeType = selectedNode?.type,
                curve = selectedValueRow == null ? null : ReadCurve(selectedValueRow),
                pointIndex = Plot.SelectedIndex, cursor = new { x = Plot.CursorX, y = Plot.CursorY },
                viewport = Plot.Viewport, structureTab = StructureTab, centerTab = CenterTab
            };
        }
        private object ReadDiagnostics()
        {
            return new
            {
                source = manualWeights != null ? "manual" : session.ReplayPlaying ? "replay" : session.Running ? "vts" : "idle",
                manualPreview = manualWeights != null, frames = session.Frames, invalidFrames = session.InvalidFrames,
                values = manualValues ?? (object)session.Values ?? new Dictionary<string, float>(),
                weights = manualWeights ?? (object)session.Weights ?? new Dictionary<string, float>(),
                errors = document.Ir.Validate().ToArray(), message = Diagnostics,
                preview = session.PreviewStatus, rawForward = session.RawForwardStatus
            };
        }
        private object EvaluateRaw(JsonElement payload)
        {
            if (!payload.TryGetProperty("raw", out var raw) || raw.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("raw 必须为数值字典。");
            var input = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var property in raw.EnumerateObject()) input.Add(property.Name, FiniteFloat(property.Value, property.Name));
            double time = payload.TryGetProperty("time", out _) ? ReadDouble(payload, "time") : 0;
            if (time < 0) throw new ArgumentException("time 不能为负数。");
            var value = new HoFaceValueSession(document.Value, 1);
            var ir = new HoFaceIrEvaluator(document.Ir);
            value.Evaluate(input, time);
            ir.Evaluate(value.Values);
            return new { source = "diagnostic", appliedToLiveSession = false, values = value.Values, weights = ir.Values };
        }
        private void PreviewAtCursor(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !float.IsFinite((float)x) || !float.IsFinite((float)y))
                throw new ArgumentException("游标坐标必须是有效数值。");
            if (!HasSelection) throw new InvalidOperationException("请先选择参数行或混合节点。");
            if (Mode != "Preview") throw new InvalidOperationException("手动权重预览仅在 Preview 模式可用。");
            EnsureNoDraft();
            var values = session.Values == null ? new Dictionary<string, float>() : new Dictionary<string, float>(session.Values);
            foreach (var parameter in document.Ir.parameters)
                if (!values.ContainsKey(parameter.name)) values.Add(parameter.name, parameter.defaultValue);
            var overrides = new Dictionary<string, float>();
            if (selectedValueRow != null) overrides[selectedValueRow.parameter] = HoFaceCurve.Transfer(selectedValueRow.curve, (float)x);
            else
            {
                if (!HasAxisX) throw new InvalidOperationException("当前节点没有可预览的参数轴。");
                overrides[selectedNode.parameterX] = (float)x;
                if (HasAxisY) overrides[selectedNode.parameterY] = (float)y;
            }
            parameterOverrides = overrides;
            ApplyParameterOverrides(values);
            Plot.CursorX = x; Plot.CursorY = y; Plot.InvalidateVisual();
            liveStatus = "手动预览 · 临时参数覆盖";
            if (session.Preview.Running) session.Preview.Send(manualValues, manualWeights, true, 0);
            OnPropertyChanged(nameof(LiveStatus)); OnPropertyChanged(nameof(HasManualPreview));
        }
        private void SetValueExpression(JsonElement payload)
        {
            int index = FindOutputIndex(RequiredString(payload, "parameter"), ReadInt(payload, "outputIndex", -1));
            string expression = ReadString(payload, "expression") ?? throw new ArgumentException("缺少 expression。");
            document.Edit((value, ir) => value.outputs[index].expression = expression);
            ReloadEditors();
        }
        private void SetNode(JsonElement payload)
        {
            string id = RequiredString(payload, "id");
            document.Edit((value, ir) =>
            {
                var node = ir.nodes.FirstOrDefault(x => x.id == id) ?? throw new ArgumentException("节点不存在：" + id);
                if (payload.TryGetProperty("name", out _)) node.name = ReadString(payload, "name");
                if (payload.TryGetProperty("parameterX", out _))
                {
                    if (node.type != "blend1d" && node.type != "blend2d") throw new ArgumentException("此类型节点没有参数轴。");
                    node.parameterX = RequiredString(payload, "parameterX");
                }
                if (payload.TryGetProperty("parameterY", out _))
                {
                    if (node.type != "blend2d") throw new ArgumentException("只有 blend2d 具有 Y 参数。");
                    node.parameterY = RequiredString(payload, "parameterY");
                }
            });
            ReloadEditors();
        }
        private void AddValueCurveKey(JsonElement payload)
        {
            int index = FindOutputIndex(RequiredString(payload, "parameter"), ReadInt(payload, "outputIndex", -1));
            float time = ReadFloat(payload, "time"), value = ReadFloat(payload, "value");
            float input = payload.TryGetProperty("inTangent", out var a) ? ReadTangent(a) : 1;
            float output = payload.TryGetProperty("outTangent", out var b) ? ReadTangent(b) : 1;
            document.Edit((profile, ir) =>
            {
                var row = profile.outputs[index];
                var keys = row.curve?.keys.ToList() ?? new List<HoFaceCurveKey>();
                if (keys.Any(k => Math.Abs(k.time - time) < 1e-5)) throw new ArgumentException("曲线点的输入坐标不能重复。");
                keys.Add(new HoFaceCurveKey(time, value, input, output));
                row.curve = new HoFaceCurveData(keys.ToArray());
            });
            ReloadEditors();
        }
        private void DeleteValueCurveKey(JsonElement payload)
        {
            int output = FindOutputIndex(RequiredString(payload, "parameter"), ReadInt(payload, "outputIndex", -1));
            int index = ReadInt(payload, "index", -1);
            document.Edit((value, ir) =>
            {
                var curve = value.outputs[output].curve;
                if (curve == null || index < 0 || index >= curve.length) throw new ArgumentException("曲线点不存在。");
                if (curve.length <= 2) throw new InvalidOperationException("至少保留两个曲线点。");
                curve.keys = curve.keys.Where((_, i) => i != index).ToArray();
            });
            ReloadEditors();
        }
        private object ConfigureView(JsonElement payload)
        {
            double? Number(string name, double min, double max)
            {
                if (!payload.TryGetProperty(name, out _)) return null;
                double number = ReadDouble(payload, name);
                if (number < min || number > max) throw new ArgumentException(name + " 超出范围：" + min + "–" + max);
                return number;
            }
            var structure = Number("structureWidth", 180, 500);
            var plot = Number("plotRatio", .1, .9);
            var width = Number("width", MinWidth, 7680);
            var height = Number("height", MinHeight, 4320);
            int structureIndex = ReadInt(payload, "structureTab", StructureTab), centerIndex = ReadInt(payload, "centerTab", CenterTab);
            if (structureIndex < 0 || structureIndex > 1 || centerIndex < 0 || centerIndex > 1)
                throw new ArgumentException("标签页索引必须为 0 或 1。");
            if (HasTextDraft && centerIndex != CenterTab) throw new InvalidOperationException("请先应用或还原 JSON 文本。");
            if (ReadBool(payload, "reset")) ResetLayout();
            if (structure.HasValue) StructureColumn.Width = new GridLength(structure.Value);
            if (plot.HasValue)
            {
                SetPlotRatio(plot.Value);
            }
            if (width.HasValue) Width = width.Value;
            if (height.HasValue) Height = height.Value;
            StructureTab = structureIndex; CenterTab = centerIndex;
            if (payload.TryGetProperty("showConnection", out _)) ShowConnection = ReadBool(payload, "showConnection");
            if (payload.TryGetProperty("showFiles", out _)) ShowFiles = ReadBool(payload, "showFiles");
            if (payload.TryGetProperty("showEditor", out _)) ShowEditor = ReadBool(payload, "showEditor");
            UpdateLayout();
            double editorTotal = PlotColumn.ActualWidth + InspectorColumn.ActualWidth;
            if (ShowEditor && editorTotal > 0) preferredPlotRatio = PlotColumn.ActualWidth / editorTotal;
            return new
            {
                width = Width, height = Height,
                structureWidth = StructureColumn.ActualWidth, plotWidth = PlotColumn.ActualWidth,
                inspectorWidth = InspectorColumn.ActualWidth, plotRatio = preferredPlotRatio,
                structureTab = StructureTab, centerTab = CenterTab, showConnection = ShowConnection, showFiles = ShowFiles,
                showEditor = ShowEditor,
                viewport = Plot.Viewport
            };
        }
        private static bool ReadBool(JsonElement payload, string name, bool fallback = false)
        {
            if (!payload.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw new ArgumentException(name + " 必须为布尔值。");
            return value.GetBoolean();
        }
    }
}
