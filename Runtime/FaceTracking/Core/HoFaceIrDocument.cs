using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    [Serializable] public sealed class HoFaceIrParameter
    {
        public string name;
        public float defaultValue, min, max = 1;
    }
    [Serializable] public sealed class HoFaceIrTarget
    {
        public string id, slot, shape;
        public float min, max = 1;
    }
    [Serializable] public sealed class HoFaceIrWeight
    {
        public string target;
        public float value;
    }
    [Serializable] public sealed class HoFaceIrChild
    {
        public string node, weightParameter;
        public float threshold, x, y, weight = 1;
    }
    [Serializable] public sealed class HoFaceIrNode
    {
        public string id, name, type = "pose", parameterX, parameterY;
        public bool normalize;
        public List<HoFaceIrWeight> weights = new List<HoFaceIrWeight>();
        public List<HoFaceIrChild> children = new List<HoFaceIrChild>();
    }
    [Serializable] public sealed class HoFaceIrDocument
    {
        public const string Format = "ho-face-ir";
        public const int Version = 1;
        public string id, displayName, root;
        public List<HoFaceIrParameter> parameters = new List<HoFaceIrParameter>();
        public List<HoFaceIrTarget> targets = new List<HoFaceIrTarget>();
        public List<HoFaceIrNode> nodes = new List<HoFaceIrNode>();

        public static bool TryParse(string json, out HoFaceIrDocument document, out string error)
        {
            document = null; error = null;
            try
            {
                var result = new HoFaceIrDocument();
                string format = null;
                double version = 0;
                var reader = new HoJsonReader(json ?? "");
                reader.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "format": format = r.ReadString(); break;
                        case "version": version = r.ReadNumber(); break;
                        case "id": result.id = r.ReadString(); break;
                        case "displayName": result.displayName = r.ReadString(); break;
                        case "root": result.root = r.ReadString(); break;
                        case "parameters": r.ReadArray(item => result.parameters.Add(ReadParameter(item))); break;
                        case "targets": r.ReadArray(item => result.targets.Add(ReadTarget(item))); break;
                        case "nodes": r.ReadArray(item => result.nodes.Add(ReadNode(item))); break;
                        default: r.SkipValue(); break;
                    }
                });
                if (json.Substring(reader.Position).Trim().Length != 0) throw new FormatException("IR JSON 后有多余内容。");
                if (format != Format || version != Version) throw new FormatException("不支持的 IR format/version。");
                var errors = result.Validate();
                if (errors.Count != 0) throw new FormatException(string.Join("\n", errors.ToArray()));
                document = result;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        private static HoFaceIrParameter ReadParameter(HoJsonReader r)
        {
            var p = new HoFaceIrParameter();
            r.ReadObject((k, v) =>
            {
                switch (k)
                {
                    case "name": p.name = v.ReadString(); break;
                    case "defaultValue": p.defaultValue = v.ReadFloat(); break;
                    case "min": p.min = v.ReadFloat(); break;
                    case "max": p.max = v.ReadFloat(); break;
                    default: v.SkipValue(); break;
                }
            });
            return p;
        }
        private static HoFaceIrTarget ReadTarget(HoJsonReader r)
        {
            var t = new HoFaceIrTarget();
            r.ReadObject((k, v) =>
            {
                switch (k)
                {
                    case "id": t.id = v.ReadString(); break;
                    case "slot": t.slot = v.ReadString(); break;
                    case "shape": t.shape = v.ReadString(); break;
                    case "min": t.min = v.ReadFloat(); break;
                    case "max": t.max = v.ReadFloat(); break;
                    default: v.SkipValue(); break;
                }
            });
            return t;
        }
        private static HoFaceIrNode ReadNode(HoJsonReader r)
        {
            var n = new HoFaceIrNode();
            r.ReadObject((k, v) =>
            {
                switch (k)
                {
                    case "id": n.id = v.ReadString(); break;
                    case "name": n.name = v.ReadString(); break;
                    case "type": n.type = v.ReadString(); break;
                    case "parameterX": n.parameterX = v.ReadString(); break;
                    case "parameterY": n.parameterY = v.ReadString(); break;
                    case "normalize": n.normalize = v.ReadBool(); break;
                    case "weights":
                        v.ReadArray(item =>
                        {
                            var w = new HoFaceIrWeight();
                            item.ReadObject((a, b) =>
                            {
                                if (a == "target") w.target = b.ReadString();
                                else if (a == "value") w.value = b.ReadFloat();
                                else b.SkipValue();
                            });
                            n.weights.Add(w);
                        }); break;
                    case "children":
                        v.ReadArray(item =>
                        {
                            var c = new HoFaceIrChild();
                            item.ReadObject((a, b) =>
                            {
                                switch (a)
                                {
                                    case "node": c.node = b.ReadString(); break;
                                    case "threshold": c.threshold = b.ReadFloat(); break;
                                    case "x": c.x = b.ReadFloat(); break;
                                    case "y": c.y = b.ReadFloat(); break;
                                    case "weight": c.weight = b.ReadFloat(); break;
                                    case "weightParameter": c.weightParameter = b.ReadString(); break;
                                    default: b.SkipValue(); break;
                                }
                            });
                            n.children.Add(c);
                        }); break;
                    default: v.SkipValue(); break;
                }
            });
            return n;
        }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public List<string> Validate()
        {
            var errors = new List<string>();
            var p = new HashSet<string>(StringComparer.Ordinal);
            var t = new HashSet<string>(StringComparer.Ordinal);
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            var n = new Dictionary<string, HoFaceIrNode>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(id)) errors.Add("IR 缺少稳定 id。");
            foreach (var item in parameters)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.name) || !p.Add(item.name))
                { errors.Add("参数名称为空或重复。"); continue; }
                if (!Finite(item.defaultValue) || !Finite(item.min) || !Finite(item.max) || item.max <= item.min)
                    errors.Add(item.name + "：参数范围或默认值无效。");
            }
            foreach (var item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !t.Add(item.id))
                { errors.Add("目标 id 为空或重复。"); continue; }
                if (string.IsNullOrWhiteSpace(item.slot) || string.IsNullOrWhiteSpace(item.shape))
                    errors.Add(item.id + "：需要明确 slot 与形态键名称。");
                if (!bindings.Add(item.slot + "\n" + item.shape)) errors.Add(item.id + "：目标绑定重复。");
                if (!Finite(item.min) || !Finite(item.max) || item.max < item.min) errors.Add(item.id + "：最终权重范围无效。");
            }
            if (targets.Count == 0) errors.Add("IR 没有目标。");
            foreach (var item in nodes)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || n.ContainsKey(item.id))
                { errors.Add("节点 id 为空或重复。"); continue; }
                n.Add(item.id, item);
            }
            if (root == null || !n.ContainsKey(root)) errors.Add("IR root 不存在。");
            foreach (var item in n.Values)
            {
                if (item.type != "pose" && item.type != "blend1d" && item.type != "blend2d" && item.type != "direct")
                    errors.Add(item.id + "：不支持的节点类型 " + item.type);
                if (item.type == "pose")
                {
                    if (item.children.Count != 0) errors.Add(item.id + "：Pose 不能含子节点。");
                    var own = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var w in item.weights)
                        if (w == null || w.target == null || !t.Contains(w.target) || !own.Add(w.target) || !Finite(w.value))
                            errors.Add(item.id + "：姿势目标缺失、重复或权重无效。");
                }
                else
                {
                    if (item.weights.Count != 0) errors.Add(item.id + "：混合节点不能含 Pose 权重。");
                    if (item.children.Count == 0) errors.Add(item.id + "：混合节点没有子项。");
                    if ((item.type == "blend1d" || item.type == "blend2d") && (item.parameterX == null || !p.Contains(item.parameterX)))
                        errors.Add(item.id + "：横轴参数未声明。");
                    if (item.type == "blend2d" && (item.parameterY == null || !p.Contains(item.parameterY)))
                        errors.Add(item.id + "：纵轴参数未声明。");
                }
                for (int i = 0; i < item.children.Count; i++)
                {
                    var c = item.children[i];
                    if (c == null) { errors.Add(item.id + "：空子项。"); continue; }
                    if (c.node == null || !n.ContainsKey(c.node)) errors.Add(item.id + "：子节点不存在。");
                    if (!Finite(c.weight) || c.weight < 0 || !Finite(c.threshold) || !Finite(c.x) || !Finite(c.y))
                        errors.Add(item.id + "：子项数值无效。");
                    if (!string.IsNullOrEmpty(c.weightParameter) && !p.Contains(c.weightParameter))
                        errors.Add(item.id + "：权重参数未声明。");
                    for (int j = 0; j < i; j++)
                    {
                        var other = item.children[j];
                        if (other == null) continue;
                        if (item.type == "blend1d" && c.threshold == other.threshold)
                            errors.Add(item.id + "：1D 阈值重复。");
                        if (item.type == "blend2d" && Math.Abs(c.x - other.x) < 1e-6 && Math.Abs(c.y - other.y) < 1e-6)
                            errors.Add(item.id + "：2D 坐标重合。");
                    }
                }
            }
            var colors = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string nodeId in n.Keys) Visit(nodeId, n, colors, errors, 0);
            return errors;
        }
        private static void Visit(string id, Dictionary<string, HoFaceIrNode> nodes,
            Dictionary<string, int> colors, List<string> errors, int depth)
        {
            if (colors.TryGetValue(id, out int color))
            { if (color == 1) errors.Add(id + "：IR 存在环。"); return; }
            if (depth > 64) { errors.Add(id + "：嵌套超过 64 层。"); return; }
            colors[id] = 1;
            foreach (var child in nodes[id].children)
                if (child?.node != null && nodes.ContainsKey(child.node)) Visit(child.node, nodes, colors, errors, depth + 1);
            colors[id] = 2;
        }
        public string Write()
        {
            var s = new StringBuilder();
            Func<string, string> q = HoFaceProfileJson.Quote;
            Func<float, string> f = x => x.ToString("R", CultureInfo.InvariantCulture);
            s.Append("{\n  \"format\": ").Append(q(Format)).Append(",\n  \"version\": 1,\n  \"id\": ").Append(q(id))
                .Append(",\n  \"displayName\": ").Append(q(displayName)).Append(",\n  \"root\": ").Append(q(root)).Append(",\n  \"parameters\": [");
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                s.Append(i == 0 ? "\n" : ",\n").Append("    { \"name\": ").Append(q(p.name))
                    .Append(", \"defaultValue\": ").Append(f(p.defaultValue)).Append(", \"min\": ").Append(f(p.min))
                    .Append(", \"max\": ").Append(f(p.max)).Append(" }");
            }
            s.Append("\n  ],\n  \"targets\": [");
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                s.Append(i == 0 ? "\n" : ",\n").Append("    { \"id\": ").Append(q(t.id)).Append(", \"slot\": ").Append(q(t.slot))
                    .Append(", \"shape\": ").Append(q(t.shape)).Append(", \"min\": ").Append(f(t.min)).Append(", \"max\": ").Append(f(t.max)).Append(" }");
            }
            s.Append("\n  ],\n  \"nodes\": [");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                s.Append(i == 0 ? "\n" : ",\n").Append("    {\n      \"id\": ").Append(q(n.id)).Append(", \"name\": ").Append(q(n.name))
                    .Append(", \"type\": ").Append(q(n.type));
                if (n.type == "pose")
                {
                    s.Append(",\n      \"weights\": [");
                    for (int j = 0; j < n.weights.Count; j++)
                        s.Append(j == 0 ? "" : ", ").Append("{ \"target\": ").Append(q(n.weights[j].target))
                            .Append(", \"value\": ").Append(f(n.weights[j].value)).Append(" }");
                    s.Append("]");
                }
                else
                {
                    s.Append(",\n      \"parameterX\": ").Append(q(n.parameterX)).Append(", \"parameterY\": ").Append(q(n.parameterY))
                        .Append(", \"normalize\": ").Append(n.normalize ? "true" : "false").Append(",\n      \"children\": [");
                    for (int j = 0; j < n.children.Count; j++)
                    {
                        var c = n.children[j];
                        s.Append(j == 0 ? "\n" : ",\n").Append("        { \"node\": ").Append(q(c.node))
                            .Append(", \"threshold\": ").Append(f(c.threshold)).Append(", \"x\": ").Append(f(c.x)).Append(", \"y\": ").Append(f(c.y))
                            .Append(", \"weight\": ").Append(f(c.weight)).Append(", \"weightParameter\": ").Append(q(c.weightParameter)).Append(" }");
                    }
                    s.Append("\n      ]");
                }
                s.Append("\n    }");
            }
            return s.Append("\n  ]\n}\n").ToString();
        }
    }
}
