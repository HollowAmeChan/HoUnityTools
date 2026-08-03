using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints
{
    /// <summary>
    /// HoAux 专用 Rig 中控。
    ///
    /// 这是一个完整的运行时组件：层、操作、绑定姿态和执行顺序都保存在
    /// 同一个组件中，不创建 Animation Rigging 的 Rig/Proxy 空物体，也不依赖
    /// 现有的通用约束组件。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    [AddComponentMenu("HoUnityTools/Rig/Ho Aux Rig")]
    public sealed class HoAuxRig : MonoBehaviour
    {
        public enum OperationType
        {
            Parent,
            Twist,
            Fan,
        }

        public enum Space
        {
            World,
            Pose,
            LocalWithParent,
            Local,
            Custom,
            LocalOwnerOrient,
        }

        public enum UpdateMode
        {
            LateUpdate,
            Manual,
        }

        [Serializable]
        public sealed class Layer
        {
            public string name = "Layer";
            public bool enabled = true;
            public int order;
            public List<Operation> operations = new List<Operation>();
        }

        [Serializable]
        public sealed class Operation
        {
            public string id = string.Empty;
            public OperationType type;
            public bool enabled = true;
            public Transform owner;
            public Transform target;
            public string ownerPath = string.Empty;
            public string targetPath = string.Empty;
            public string sourceBone = string.Empty;

            [Range(0.0f, 1.0f)]
            public float weight = 1.0f;

            public Space sourceSpace = Space.World;
            public Space targetSpace = Space.World;
            public bool maintainOffset = true;
            public bool useX = true;
            public bool useY = true;
            public bool useZ = true;

            // Twist 对外仍是一条操作，这一组字段保存内部 Stretch To 阶段。
            public bool stretchEnabled = true;
            [Range(0.0f, 1.0f)]
            public float stretchWeight = 1.0f;
            public float stretchHeadTail;
            public float restLength;
            public string volume = "NO_VOLUME";
            public string keepAxis = "SWING_Y";
            public Space stretchSourceSpace = Space.World;
            public Space stretchTargetSpace = Space.World;

            // 绑定数据必须序列化；逐帧重算会让 Parent/Twist 累积自己的输出。
            [HideInInspector] public bool hasBindPose;
            [HideInInspector] public Vector3 bindOwnerPosition;
            [HideInInspector] public Vector3 bindOwnerLocalPosition;
            [HideInInspector] public Quaternion bindOwnerRotation = Quaternion.identity;
            [HideInInspector] public Quaternion bindOwnerLocalRotation = Quaternion.identity;
            [HideInInspector] public Vector3 bindOwnerLocalScale = Vector3.one;
            [HideInInspector] public Quaternion bindTargetRotation = Quaternion.identity;
            [HideInInspector] public Quaternion bindTargetLocalRotation = Quaternion.identity;
            [HideInInspector] public Quaternion targetToOwnerOrientation = Quaternion.identity;
            [HideInInspector] public Vector3 bindTargetLocalPosition;
            [HideInInspector] public Quaternion parentRotationOffset = Quaternion.identity;
        }

        [SerializeField] private Transform rigRoot;
        [SerializeField] private UpdateMode updateMode = UpdateMode.LateUpdate;
        [SerializeField] private bool evaluateInEditMode = true;
        [SerializeField] private bool evaluateOnEnable = true;
        [SerializeField] private string sourceArmature = string.Empty;
        [SerializeField] private string exporterVersion = string.Empty;
        [SerializeField] private string exportTime = string.Empty;
        [SerializeField] private List<Layer> layers = new List<Layer>();

        private bool needsBinding = true;

        public Transform RigRoot
        {
            get { return rigRoot != null ? rigRoot : transform; }
            set { rigRoot = value; needsBinding = true; }
        }

        public UpdateMode Mode
        {
            get { return updateMode; }
            set { updateMode = value; }
        }

        public bool EvaluateInEditMode
        {
            get { return evaluateInEditMode; }
            set { evaluateInEditMode = value; }
        }

        public string SourceArmature
        {
            get { return sourceArmature; }
            set { sourceArmature = value ?? string.Empty; }
        }

        public string ExporterVersion
        {
            get { return exporterVersion; }
            set { exporterVersion = value ?? string.Empty; }
        }

        public string ExportTime
        {
            get { return exportTime; }
            set { exportTime = value ?? string.Empty; }
        }

        public IList<Layer> Layers
        {
            get { return layers; }
        }

        private void OnEnable()
        {
            needsBinding = true;
            if (evaluateOnEnable)
                CaptureBindPose();
        }

        private void OnDisable()
        {
            RestoreOwnersToBindPose();
        }

        private void OnValidate()
        {
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                Layer layer = layers[layerIndex];
                if (layer == null || layer.operations == null)
                    continue;
                for (int operationIndex = 0; operationIndex < layer.operations.Count; operationIndex++)
                    ClampOperation(layer.operations[operationIndex]);
            }
            needsBinding = true;
        }

        private void LateUpdate()
        {
            if (updateMode == UpdateMode.LateUpdate)
                EvaluateNow();
        }

        /// <summary>手动执行一次全部启用层。Warudo/其他宿主可在自己的 PostLateUpdate 调用。</summary>
        public void EvaluateNow()
        {
            if (!enabled || (!Application.isPlaying && !evaluateInEditMode))
                return;

            ResolveReferences();
            if (needsBinding)
                CaptureBindPose();
            RestoreOwnersToBindPose();

            List<Layer> sortedLayers = new List<Layer>(layers);
            sortedLayers.Sort((left, right) =>
            {
                if (left == null) return 1;
                if (right == null) return -1;
                return left.order.CompareTo(right.order);
            });

            for (int layerIndex = 0; layerIndex < sortedLayers.Count; layerIndex++)
            {
                Layer layer = sortedLayers[layerIndex];
                if (layer == null || !layer.enabled || layer.operations == null)
                    continue;

                for (int operationIndex = 0; operationIndex < layer.operations.Count; operationIndex++)
                {
                    Operation operation = layer.operations[operationIndex];
                    if (operation == null || !operation.enabled ||
                        operation.owner == null || operation.target == null)
                        continue;
                    EvaluateOperation(operation);
                }
            }
        }

        /// <summary>重新以当前骨架姿态作为绑定姿态。</summary>
        public void CaptureBindPose()
        {
            ResolveReferences();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                Layer layer = layers[layerIndex];
                if (layer == null || layer.operations == null)
                    continue;
                for (int operationIndex = 0; operationIndex < layer.operations.Count; operationIndex++)
                    CaptureBindPose(layer.operations[operationIndex]);
            }
            needsBinding = false;
        }

        /// <summary>
        /// Restore every driven owner once before evaluating the layer stack.
        /// Aux bones are normally outside Animator clips; without this base reset,
        /// influence interpolation would start from the previous frame's output and
        /// asymptotically accumulate toward 100 percent.
        /// </summary>
        public void RestoreOwnersToBindPose()
        {
            var restored = new HashSet<Transform>();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                Layer layer = layers[layerIndex];
                if (layer == null || layer.operations == null)
                    continue;
                for (int operationIndex = 0; operationIndex < layer.operations.Count; operationIndex++)
                {
                    Operation operation = layer.operations[operationIndex];
                    if (operation == null || !operation.hasBindPose || operation.owner == null ||
                        !restored.Add(operation.owner))
                        continue;
                    operation.owner.localPosition = operation.bindOwnerLocalPosition;
                    operation.owner.localRotation = operation.bindOwnerLocalRotation;
                    operation.owner.localScale = operation.bindOwnerLocalScale;
                }
            }
        }

        /// <summary>删除本组件拥有的所有 Rig 操作，不会碰通用约束组件。</summary>
        public void ClearOperations()
        {
            layers.Clear();
            needsBinding = true;
        }

        public Layer GetOrCreateLayer(OperationType type)
        {
            string layerName = type.ToString();
            for (int index = 0; index < layers.Count; index++)
            {
                Layer existing = layers[index];
                if (existing != null && string.Equals(existing.name, layerName, StringComparison.Ordinal))
                    return existing;
            }

            Layer layer = new Layer
            {
                name = layerName,
                order = LayerOrder(type),
            };
            layers.Add(layer);
            return layer;
        }

        public Operation AddOperation(
            OperationType type,
            Transform owner,
            Transform target,
            float weight = 1.0f)
        {
            Layer layer = GetOrCreateLayer(type);
            Operation operation = new Operation
            {
                id = Guid.NewGuid().ToString("N"),
                type = type,
                owner = owner,
                target = target,
                ownerPath = GetRelativePath(RigRoot, owner),
                targetPath = GetRelativePath(RigRoot, target),
                weight = Mathf.Clamp01(weight),
            };
            if (type == OperationType.Fan)
            {
                operation.sourceSpace = Space.World;
                operation.targetSpace = Space.World;
            }
            layer.operations.Add(operation);
            needsBinding = true;
            return operation;
        }

        public int RemoveOperationsFromSource(string armature, string time, string version)
        {
            // 空来源代表手动配置：首次导入不能把它当成同一份导出结果清空。
            // 已由导入器写入来源后，只有同一骨架才允许重导入替换。
            if (string.IsNullOrEmpty(sourceArmature) && layers.Count > 0)
                return 0;

            bool matches = string.Equals(sourceArmature, armature, StringComparison.Ordinal) ||
                (string.IsNullOrEmpty(sourceArmature) && string.IsNullOrEmpty(armature));
            if (!matches)
                return 0;

            int removed = 0;
            for (int layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
            {
                Layer layer = layers[layerIndex];
                if (layer == null || layer.operations == null)
                    continue;
                removed += layer.operations.Count;
                layer.operations.Clear();
                if (layer.operations.Count == 0)
                    layers.RemoveAt(layerIndex);
            }
            sourceArmature = armature ?? string.Empty;
            exportTime = time ?? string.Empty;
            exporterVersion = version ?? string.Empty;
            needsBinding = true;
            return removed;
        }

        public static string GetRelativePath(Transform root, Transform value)
        {
            if (root == null || value == null)
                return string.Empty;
            if (value == root)
                return string.Empty;

            List<string> names = new List<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }
            if (current != root)
                return value.name;
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private void EvaluateOperation(Operation operation)
        {
            switch (operation.type)
            {
                case OperationType.Parent:
                    ApplyParent(operation);
                    break;
                case OperationType.Twist:
                    ApplyCopyRotation(operation);
                    if (operation.stretchEnabled)
                        ApplyStretchTo(operation);
                    break;
                case OperationType.Fan:
                    ApplyCopyRotation(operation);
                    break;
            }
        }

        private static void ApplyParent(Operation operation)
        {
            Transform owner = operation.owner;
            Transform target = operation.target;
            Vector3 desiredPosition = target.TransformPoint(operation.bindTargetLocalPosition);
            Quaternion desiredRotation = target.rotation * operation.parentRotationOffset;
            float weight = Mathf.Clamp01(operation.weight);
            owner.position = Vector3.Lerp(owner.position, desiredPosition, weight);
            owner.rotation = Quaternion.Slerp(owner.rotation, desiredRotation, weight);
        }

        private static void ApplyCopyRotation(Operation operation)
        {
            Transform owner = operation.owner;
            Transform target = operation.target;

            if (operation.sourceSpace == Space.Local &&
                (operation.targetSpace == Space.Local ||
                 operation.targetSpace == Space.LocalOwnerOrient))
            {
                Quaternion targetDelta = Quaternion.Inverse(operation.bindTargetLocalRotation) *
                    target.localRotation;
                Quaternion mappedDelta = operation.targetToOwnerOrientation * targetDelta *
                    Quaternion.Inverse(operation.targetToOwnerOrientation);
                Quaternion desiredLocalRotation = operation.bindOwnerLocalRotation * mappedDelta;
                owner.localRotation = Quaternion.Slerp(
                    owner.localRotation,
                    desiredLocalRotation,
                    Mathf.Clamp01(operation.weight));
                return;
            }

            Quaternion desiredRotation = target.rotation *
                Quaternion.Inverse(operation.bindTargetRotation) * operation.bindOwnerRotation;
            desiredRotation = ApplyAxisMask(operation, desiredRotation);
            owner.rotation = Quaternion.Slerp(
                owner.rotation,
                desiredRotation,
                Mathf.Clamp01(operation.weight));
        }

        private static Quaternion ApplyAxisMask(Operation operation, Quaternion desiredRotation)
        {
            if (operation.useX && operation.useY && operation.useZ)
                return desiredRotation;
            if (!operation.useX && !operation.useY && !operation.useZ)
                return operation.bindOwnerRotation;

            Quaternion delta = desiredRotation * Quaternion.Inverse(operation.bindOwnerRotation);
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (axis.sqrMagnitude < 1e-8f || Mathf.Abs(angle) < 1e-5f)
                return operation.bindOwnerRotation;

            Vector3 localAxis = Quaternion.Inverse(operation.bindTargetRotation) * axis;
            localAxis = new Vector3(
                operation.useX ? localAxis.x : 0.0f,
                operation.useY ? localAxis.y : 0.0f,
                operation.useZ ? localAxis.z : 0.0f);
            if (localAxis.sqrMagnitude < 1e-8f)
                return operation.bindOwnerRotation;
            Vector3 worldAxis = operation.bindTargetRotation * localAxis.normalized;
            return Quaternion.AngleAxis(angle, worldAxis) * operation.bindOwnerRotation;
        }

        private static void ApplyStretchTo(Operation operation)
        {
            Transform owner = operation.owner;
            Transform target = operation.target;
            Vector3 toTarget = target.position - owner.position;
            float distance = toTarget.magnitude;
            if (distance <= 1e-6f)
                return;

            // Blender 默认的 keep_axis=SWING_Y：让骨骼局部 Y 指向目标，
            // 同时保留上一步 Copy Rotation 已经产生的扭转分量。
            if (string.IsNullOrEmpty(operation.keepAxis) ||
                string.Equals(operation.keepAxis, "SWING_Y", StringComparison.OrdinalIgnoreCase))
            {
                Quaternion swing = Quaternion.FromToRotation(
                    owner.rotation * Vector3.up,
                    toTarget / distance);
                Quaternion desiredRotation = swing * owner.rotation;
                owner.rotation = Quaternion.Slerp(
                    owner.rotation,
                    desiredRotation,
                    Mathf.Clamp01(operation.stretchWeight));
            }

            float restLength = operation.restLength;
            if (restLength <= 1e-6f)
                restLength = Vector3.Distance(operation.bindOwnerPosition, operation.target.position);
            if (restLength <= 1e-6f)
                return;

            Vector3 scale = owner.localScale;
            scale.y = operation.bindOwnerLocalScale.y * distance / restLength;
            if (!string.Equals(operation.volume, "NO_VOLUME", StringComparison.OrdinalIgnoreCase))
            {
                float reciprocal = 1.0f / Mathf.Sqrt(Mathf.Max(distance / restLength, 1e-6f));
                scale.x = operation.bindOwnerLocalScale.x * reciprocal;
                scale.z = operation.bindOwnerLocalScale.z * reciprocal;
            }
            owner.localScale = scale;
        }

        private void ResolveReferences()
        {
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                Layer layer = layers[layerIndex];
                if (layer == null || layer.operations == null)
                    continue;
                for (int operationIndex = 0; operationIndex < layer.operations.Count; operationIndex++)
                {
                    Operation operation = layer.operations[operationIndex];
                    if (operation == null)
                        continue;
                    if (operation.owner == null && !string.IsNullOrEmpty(operation.ownerPath))
                        operation.owner = FindRelativeTransform(operation.ownerPath);
                    if (operation.target == null && !string.IsNullOrEmpty(operation.targetPath))
                        operation.target = FindRelativeTransform(operation.targetPath);
                }
            }
        }

        private Transform FindRelativeTransform(string path)
        {
            Transform root = RigRoot;
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(path) || path == ".")
                return root;
            Transform result = root.Find(path);
            if (result != null)
                return result;
            return FindByName(root, path);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindByName(root.GetChild(index), name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static void CaptureBindPose(Operation operation)
        {
            if (operation == null || operation.owner == null || operation.target == null)
                return;
            operation.bindOwnerPosition = operation.owner.position;
            operation.bindOwnerLocalPosition = operation.owner.localPosition;
            operation.bindOwnerRotation = operation.owner.rotation;
            operation.bindOwnerLocalRotation = operation.owner.localRotation;
            operation.bindOwnerLocalScale = operation.owner.localScale;
            operation.bindTargetRotation = operation.target.rotation;
            operation.bindTargetLocalRotation = operation.target.localRotation;
            operation.targetToOwnerOrientation = Quaternion.Inverse(operation.bindOwnerRotation) *
                operation.bindTargetRotation;
            if (operation.maintainOffset)
            {
                operation.bindTargetLocalPosition = operation.target.InverseTransformPoint(operation.owner.position);
                operation.parentRotationOffset = Quaternion.Inverse(operation.target.rotation) *
                    operation.owner.rotation;
            }
            else
            {
                operation.bindTargetLocalPosition = Vector3.zero;
                operation.parentRotationOffset = Quaternion.identity;
            }
            if (operation.type == OperationType.Twist && operation.restLength <= 1e-6f)
                operation.restLength = Vector3.Distance(operation.owner.position, operation.target.position);
            operation.hasBindPose = true;
            ClampOperation(operation);
        }

        private static void ClampOperation(Operation operation)
        {
            if (operation == null)
                return;
            operation.weight = Mathf.Clamp01(operation.weight);
            operation.stretchWeight = Mathf.Clamp01(operation.stretchWeight);
            operation.restLength = Mathf.Max(0.0f, operation.restLength);
        }

        private static int LayerOrder(OperationType type)
        {
            switch (type)
            {
                case OperationType.Parent: return 0;
                case OperationType.Twist: return 100;
                case OperationType.Fan: return 200;
                default: return 1000;
            }
        }
    }
}
