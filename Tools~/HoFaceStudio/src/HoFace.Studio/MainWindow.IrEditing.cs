using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public partial class MainWindow
    {
        private int selectedPoseWeightIndex = -1;
        private int selectedControlPointIndex = -1;
        private string poseTargetDraft = "";
        private string poseWeightDraft = "1";
        private int visibleIrNodeCount;

        public int SelectedPoseWeightIndex
        {
            get => selectedPoseWeightIndex;
            set
            {
                if (selectedPoseWeightIndex == value) return;
                selectedPoseWeightIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeletePoseWeight));
            }
        }
        public int SelectedControlPointIndex
        {
            get => selectedControlPointIndex;
            set
            {
                if (selectedControlPointIndex == value) return;
                selectedControlPointIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDeleteSelectedControlPoint));
            }
        }
        public string PoseTargetDraft { get => poseTargetDraft; set { poseTargetDraft = value ?? ""; OnPropertyChanged(); } }
        public string PoseWeightDraft { get => poseWeightDraft; set { poseWeightDraft = value ?? ""; OnPropertyChanged(); } }
        public int VisibleIrNodeCount => visibleIrNodeCount;
        public int OrphanIrNodeCount => document.Ir == null ? 0 : document.Ir.nodes.Count - ReachableIrNodes(document.Ir).Count;
        public bool HasOrphanIrNodes => OrphanIrNodeCount > 0;
        public bool CanDeleteSelectedNode => selectedNode != null && (document.Ir.root == selectedNode.id ||
            !document.Ir.nodes.Any(parent => IsBlendContainer(parent) && parent.children.Count <= 1 &&
                parent.children.Any(child => child?.node == selectedNode.id)));
        public bool ShowIrGraphActions => CenterTab == 1 && HasNodeSelection;
        public bool CanDeleteSelectedControlPoint => HasAxisX && ControlPoints.Count > 1 && selectedControlPointIndex >= 0 &&
            selectedControlPointIndex < ControlPoints.Count;
        public Visibility PoseWeightEmptyVisibility => HasPoseSelection && InspectorWeights.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        public string BlendChildrenTitle => selectedNode?.type == "blend1d" ? "1D Blend Tree 子项" :
            selectedNode?.type == "blend2d" ? "2D Blend Tree 子项" : "Direct 子项";
        public bool CanAddPoseRule => FindBlendParentId() != null;
        public bool CanAddBlendRule => FindBlendParentId() != null;
        public bool CanDeletePoseWeight => HasPoseSelection && selectedPoseWeightIndex >= 0 &&
            selectedPoseWeightIndex < InspectorWeights.Count;
        public string PoseWeightEmptyText => HasPoseSelection && InspectorWeights.Count == 0
            ? "此 Pose 尚未保存任何输出权重" : "";

        private static bool IsBlendContainer(HoFaceIrNode node) =>
            node != null && (node.type == "blend1d" || node.type == "blend2d" || node.type == "direct");

        private HoFaceIrNode BlendPreviewNode
        {
            get
            {
                if (IsBlendContainer(selectedNode)) return selectedNode;
                return selectedNode == null ? null : document.Ir?.nodes.FirstOrDefault(x =>
                    IsBlendContainer(x) && x.children.Any(child => child?.node == selectedNode.id));
            }
        }

        private List<HoFaceIrNode> ReachableIrNodes(HoFaceIrDocument graph)
        {
            var result = new List<HoFaceIrNode>();
            if (graph == null) return result;
            var nodes = graph.nodes.ToDictionary(x => x.id, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (id == null || !visited.Add(id) || !nodes.TryGetValue(id, out var node)) return;
                result.Add(node);
                if (node.children == null) return;
                foreach (var child in node.children) Visit(child?.node);
            }
            Visit(graph.root);
            return result;
        }

        private string NodeAlias(string id)
        {
            var node = document.Ir?.nodes.FirstOrDefault(x => x.id == id);
            return node == null || string.IsNullOrWhiteSpace(node.name) ? id : node.name;
        }

        private void RefreshIrTree()
        {
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var oldRoot in IrTreeRoots) CollectExpanded(oldRoot, expanded);
            IrTreeRoots.Clear();
            string filter = IrFilter?.Trim() ?? "";
            var graph = document.Ir;
            if (graph != null && graph.nodes.Any(x => x.id == graph.root))
            {
                var nodes = graph.nodes.ToDictionary(x => x.id, StringComparer.Ordinal);
                var matchCache = new Dictionary<string, bool>(StringComparer.Ordinal);
                bool BranchMatches(string id, HashSet<string> visiting)
                {
                    if (string.IsNullOrEmpty(filter)) return true;
                    if (matchCache.TryGetValue(id, out bool cached)) return cached;
                    if (!visiting.Add(id) || !nodes.TryGetValue(id, out var node)) return false;
                    bool matches = (node.id + " " + node.name + " " + node.type + " " +
                        node.parameterX + " " + node.parameterY).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matches && node.children != null)
                        matches = node.children.Any(child => child != null && BranchMatches(child.node, visiting));
                    visiting.Remove(id);
                    matchCache[id] = matches;
                    return matches;
                }
                IrTreeItem Build(string id, HashSet<string> path, bool root)
                {
                    if (!nodes.TryGetValue(id, out var node) || !path.Add(id)) return null;
                    if (!BranchMatches(id, new HashSet<string>(StringComparer.Ordinal)))
                    { path.Remove(id); return null; }
                    var item = new IrTreeItem(node, root || expanded.Contains(id) || !string.IsNullOrEmpty(filter))
                    { IsSelected = id == selectedNodeId };
                    if (node.children != null)
                        foreach (var child in node.children)
                        {
                            var nested = child == null ? null : Build(child.node, path, false);
                            if (nested != null) item.Children.Add(nested);
                        }
                    path.Remove(id);
                    return item;
                }
                var root = Build(graph.root, new HashSet<string>(StringComparer.Ordinal), true);
                if (root != null)
                {
                    IrTreeRoots.Add(root);
                    visibleIrNodeCount = CountTree(root);
                }
                else visibleIrNodeCount = 0;
            }
            else visibleIrNodeCount = 0;
            foreach (string name in new[] { nameof(NodeCount), nameof(VisibleIrNodeCount), nameof(OrphanIrNodeCount),
                nameof(HasOrphanIrNodes), nameof(CanDeleteSelectedNode), nameof(CanAddPoseRule),
                nameof(CanAddBlendRule), nameof(CanDeletePoseWeight), nameof(PoseWeightEmptyText),
                nameof(PoseWeightEmptyVisibility), nameof(CanDeleteSelectedControlPoint), nameof(BlendChildrenTitle) })
                OnPropertyChanged(name);
        }

        private static int CountTree(IrTreeItem item) => 1 + item.Children.Sum(CountTree);
        private static void CollectExpanded(IrTreeItem item, HashSet<string> expanded)
        {
            if (item.IsExpanded) expanded.Add(item.NodeId);
            foreach (var child in item.Children) CollectExpanded(child, expanded);
        }
        private void SelectIrTreeItem(string id)
        {
            foreach (var root in IrTreeRoots) SetTreeSelection(root, id);
        }
        private static bool SetTreeSelection(IrTreeItem item, string id)
        {
            bool selected = item.NodeId == id;
            bool descendantSelected = false;
            foreach (var child in item.Children)
                descendantSelected |= SetTreeSelection(child, id);
            item.IsSelected = selected;
            if (descendantSelected) item.IsExpanded = true;
            return selected || descendantSelected;
        }
        private void NodeTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is IrTreeItem item && item.Node != selectedNode)
                SelectedNodeRow = NodeRows.FirstOrDefault(x => x.Node.id == item.NodeId);
        }
        private string FindBlendParentId()
        {
            if (selectedNode == null || document.Ir == null) return null;
            if (IsBlendContainer(selectedNode)) return selectedNode.id;
            return document.Ir.nodes.FirstOrDefault(x => IsBlendContainer(x) &&
                x.children.Any(child => child?.node == selectedNode.id))?.id;
        }

        private void AddPoseRuleClicked(object sender, RoutedEventArgs e)
        {
            try { AddChildRule(false, false); }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void AddBlendRuleClicked(object sender, RoutedEventArgs e)
        {
            try { AddChildRule(true, false); }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }
        private void AddChildRule(bool nestedBlend, bool keepParentSelected)
        {
            EnsureNoDraft();
            string parentId = FindBlendParentId();
            if (parentId == null) throw new InvalidOperationException("先在树中选择一个 Blend Tree，再添加子项。");
            string kind = nestedBlend && document.Ir.nodes.First(x => x.id == parentId).type == "blend2d"
                ? "blend2d" : nestedBlend ? "blend1d" : "pose";
            AddChildRule(parentId, kind, null, keepParentSelected);
        }

        private object AddIrRuleFromAutomation(JsonElement payload)
        {
            string parentId = RequiredString(payload, "parentId");
            string kind = RequiredString(payload, "type").ToLowerInvariant();
            string name = ReadString(payload, "name");
            EnsureNoDraft();
            string id = AddChildRule(parentId, kind, name, false);
            return new { created = id, selectedNode = selectedNodeId, rules = ReadItems(null) };
        }

        private object DeleteIrRuleFromAutomation(JsonElement payload)
        {
            string id = RequiredString(payload, "id");
            DeleteIrRule(id);
            return new { deleted = id, selectedNode = selectedNodeId, rules = ReadItems(null) };
        }

        private object DeleteIrChildFromAutomation(JsonElement payload)
        {
            string nodeId = RequiredString(payload, "nodeId");
            int index = ReadInt(payload, "index", -1);
            DeleteIrChild(nodeId, index, true);
            return new { nodeId, index, rules = ReadItems(null) };
        }

        private string AddChildRule(string parentId, string kind, string name, bool keepParentSelected)
        {
            if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException("缺少父级混合规则。");
            if (kind != "pose" && kind != "blend1d" && kind != "blend2d" && kind != "direct")
                throw new ArgumentException("IR 子规则类型仅支持 pose、blend1d、blend2d 或 direct。");
            string createdId = null;
            document.Edit((value, graph) =>
            {
                var parent = graph.nodes.FirstOrDefault(x => x.id == parentId && IsBlendContainer(x))
                    ?? throw new ArgumentException("父级必须是一个 Blend Tree 或 Direct 节点。");
                if (kind != "pose")
                {
                    string parameterName = null;
                    if (kind == "blend1d" || kind == "blend2d")
                    {
                        var parameter = graph.parameters.FirstOrDefault(x => x.name == parent.parameterX)
                            ?? graph.parameters.FirstOrDefault();
                        if (parameter == null)
                        {
                            parameter = new HoFaceIrParameter
                            { name = UniqueId(graph.parameters.Select(x => x.name), "Blend/Value"), min = 0, max = 1 };
                            graph.parameters.Add(parameter);
                        }
                        parameterName = parameter.name;
                    }
                    string parameterY = null;
                    if (kind == "blend2d")
                    {
                        var yParameter = graph.parameters.FirstOrDefault(x => x.name == parent.parameterY &&
                            x.name != parameterName) ?? graph.parameters.FirstOrDefault(x => x.name != parameterName);
                        if (yParameter == null)
                        {
                            yParameter = new HoFaceIrParameter
                            { name = UniqueId(graph.parameters.Select(x => x.name), "Blend/ValueY"), min = 0, max = 1 };
                            graph.parameters.Add(yParameter);
                        }
                        parameterY = yParameter.name;
                    }
                    createdId = UniqueId(graph.nodes.Select(x => x.id), kind);
                    string poseId = UniqueId(graph.nodes.Select(x => x.id).Concat(new[] { createdId }), "pose");
                    string poseName = "新姿势";
                    var pose = new HoFaceIrNode { id = poseId, name = poseName, type = "pose" };
                    graph.nodes.Add(pose);
                    var blend = new HoFaceIrNode
                    {
                        id = createdId, name = name ?? (kind == "direct" ? "新 Direct" : "新混合"),
                        type = kind, parameterX = parameterName, parameterY = parameterY,
                        children = new List<HoFaceIrChild> { new HoFaceIrChild { node = poseId, threshold = 0, weight = 1 } }
                    };
                    graph.nodes.Add(blend);
                    AppendChild(parent, blend.id);
                }
                else
                {
                    createdId = UniqueId(graph.nodes.Select(x => x.id), "pose");
                    graph.nodes.Add(new HoFaceIrNode { id = createdId, name = name ?? "新姿势", type = "pose" });
                    AppendChild(parent, createdId);
                }
            });
            ReloadEditors();
            selectedNodeId = keepParentSelected ? parentId : createdId;
            ResolveSelection();
            RefreshPlot(true);
            RefreshRows();
            SelectIrTreeItem(selectedNodeId);
            OnPropertyChanged(nameof(SelectedNodeRow));
            return createdId;
        }

        private static void AppendChild(HoFaceIrNode parent, string childId)
        {
            var child = new HoFaceIrChild { node = childId, weight = 1 };
            if (parent.type == "blend1d")
                child.threshold = parent.children.Count == 0 ? 0 : parent.children.Max(x => x.threshold) + 1;
            else if (parent.type == "blend2d")
            {
                var candidates = new[]
                {
                    (X: 0f, Y: 0f), (X: 0f, Y: 1f), (X: 1f, Y: 0f), (X: 1f, Y: 1f),
                    (X: -1f, Y: 0f), (X: 0f, Y: -1f), (X: -1f, Y: -1f), (X: 1f, Y: -1f),
                    (X: -1f, Y: 1f)
                };
                var free = candidates.FirstOrDefault(candidate => !parent.children.Any(existing =>
                    Math.Abs(existing.x - candidate.X) < 1e-6 && Math.Abs(existing.y - candidate.Y) < 1e-6));
                if (parent.children.Any(existing => Math.Abs(existing.x - free.X) < 1e-6 &&
                    Math.Abs(existing.y - free.Y) < 1e-6))
                {
                    int ring = 2;
                    while (true)
                    {
                        var candidate = (X: (float)ring, Y: 0f);
                        if (!parent.children.Any(existing => Math.Abs(existing.x - candidate.X) < 1e-6 &&
                            Math.Abs(existing.y - candidate.Y) < 1e-6))
                        { free = candidate; break; }
                        ring++;
                    }
                }
                child.x = free.X;
                child.y = free.Y;
            }
            parent.children.Add(child);
        }

        private static string UniqueId(IEnumerable<string> existing, string prefix)
        {
            var names = new HashSet<string>(existing.Where(x => x != null), StringComparer.Ordinal);
            if (!names.Contains(prefix)) return prefix;
            for (int i = 2; ; i++)
            {
                string candidate = prefix + i;
                if (!names.Contains(candidate)) return candidate;
            }
        }

        private void DeleteControlPointClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (selectedNode == null || ControlPointGrid.SelectedIndex < 0 ||
                    ControlPointGrid.SelectedIndex >= selectedNode.children.Count) return;
                DeleteIrChild(selectedNodeId, ControlPointGrid.SelectedIndex, true);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }

        private void DeleteSelectedRuleClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (selectedNode == null) return;
                DeleteIrRule(selectedNodeId);
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }

        private void DeleteIrRule(string deletingId)
        {
            EnsureNoDraft();
            document.Edit((value, graph) =>
            {
                var deleting = graph.nodes.FirstOrDefault(x => x.id == deletingId)
                    ?? throw new ArgumentException("IR 规则不存在：" + deletingId);
                if (graph.root != deletingId && graph.nodes.Any(parent => IsBlendContainer(parent) &&
                    parent.children.Count <= 1 && parent.children.Any(child => child?.node == deletingId)))
                    throw new InvalidOperationException("此规则是父 Blend Tree 的唯一子项；请先添加替代子项。");
                if (graph.root == deletingId)
                {
                    graph.root = deleting.children?.FirstOrDefault()?.node;
                    if (string.IsNullOrEmpty(graph.root))
                    {
                        graph.root = UniqueId(graph.nodes.Select(x => x.id), "pose");
                        graph.nodes.Add(new HoFaceIrNode { id = graph.root, name = "新姿势", type = "pose" });
                    }
                }
                foreach (var parent in graph.nodes)
                    parent.children.RemoveAll(child => child?.node == deletingId);
                graph.nodes.Remove(deleting);
                PruneUnreachable(graph);
                PruneUnusedTargets(graph);
            });
            selectedNodeId = document.Ir.root;
            ReloadEditors();
        }

        private void DeleteIrChild(string parentId, int index, bool deleteUnreferenced)
        {
            EnsureNoDraft();
            document.Edit((value, graph) =>
            {
                var parent = graph.nodes.FirstOrDefault(x => x.id == parentId && IsBlendContainer(x))
                    ?? throw new ArgumentException("混合规则不存在：" + parentId);
                if (index < 0 || index >= parent.children.Count)
                    throw new ArgumentException("混合子项索引无效。");
                if (parent.children.Count <= 1)
                    throw new InvalidOperationException("Blend Tree 至少保留一个子项；请先添加替代子项。");
                parent.children.RemoveAt(index);
                if (deleteUnreferenced)
                {
                    PruneUnreachable(graph);
                    PruneUnusedTargets(graph);
                }
            });
            ReloadEditors();
        }

        private void ClearOrphanRulesClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearOrphanIrRules();
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }

        private int ClearOrphanIrRules()
        {
            EnsureNoDraft();
            int removed = OrphanIrNodeCount;
            if (removed == 0) return 0;
            document.Edit((value, graph) =>
            {
                PruneUnreachable(graph);
                PruneUnusedTargets(graph);
            });
            selectedNodeId = document.Ir.root;
            ReloadEditors();
            return removed;
        }

        private object ClearOrphanRulesFromAutomation() =>
            new { removed = ClearOrphanIrRules(), rules = ReadItems(null) };

        private object DeletePoseWeightFromAutomation(JsonElement payload)
        {
            EnsureNoDraft();
            string nodeId = RequiredString(payload, "nodeId");
            string targetId = RequiredString(payload, "targetId");
            document.Edit((value, graph) =>
            {
                var pose = graph.nodes.FirstOrDefault(x => x.id == nodeId && x.type == "pose")
                    ?? throw new ArgumentException("Pose 节点不存在：" + nodeId);
                int index = pose.weights.FindIndex(x => x.target == targetId);
                if (index < 0) throw new ArgumentException("此 Pose 未保存该目标：" + targetId);
                pose.weights.RemoveAt(index);
                PruneUnusedTargets(graph);
            });
            ReloadEditors();
            return new { nodeId, targetId, rules = ReadItems(null) };
        }

        private static void PruneUnreachable(HoFaceIrDocument graph)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var byId = graph.nodes.ToDictionary(x => x.id, StringComparer.Ordinal);
            void Visit(string id)
            {
                if (id == null || !reachable.Add(id) || !byId.TryGetValue(id, out var node)) return;
                foreach (var child in node.children) Visit(child?.node);
            }
            Visit(graph.root);
            graph.nodes.RemoveAll(node => !reachable.Contains(node.id));
        }

        private static void PruneUnusedTargets(HoFaceIrDocument graph)
        {
            var used = new HashSet<string>(graph.nodes.Where(x => x.type == "pose")
                .SelectMany(x => x.weights).Where(x => x?.target != null).Select(x => x.target), StringComparer.Ordinal);
            graph.targets.RemoveAll(target => !used.Contains(target.id));
        }

        private void AddPoseWeightClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (!HasPoseSelection) return;
                string targetId = PoseTargetDraft.Trim();
                if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("请输入目标 ID。");
                float value = ParseNumber(PoseWeightDraft);
                document.Edit((profile, graph) =>
                {
                    var pose = graph.nodes.First(x => x.id == selectedNodeId && x.type == "pose");
                    if (pose.weights.Any(x => x.target == targetId)) throw new ArgumentException("此 Pose 已有该目标。");
                    if (!graph.targets.Any(x => x.id == targetId))
                        graph.targets.Add(new HoFaceIrTarget { id = targetId, min = 0, max = 1 });
                    pose.weights.Add(new HoFaceIrWeight { target = targetId, value = value });
                });
                PoseTargetDraft = "";
                PoseWeightDraft = "1";
                ReloadEditors();
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }

        private void DeletePoseWeightClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureNoDraft();
                if (!HasPoseSelection || !CanDeletePoseWeight) return;
                int index = selectedPoseWeightIndex;
                document.Edit((value, graph) =>
                {
                    var pose = graph.nodes.First(x => x.id == selectedNodeId);
                    pose.weights.RemoveAt(index);
                    PruneUnusedTargets(graph);
                });
                selectedPoseWeightIndex = -1;
                ReloadEditors();
            }
            catch (Exception error) { ReloadEditorsKeepingDraft(); ShowError(error.Message); }
        }

        private void EditPoseWeightTarget(string nodeId, string previousTarget, string nextTarget)
        {
            nextTarget = nextTarget?.Trim();
            if (string.IsNullOrWhiteSpace(nextTarget)) throw new ArgumentException("目标 ID 不能为空。");
            var before = SnapshotDocument();
            AuthoringDocument.Parse(before.Value, before.Ir, out _, out var graph);
            var pose = graph.nodes.FirstOrDefault(x => x.id == nodeId && x.type == "pose")
                ?? throw new InvalidOperationException("Pose 节点不存在。");
            var weight = pose.weights.FirstOrDefault(x => x.target == previousTarget)
                ?? throw new InvalidOperationException("此 Pose 已不存在该目标。");
            if (pose.weights.Any(x => x != weight && x.target == nextTarget))
                throw new ArgumentException("此 Pose 已有该目标。");
            weight.target = nextTarget;
            if (!graph.targets.Any(x => x.id == nextTarget))
                graph.targets.Add(new HoFaceIrTarget { id = nextTarget, min = 0, max = 1 });
            PruneUnusedTargets(graph);
            var errors = graph.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            document.ApplyText(before.Value, graph.Write());
            ReloadEditors();
        }

        private static HashSet<string> ReachableIds(HoFaceIrDocument graph)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (graph == null) return result;
            var nodes = graph.nodes.ToDictionary(x => x.id, StringComparer.Ordinal);
            void Visit(string id)
            {
                if (id == null || !result.Add(id) || !nodes.TryGetValue(id, out var node)) return;
                foreach (var child in node.children) Visit(child?.node);
            }
            Visit(graph.root);
            return result;
        }
    }

    public sealed class IrTreeItem : INotifyPropertyChanged
    {
        private bool isExpanded;
        private bool isSelected;
        private bool isOverridden;
        public HoFaceIrNode Node { get; }
        public string NodeId => Node.id;
        public string Name => string.IsNullOrWhiteSpace(Node.name) ? Node.id : Node.name;
        public string KindLabel => Node.type switch
        {
            "pose" => "Pose",
            "blend1d" => "1D",
            "blend2d" => "2D",
            "direct" => "Direct",
            _ => Node.type
        };
        public string Tooltip => Node.id + " · " + KindLabel;
        public ObservableCollection<IrTreeItem> Children { get; } = new ObservableCollection<IrTreeItem>();
        public bool IsExpanded { get => isExpanded; set { isExpanded = value; OnPropertyChanged(); } }
        public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(); } }
        public bool IsOverridden
        {
            get => isOverridden;
            set { if (isOverridden == value) return; isOverridden = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        public IrTreeItem(HoFaceIrNode node, bool expanded) { Node = node; isExpanded = expanded; }
        private void OnPropertyChanged([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
