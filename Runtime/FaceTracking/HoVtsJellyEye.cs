using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>One pupil or highlight layer, with independent movement, scale and squash overlays.</summary>
    [AddComponentMenu("HoUnityTools/面捕/VTS 果冻眼")]
    [DefaultExecutionOrder(31500)]
    public sealed class HoVtsJellyEye : MonoBehaviour
    {
        [SerializeField] private HoVtsIrController irController;
        [SerializeField] private List<SkinnedMeshRenderer> targetMeshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private HoVtsJellyInputs inputs = new HoVtsJellyInputs();
        [SerializeField] private HoVtsJellyMovement movement = new HoVtsJellyMovement();
        [SerializeField] private HoVtsJellyScale scale = new HoVtsJellyScale();
        [SerializeField] private HoVtsJellySquash squash = new HoVtsJellySquash();
        [SerializeField] private bool autoRun = true;

        private readonly List<OutputBinding> bindings = new List<OutputBinding>();
        private readonly List<SkinnedMeshRenderer> loadedRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Mesh> loadedMeshes = new List<Mesh>();
        private readonly List<string> loadedInputNames = new List<string>();
        private readonly string[] loadedOutputNames = new string[8];
        private readonly List<string> missingOutputKeys = new List<string>();
        private readonly List<string> missingInputKeys = new List<string>();
        private readonly Dictionary<string, float> frame = new Dictionary<string, float>(StringComparer.Ordinal);
        private HoVtsJellySpring horizontalSpring, verticalSpring, scaleSpring, squashSpring;
        private HoVtsIrController loadedController;
        private ulong loadedBindingRevision;
        private bool configurationLoaded, bindingsValid, hadInput;
        private string bindingError, inputStatus, status = "未初始化";

        public HoVtsIrController IrController { get => irController; set { if (irController != value) { Stop(); irController = value; } } }
        public List<SkinnedMeshRenderer> TargetMeshes => targetMeshes;
        public HoVtsJellyInputs Inputs => inputs;
        public HoVtsJellyMovement Movement => movement;
        public HoVtsJellyScale Scale => scale;
        public HoVtsJellySquash Squash => squash;
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public string Status => status;
        public int BindingCount => bindings.Count;
        public IReadOnlyList<string> MissingOutputKeys => missingOutputKeys;
        public IReadOnlyList<string> MissingInputKeys => missingInputKeys;
        public Vector2 MovementOffset { get; private set; }
        public float ScaleOffset { get; private set; }
        /// <summary>Positive is stretch; negative is squash.</summary>
        public float SquashOffset { get; private set; }

        private void LateUpdate()
        {
            if (autoRun) Step(Time.deltaTime);
        }

        /// <summary>Advance once after IR. This method only reads its completed output, never evaluates it.</summary>
        public bool Step(float deltaTime)
        {
            if (!isActiveAndEnabled) { Stop(); return false; }
            if (!Finite(deltaTime) || deltaTime <= 0) return false;
            if (!ConfigurationMatches()) BuildBindings();
            if (!bindingsValid) { status = bindingError; return false; }

            bool hasInput = TryReadInputs(out float horizontal, out float vertical, out float blink);
            if (hasInput)
            {
                horizontal += blink * SafeGain(movement.blinkHorizontal);
                vertical += blink * SafeGain(movement.blinkVertical);
                if (!hadInput)
                {
                    horizontalSpring.Reset(horizontal); verticalSpring.Reset(vertical);
                    scaleSpring.Reset(blink); squashSpring.Reset(blink);
                }
                horizontalSpring.Step(horizontal, deltaTime, movement.response);
                verticalSpring.Step(vertical, deltaTime, movement.response);
                scaleSpring.Step(blink, deltaTime, scale.response);
                squashSpring.Step(blink, deltaTime, squash.response);
            }
            else
            {
                horizontalSpring.Settle(deltaTime, movement.response);
                verticalSpring.Settle(deltaTime, movement.response);
                scaleSpring.Settle(deltaTime, scale.response);
                squashSpring.Settle(deltaTime, squash.response);
            }
            hadInput = hasInput;
            MovementOffset = new Vector2(horizontalSpring.WeightOffset(movement.response), verticalSpring.WeightOffset(movement.response));
            ScaleOffset = scaleSpring.WeightOffset(scale.response);
            SquashOffset = squashSpring.WeightOffset(squash.response);

            if (!hasInput && horizontalSpring.IsSettled && verticalSpring.IsSettled && scaleSpring.IsSettled && squashSpring.IsSettled)
            {
                ReleaseBindings();
                ClearSprings();
                status = inputStatus;
                return false;
            }

            try
            {
                // Reserve every binding before touching any output. Declared IR targets are
                // never valid overlay destinations, even when their current value is zero.
                foreach (var binding in bindings)
                {
                    if (irController != null && irController.IsTargetBound(binding.renderer, binding.index))
                        throw new InvalidOperationException("果冻输出与 IR 写入重叠：" + binding.key);
                    binding.Reserve(this);
                }
                foreach (var binding in bindings)
                {
                    float value = binding.axis == 0 ? MovementOffset.x : binding.axis == 1 ? MovementOffset.y
                        : binding.axis == 2 ? ScaleOffset : SquashOffset;
                    binding.Write(Mathf.Max(0, value * binding.direction));
                }
            }
            catch (Exception exception)
            {
                ReleaseBindings(); ClearSprings();
                status = "果冻写入失败：" + exception.Message;
                return false;
            }
            status = hasInput ? "运行中" : "IR 输出失效，正在归中";
            if (hasInput && missingOutputKeys.Count > 0) status += "（部分输出键缺失）";
            return true;
        }

        private bool TryReadInputs(out float horizontal, out float vertical, out float blink)
        {
            horizontal = vertical = blink = 0;
            missingInputKeys.Clear();
            frame.Clear();
            inputStatus = "等待 IR 输出（已归中）";
            if (irController == null || !irController.HasOutputFrame) return false;
            irController.CopyFinalWeightsTo(frame);
            bool configured = false;
            foreach (string key in inputs.blinkKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                configured = true;
                blink = Mathf.Max(blink, Read(key));
            }
            horizontal = Read(inputs.lookRight) - Read(inputs.lookLeft);
            vertical = Read(inputs.lookUp) - Read(inputs.lookDown);
            configured |= !string.IsNullOrEmpty(inputs.lookRight) || !string.IsNullOrEmpty(inputs.lookLeft)
                || !string.IsNullOrEmpty(inputs.lookUp) || !string.IsNullOrEmpty(inputs.lookDown);
            if (!configured) inputStatus = "未配置 IR 输入形态键";
            else if (missingInputKeys.Count > 0) inputStatus = "输入键不存在或未绑定：" + string.Join("、", missingInputKeys);
            return configured && missingInputKeys.Count == 0;
        }

        private float Read(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            if (frame.TryGetValue(key, out float value)) return value / 100f;
            if (!missingInputKeys.Contains(key)) missingInputKeys.Add(key);
            return 0;
        }

        private bool ConfigurationMatches()
        {
            if (!configurationLoaded || loadedController != irController
                || (irController != null && loadedBindingRevision != irController.BindingRevision)
                || targetMeshes == null || loadedRenderers.Count != targetMeshes.Count
                || inputs == null || inputs.blinkKeys == null || movement == null || scale == null || squash == null
                || loadedInputNames.Count != inputs.blinkKeys.Count + 4) return false;
            for (int i = 0; i < targetMeshes.Count; i++)
                if (targetMeshes[i] == null || loadedRenderers[i] != targetMeshes[i]
                    || loadedMeshes[i] != targetMeshes[i].sharedMesh) return false;
            for (int i = 0; i < loadedInputNames.Count; i++)
                if (!string.Equals(loadedInputNames[i], InputName(i), StringComparison.Ordinal)) return false;
            for (int i = 0; i < 8; i++)
                if (!string.Equals(loadedOutputNames[i], OutputName(i), StringComparison.Ordinal)) return false;
            return true;
        }

        private void BuildBindings()
        {
            Stop();
            inputs = inputs ?? new HoVtsJellyInputs();
            inputs.blinkKeys = inputs.blinkKeys ?? new List<string>();
            movement = movement ?? new HoVtsJellyMovement();
            scale = scale ?? new HoVtsJellyScale();
            squash = squash ?? new HoVtsJellySquash();
            loadedController = irController;
            loadedBindingRevision = irController != null ? irController.BindingRevision : 0;
            if (targetMeshes != null) foreach (var renderer in targetMeshes)
            {
                loadedRenderers.Add(renderer); loadedMeshes.Add(renderer != null ? renderer.sharedMesh : null);
            }
            for (int i = 0; i < inputs.blinkKeys.Count + 4; i++) loadedInputNames.Add(InputName(i));
            for (int i = 0; i < 8; i++) loadedOutputNames[i] = OutputName(i);
            configurationLoaded = true;
            try
            {
                if (irController == null) throw new InvalidOperationException("未指定 IR 组件。");
                if (targetMeshes == null || targetMeshes.Count == 0) throw new InvalidOperationException("未指定驱动网格列表。");
                var renderers = new HashSet<SkinnedMeshRenderer>();
                var keys = new HashSet<(SkinnedMeshRenderer, int)>();
                foreach (var renderer in targetMeshes)
                {
                    if (renderer == null || renderer.sharedMesh == null) throw new InvalidOperationException("驱动网格列表含空对象或未指定 Mesh。");
                    if (!renderers.Add(renderer)) throw new InvalidOperationException("驱动网格重复：" + renderer.name);
                }
                for (int slot = 0; slot < 8; slot++)
                {
                    string key = loadedOutputNames[slot];
                    if (string.IsNullOrEmpty(key)) continue;
                    bool found = false;
                    foreach (var renderer in targetMeshes)
                    {
                        int index = renderer.sharedMesh.GetBlendShapeIndex(key);
                        if (index < 0) continue;
                        if (!keys.Add((renderer, index))) throw new InvalidOperationException("多个果冻方向使用同一输出键：" + key);
                        bindings.Add(new OutputBinding(renderer, index, key, slot / 2, slot % 2 == 0 ? 1f : -1f));
                        found = true;
                    }
                    if (!found) missingOutputKeys.Add(key);
                }
                if (bindings.Count == 0) throw new InvalidOperationException("未找到果冻输出形态键；空方向会跳过，请至少配置一个存在的键。");
                bindingsValid = true;
                status = "等待 IR 输出";
            }
            catch (Exception exception) { bindingError = exception.Message; bindingsValid = false; }
        }

        private string InputName(int index)
        {
            int count = inputs.blinkKeys.Count;
            if (index < count) return inputs.blinkKeys[index];
            switch (index - count) { case 0: return inputs.lookLeft; case 1: return inputs.lookRight; case 2: return inputs.lookUp; default: return inputs.lookDown; }
        }

        private string OutputName(int slot)
        {
            switch (slot)
            {
                case 0: return movement.right; case 1: return movement.left;
                case 2: return movement.up; case 3: return movement.down;
                case 4: return scale.enlarge; case 5: return scale.shrink;
                case 6: return squash.stretch; default: return squash.squash;
            }
        }

        public void ResetSprings()
        {
            ReleaseBindings(); ClearSprings(); status = "已复位";
        }

        public void Stop()
        {
            ReleaseBindings(); ClearSprings();
            bindings.Clear(); loadedRenderers.Clear(); loadedMeshes.Clear(); loadedInputNames.Clear();
            missingInputKeys.Clear(); missingOutputKeys.Clear(); frame.Clear();
            configurationLoaded = bindingsValid = false; loadedController = null; bindingError = null;
            status = "已停止";
        }

        private void ReleaseBindings()
        {
            foreach (var binding in bindings) binding.Restore(this);
            HoFaceOutputOwnership.Release(this);
        }

        private void ClearSprings()
        {
            horizontalSpring.Reset(); verticalSpring.Reset(); scaleSpring.Reset(); squashSpring.Reset();
            hadInput = false; MovementOffset = Vector2.zero; ScaleOffset = SquashOffset = 0;
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private static float SafeGain(float value) => Finite(value) ? Mathf.Clamp(value, -1, 1) : 0;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class OutputBinding
        {
            public readonly SkinnedMeshRenderer renderer;
            public readonly int index, axis;
            public readonly string key;
            public readonly float direction;
            private readonly Mesh mesh;
            private float initial, lastWritten;
            private bool written;

            public OutputBinding(SkinnedMeshRenderer renderer, int index, string key, int axis, float direction)
            {
                this.renderer = renderer; this.index = index; this.key = key; this.axis = axis; this.direction = direction;
                mesh = renderer.sharedMesh;
            }

            public void Reserve(object owner)
            {
                HoFaceOutputOwnership.Reserve(renderer, index, owner);
                if (!written)
                {
                    initial = renderer.GetBlendShapeWeight(index);
                    if (float.IsNaN(initial) || float.IsInfinity(initial)) initial = 0;
                }
            }

            public void Write(float positiveOffset)
            {
                // Keep the captured neutral exact, including nonzero model baselines.
                // Only the added deformation uses the remaining 0..100 headroom.
                lastWritten = initial + Mathf.Min(positiveOffset, Mathf.Max(0, 100f - initial));
                renderer.SetBlendShapeWeight(index, lastWritten);
                written = true;
            }

            public void Restore(object owner)
            {
                if (written && renderer != null && renderer.sharedMesh == mesh
                    && HoFaceOutputOwnership.IsReservedBy(renderer, index, owner)
                    && Mathf.Approximately(renderer.GetBlendShapeWeight(index), lastWritten))
                    renderer.SetBlendShapeWeight(index, initial);
                written = false;
            }
        }
    }
}
