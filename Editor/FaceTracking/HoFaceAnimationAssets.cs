using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public sealed class HoFaceBinding
    {
        public SkinnedMeshRenderer renderer;
        public int index;
        public string path, shape;
        public float initial;
    }

    public sealed class HoFaceCompiledController : IDisposable
    {
        public AnimatorOverrideController controller;
        public readonly List<AnimationClip> clips = new List<AnimationClip>();
        public readonly List<HoFaceBinding> bindings = new List<HoFaceBinding>();
        public readonly List<string> warnings = new List<string>();
        /// <summary>基础 Controller 上的 Float 参数名。AnimatorOverrideController 自己不暴露 parameters。</summary>
        public readonly HashSet<string> floatParameters = new HashSet<string>(StringComparer.Ordinal);
        public void Dispose()
        {
            if (controller != null) Object.DestroyImmediate(controller);
            foreach (var clip in clips) if (clip != null) Object.DestroyImmediate(clip);
            clips.Clear();
        }
    }

    public static class HoFaceAnimationAssets
    {
        public static bool Allowed(HoFaceTrackingDebugger rig, string shape)
        {
            if ((rig.outputRegions & HoFaceTrackingChannels.Region(shape)) == 0) return false;
            foreach (var channel in rig.channels)
                if (channel != null && channel.shape == shape) return channel.mode != HoFaceInputMode.Release;
            return false;
        }

        public static HoFaceCompiledController Compile(HoFaceTrackingDebugger rig, Func<string, bool> outputFilter = null)
        {
            if (rig.targetAnimator == null) throw new InvalidOperationException("请指定角色 Animator。");
            if (!(rig.faceController is AnimatorController source))
                throw new InvalidOperationException("请选择纯 Unity AnimatorController。首版不接 VRC 行为或嵌套 OverrideController。");
            foreach (var layer in source.layers)
            {
                if (layer.syncedLayerIndex >= 0) throw new InvalidOperationException("首版暂不支持同步图层。");
                ValidateMachine(layer.stateMachine);
            }
            var parameters = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            foreach (var p in source.parameters) parameters[p.name] = p.type;
            var usedParameters = new HashSet<string>(StringComparer.Ordinal);
            var usedShapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in rig.channels)
            {
                if (channel == null || HoFaceTrackingChannels.IndexOf(channel.shape) < 0)
                    throw new InvalidOperationException("参数映射必须使用标准 ARKit 键名。");
                if (!usedShapes.Add(channel.shape)) throw new InvalidOperationException("重复形态键映射：" + channel.shape);
                if (string.IsNullOrWhiteSpace(channel.parameter)) throw new InvalidOperationException("参数名不能为空：" + channel.shape);
                if (!usedParameters.Add(channel.parameter)) throw new InvalidOperationException("多个通道映射到同一参数：" + channel.parameter);
                if (parameters.TryGetValue(channel.parameter, out var type) && type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException("面捕参数必须是 Float：" + channel.parameter);
            }

            var result = new HoFaceCompiledController();
            try
            {
                foreach (var p in source.parameters)
                    if (p.type == AnimatorControllerParameterType.Float) result.floatParameters.Add(p.name);
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                var seen = new HashSet<AnimationClip>();
                var bound = new HashSet<(SkinnedMeshRenderer, int)>();
                foreach (AnimationClip clip in source.animationClips)
                {
                    if (!seen.Add(clip)) continue;
                    if (AnimationUtility.GetAnimationEvents(clip).Length != 0 || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0 || clip.humanMotion)
                        throw new InvalidOperationException("面部片段含事件、对象曲线或 Humanoid 动画：" + clip.name);
                    var copy = Object.Instantiate(clip);
                    copy.name = clip.name + " (Ho Preview)";
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    result.clips.Add(copy);
                    var curves = AnimationUtility.GetCurveBindings(clip);
                    foreach (var curve in curves) AnimationUtility.SetEditorCurve(copy, curve, null);
                    foreach (var curve in curves)
                    {
                        if (curve.type != typeof(SkinnedMeshRenderer) || !curve.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                            throw new InvalidOperationException("首版面部控制器只允许形态键曲线：" + clip.name + " / " + curve.propertyName);
                        string shape = curve.propertyName.Substring("blendShape.".Length);
                        if (!Allowed(rig, shape) || (outputFilter != null && !outputFilter(shape))) continue;
                        string path = Remap(rig, curve.path);
                        Transform node = path.Length == 0 ? rig.targetAnimator.transform : rig.targetAnimator.transform.Find(path);
                        var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                        {
                            string warning = path + " / " + shape + "：未找到绑定";
                            if (!result.warnings.Contains(warning)) result.warnings.Add(warning);
                            continue;
                        }
                        if (renderer.GetComponentInParent<Animator>() != rig.targetAnimator)
                            throw new InvalidOperationException("目标 Mesh 位于另一个 Animator 下：" + path);
                        var binding = curve;
                        binding.path = path;
                        AnimationUtility.SetEditorCurve(copy, binding, AnimationUtility.GetEditorCurve(clip, curve));
                        int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
                        if (bound.Add((renderer, index))) result.bindings.Add(new HoFaceBinding
                        {
                            renderer = renderer, index = index, path = path, shape = shape, initial = renderer.GetBlendShapeWeight(index)
                        });
                    }
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(clip, copy));
                }
                result.controller = new AnimatorOverrideController(source) { hideFlags = HideFlags.HideAndDontSave };
                result.controller.ApplyOverrides(overrides);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static string Remap(HoFaceTrackingDebugger rig, string path)
        {
            string result = path;
            int longest = -1;
            foreach (var mapping in rig.pathRemaps)
            {
                if (mapping == null || mapping.target == null) continue;
                string prefix = mapping.sourcePath ?? "";
                if (path != prefix && !(prefix.Length > 0 && path.StartsWith(prefix + "/", StringComparison.Ordinal))) continue;
                if (prefix.Length <= longest) continue;
                if (!mapping.target.transform.IsChildOf(rig.targetAnimator.transform)) throw new InvalidOperationException("重映射目标必须位于 Animator 根下。");
                string target = AnimationUtility.CalculateTransformPath(mapping.target.transform, rig.targetAnimator.transform);
                result = target + path.Substring(prefix.Length);
                longest = prefix.Length;
            }
            return result;
        }

        private static void ValidateMachine(AnimatorStateMachine machine)
        {
            if (machine.behaviours.Length != 0) throw new InvalidOperationException("不支持 StateMachineBehaviour：" + machine.name);
            foreach (var child in machine.states)
            {
                if (child.state.behaviours.Length != 0 || child.state.writeDefaultValues)
                    throw new InvalidOperationException("面部状态必须无 Behaviour 且 Write Defaults Off：" + child.state.name);
            }
            foreach (var child in machine.stateMachines) ValidateMachine(child.stateMachine);
        }

        public static AnimatorController Generate(Animator animator, string assetPath)
        {
            if (animator == null) throw new InvalidOperationException("请先指定 Animator。");
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) throw new InvalidOperationException("目标资产已存在，请选择新路径。");
            var meshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var groups = new Dictionary<string, List<EditorCurveBinding>>(StringComparer.Ordinal);
            foreach (var mesh in meshes)
            {
                if (mesh.sharedMesh == null || mesh.GetComponentInParent<Animator>() != animator) continue;
                foreach (string shape in HoFaceTrackingChannels.Names)
                {
                    if (mesh.sharedMesh.GetBlendShapeIndex(shape) < 0) continue;
                    if (!groups.TryGetValue(shape, out var list)) groups[shape] = list = new List<EditorCurveBinding>();
                    list.Add(EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(mesh.transform, animator.transform), typeof(SkinnedMeshRenderer), "blendShape." + shape));
                }
            }
            if (groups.Count == 0) throw new InvalidOperationException("Animator 子级没有匹配标准 ARKit 名称的形态键。");
            AnimatorController controller = null;
            try
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
                var initialLayers = controller.layers;
                initialLayers[0].iKPass = true;
                controller.layers = initialLayers;
                var root = controller.layers[0].stateMachine.AddState("Neutral");
                root.writeDefaultValues = false;
                var empty = new AnimationClip { name = "Neutral" };
                AssetDatabase.AddObjectToAsset(empty, controller);
                root.motion = empty;
                // Independent Override layers avoid Direct-tree normalization and cross-channel attenuation.
                foreach (var pair in groups)
                {
                    string parameter = "ARKit/" + pair.Key;
                    controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                    controller.AddLayer(pair.Key);
                    var layers = controller.layers;
                    var layer = layers[layers.Length - 1];
                    layer.defaultWeight = 1f;
                    layers[layers.Length - 1] = layer;
                    controller.layers = layers;
                    var state = layer.stateMachine.AddState(pair.Key);
                    state.writeDefaultValues = false;
                    var tree = new BlendTree { name = pair.Key, blendType = BlendTreeType.Simple1D, blendParameter = parameter, useAutomaticThresholds = false };
                    AssetDatabase.AddObjectToAsset(tree, controller);
                    for (int endpoint = 0; endpoint < 2; endpoint++)
                    {
                        var clip = new AnimationClip { name = pair.Key + "_" + endpoint, frameRate = 60f };
                        foreach (var binding in pair.Value) AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, endpoint * 100f));
                        AssetDatabase.AddObjectToAsset(clip, controller);
                        tree.AddChild(clip, endpoint);
                    }
                    state.motion = tree;
                }
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                return controller;
            }
            catch { if (controller != null) AssetDatabase.DeleteAsset(assetPath); throw; }
        }
    }
}
