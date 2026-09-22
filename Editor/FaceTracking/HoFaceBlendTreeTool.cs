using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>树里的一个键：哪张网格的哪个形态键。位置（第几个键）由列表顺序决定，用来对齐各方向的权重。</summary>
    [Serializable]
    public sealed class HoBlendTreeKey
    {
        public SkinnedMeshRenderer renderer;
        public int index;
        public string shape;

        /// <summary>工具 UI 的勾选状态。真正的键顺序以 <see cref="HoBlendTreePlan.keys"/> 为准。</summary>
        public bool selected;

        public bool Valid => renderer != null && renderer.sharedMesh != null && index >= 0
            && index < renderer.sharedMesh.blendShapeCount
            && renderer.sharedMesh.GetBlendShapeName(index) == shape;
    }

    /// <summary>树的一个子节点 = 一个"方向"：参数空间里的一个位置 + 每个键各自的权重。</summary>
    [Serializable]
    public sealed class HoBlendTreeDirection
    {
        public string name = "新方向";
        public Vector2 position;
        public float[] weights = new float[0];

        public float Weight(int key)
        {
            return weights != null && key >= 0 && key < weights.Length ? weights[key] : 0f;
        }

        public void SetWeight(int key, float value)
        {
            if (key < 0) return;
            if (weights == null || key >= weights.Length) Array.Resize(ref weights, key + 1);
            weights[key] = Mathf.Clamp(value, 0f, 100f);
        }

        public void Fit(int count)
        {
            if (weights == null) weights = new float[0];
            if (weights.Length == count) return;
            var next = new float[count];
            Array.Copy(weights, next, Mathf.Min(weights.Length, count));
            weights = next;
        }
    }

    /// <summary>
    /// 工具的输入：一棵"参数 → 姿势"的映射，**通用**（不绑定任何特定参数）。
    ///
    /// 默认值历史上是果冻那对参数 `Ho/JellyX` / `Ho/JellyY` —— **那两个参数已经不在生成物里了**：
    /// 果冻搬去了独立的 `HoSpringConstraint`，直接读键写键，不再借道 Animator 参数
    /// （见 docs/archive/FACE_TRACKING_PIPELINE_SPLIT.md 19.2）。所以这里也不再引用那个常量，
    /// 参数名由使用者填；名字不存在时工具会自己补出来（<see cref="EnsureParameter"/>）。
    /// </summary>
    [Serializable]
    public sealed class HoBlendTreePlan
    {
        public string layerName = "Ho/BlendTree";
        public string parameterX = "Ho/ParamX";
        public string parameterY = "Ho/ParamY";
        public readonly List<HoBlendTreeKey> keys = new List<HoBlendTreeKey>();
        public readonly List<HoBlendTreeDirection> directions = new List<HoBlendTreeDirection>();

        /// <summary>
        /// 是否自动补一个位于原点、全写 0 的子节点。
        ///
        /// **这是这个工具存在的主要理由之一。** 2D 混合树对权重做归一化 —— 只摆四角节点时，
        /// 参数在 (0,0) 拿到的是那四个姿势的加权平均，**静止时脸上就挂着一个固定的形变**。
        /// 原点放一个写零的子节点，静止才是精确的静止。
        /// </summary>
        public bool originChild = true;

        public bool HasOrigin
        {
            get
            {
                foreach (var direction in directions)
                    if (direction.position == Vector2.zero) return true;
                return false;
            }
        }

        public void FitKeys()
        {
            foreach (var direction in directions) direction.Fit(keys.Count);
        }
    }

    /// <summary>
    /// 把一份 <see cref="HoBlendTreePlan"/> 落成控制器资产：参数 + 一个图层 + 一棵 2D FreeformCartesian 树
    /// + 每个方向一张静态姿势片段。
    ///
    /// **为什么不"每个方向一个片段、片段带时间轴"**：那正是被砍掉的果冻动画方案。树是参数的函数、
    /// 没有时间轴，物理算出来的轨迹（过冲、回弹、两个轴不同频走出的 Lissajous）才能原样穿过去。
    ///
    /// **幂等**：同名图层整层重建（位置不变），片段按名字复用，跑第二次不会把子资产越堆越多。
    /// 只清理自己名下的孤儿资产，别人的片段和树一个字节都不动。
    /// </summary>
    public static class HoFaceBlendTreeTool
    {
        /// <summary>自家子资产的前缀。<b>不许改</b>：清理孤儿靠它认领，改了就会开始漏。</summary>
        public const string Prefix = "Ho/BT ";

        public const string StateName = "混合";

        public static bool IsOurs(string assetName) =>
            !string.IsNullOrEmpty(assetName) && assetName.StartsWith(Prefix, StringComparison.Ordinal);

        public static string TreeName(string layerName) => Prefix + layerName;

        public static string ClipName(string layerName, string direction) => Prefix + layerName + " · " + direction;

        /// <summary>四个角：两条轴各自 ±1，正方凸包把整个 [-1,1]² 包住，参数怎么夹都在包内。</summary>
        public static readonly Vector2[] Corners =
        {
            new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f)
        };

        public static readonly string[] CornerNames = { "右上", "左上", "左下", "右下" };

        /// <summary>缺就补，类型不对就报错（不静默）。</summary>
        public static void EnsureParameter(AnimatorController controller, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("参数名不能为空。");
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name != name) continue;
                if (parameter.type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException("参数 " + name + " 已存在且不是 Float（" + parameter.type + "）。");
                return;
            }

            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        public static int IndexOfLayer(AnimatorController controller, string layerName)
        {
            for (int i = 0; i < controller.layers.Length; i++)
                if (controller.layers[i].name == layerName) return i;
            return -1;
        }

        /// <summary>角色身上所有网格的形态键，一个不落地列出来。做"输入 mesh 列表 → 勾键"那一步用。</summary>
        public static List<HoBlendTreeKey> ScanKeys(IEnumerable<SkinnedMeshRenderer> meshes)
        {
            var result = new List<HoBlendTreeKey>();
            var seen = new HashSet<(SkinnedMeshRenderer, int)>();
            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.sharedMesh == null) continue;
                Mesh shared = mesh.sharedMesh;
                for (int i = 0; i < shared.blendShapeCount; i++)
                {
                    if (!seen.Add((mesh, i))) continue;
                    result.Add(new HoBlendTreeKey { renderer = mesh, index = i, shape = shared.GetBlendShapeName(i) });
                }
            }

            return result;
        }

        public static void Validate(Animator animator, AnimatorController controller, HoBlendTreePlan plan)
        {
            if (animator == null) throw new InvalidOperationException("请先指定角色 Animator。");
            if (controller == null) throw new InvalidOperationException("请先指定目标控制器。");
            if (string.IsNullOrWhiteSpace(plan.layerName)) throw new InvalidOperationException("图层名不能为空。");
            if (plan.layerName == HoFaceAnimationAssets.DriveLayerName)
                throw new InvalidOperationException("不能写进驱动层：" + HoFaceAnimationAssets.DriveLayerName + "。");
            if (plan.layerName == HoFaceAnimationAssets.EditLayerName)
                throw new InvalidOperationException("不能写进扩展点层：" + HoFaceAnimationAssets.EditLayerName + "。");
            if (plan.parameterX == plan.parameterY)
                throw new InvalidOperationException("横纵两个参数不能是同一个。");
            if (plan.keys.Count == 0) throw new InvalidOperationException("至少选一个形态键。");
            if (plan.directions.Count == 0) throw new InvalidOperationException("至少加一个方向（原点子节点是自动补的）。");
            foreach (var key in plan.keys)
            {
                if (!key.Valid) throw new InvalidOperationException("网格或形态键已失效：" + key.shape);
                if (key.renderer.GetComponentInParent<Animator>() != animator)
                    throw new InvalidOperationException("网格不在这个 Animator 下，曲线绑不上：" + key.renderer.name);
            }
        }

        /// <summary>落盘。**这是本工具唯一的写盘点。**</summary>
        public static void Write(Animator animator, AnimatorController controller, HoBlendTreePlan plan)
        {
            Validate(animator, controller, plan);
            plan.FitKeys();
            EnsureParameter(controller, plan.parameterX);
            EnsureParameter(controller, plan.parameterY);

            string assetPath = AssetDatabase.GetAssetPath(controller);
            var existing = IndexOurs(assetPath);

            // ── 图层：同名整层重建，位置保持不变（层序会决定同属性谁覆盖谁，不能随手挪）──
            int index = IndexOfLayer(controller, plan.layerName);
            if (index >= 0) controller.RemoveLayer(index);
            controller.AddLayer(plan.layerName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            if (index >= 0)
            {
                var list = new List<AnimatorControllerLayer>(layers);
                list.RemoveAt(list.Count - 1);
                list.Insert(Mathf.Clamp(index, 0, list.Count), layer);
                layers = list.ToArray();
            }

            controller.layers = layers;

            // ── 树 ────────────────────────────────────────────────────────────
            var tree = new BlendTree
            {
                name = TreeName(plan.layerName),
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = plan.parameterX,
                blendParameterY = plan.parameterY
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            var state = layer.stateMachine.AddState(StateName);
            state.writeDefaultValues = true;
            state.motion = tree;

            // 绑定路径在这里算一次，方向循环里复用。
            var paths = new string[plan.keys.Count];
            string[] shapes = new string[plan.keys.Count];
            for (int i = 0; i < plan.keys.Count; i++)
            {
                paths[i] = AnimationUtility.CalculateTransformPath(plan.keys[i].renderer.transform, animator.transform);
                shapes[i] = plan.keys[i].shape;
            }

            var keep = new HashSet<Object> { tree };
            var children = new List<ChildMotion>();
            if (plan.originChild && !plan.HasOrigin)
                children.Add(Child(controller, existing, keep, plan, paths, shapes, "原点", Vector2.zero, null));
            foreach (var direction in plan.directions)
                children.Add(Child(controller, existing, keep, plan, paths, shapes, direction.name, direction.position, direction));

            tree.children = children.ToArray();

            Prune(assetPath, plan.layerName, keep);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static ChildMotion Child(AnimatorController controller, Dictionary<string, Object> existing,
            HashSet<Object> keep, HoBlendTreePlan plan, string[] paths, string[] shapes,
            string directionName, Vector2 position, HoBlendTreeDirection direction)
        {
            string name = ClipName(plan.layerName, directionName);
            if (!existing.TryGetValue(name, out Object asset) || !(asset is AnimationClip clip))
            {
                clip = new AnimationClip { name = name, frameRate = 60f };
                AssetDatabase.AddObjectToAsset(clip, controller);
            }

            // 每个方向都写**所有**键（没设权重的写 0）：这样任意参数点上都有确定的值，
            // 不会出现"某个键在这个方向上没人写，于是跟 Write Defaults 打架"。
            for (int i = 0; i < paths.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(paths[i], typeof(SkinnedMeshRenderer), "blendShape." + shapes[i]),
                    AnimationCurve.Constant(0f, 1f / 60f, direction != null ? direction.Weight(i) : 0f));
            }

            keep.Add(clip);
            return new ChildMotion { motion = clip, position = position, timeScale = 1f };
        }

        private static Dictionary<string, Object> IndexOurs(string assetPath)
        {
            var result = new Dictionary<string, Object>(StringComparer.Ordinal);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (asset != null && IsOurs(asset.name)) result[asset.name] = asset;
            return result;
        }

        /// <summary>
        /// 清掉这次没用上的自家孤儿（改过方向名、删过方向都会留下）。
        /// **范围卡得很死**：只删"当前图层树里到不了、且名字确实属于本工具这一层"的东西 ——
        /// 别人的片段、另一层同一棵树都不受影响。
        /// </summary>
        private static void Prune(string assetPath, string layerName, HashSet<Object> keep)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var live = new HashSet<Object>();
            foreach (var asset in all)
                if (asset is AnimatorController controller)
                    foreach (var layer in controller.layers) Collect(layer.stateMachine, live);

            string scope = Prefix + layerName;
            bool Mine(Object candidate)
            {
                if (candidate == null || live.Contains(candidate) || keep.Contains(candidate)) return false;
                if (candidate is AnimatorStateMachine machine) return machine.name == layerName;
                if (candidate is AnimatorState state) return state.name == StateName;
                if (candidate is AnimationClip clip) return clip.name.StartsWith(scope, StringComparison.Ordinal);
                if (candidate is BlendTree tree) return tree.name == TreeName(layerName);
                return false;
            }

            foreach (var asset in all)
                if (Mine(asset)) Object.DestroyImmediate(asset, true);
        }

        private static void Collect(AnimatorStateMachine machine, HashSet<Object> live)
        {
            if (machine == null || !live.Add(machine)) return;
            foreach (var child in machine.states)
            {
                if (child.state == null) continue;
                live.Add(child.state);
                Collect(child.state.motion, live);
            }

            foreach (var child in machine.stateMachines) Collect(child.stateMachine, live);
        }

        private static void Collect(Motion motion, HashSet<Object> live)
        {
            if (motion == null || !live.Add(motion)) return;
            if (!(motion is BlendTree tree)) return;
            foreach (var child in tree.children) Collect(child.motion, live);
        }
    }
}
