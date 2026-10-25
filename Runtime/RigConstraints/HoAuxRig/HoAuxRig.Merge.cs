using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints
{
    public sealed partial class HoAuxRig
    {
        // Object-reference remapping alone does not move the direct stack's rig
        // space, stored bind poses or path fallback. Capture those before deletion.
        public Action PrepareArmatureMerge(IReadOnlyDictionary<Transform, Transform> replacements)
        {
            ResolveReferences();
            ResolveDirectReferences();
            if (!MergeTouchesRig(replacements)) return null;
            Transform nextRoot = Remapped(RigRoot, replacements);
            Transform nextSpace = Remapped(directSpace, replacements);
            // Importers may use the FBX wrapper as rig space while the merge
            // marker lives on its Armature child. That wrapper stays behind too.
            if (nextSpace == directSpace && directSpace != null)
                foreach (var pair in replacements)
                    if ((pair.Key.parent == null || !replacements.ContainsKey(pair.Key.parent)) &&
                        pair.Key != directSpace && pair.Key.IsChildOf(directSpace) &&
                        (pair.Value == pair.Key || !pair.Value.IsChildOf(directSpace)))
                    { nextSpace = pair.Value; break; }
            Matrix4x4 spaceChange = directSpace != null && nextSpace != null
                ? nextSpace.worldToLocalMatrix * directSpace.localToWorldMatrix
                : Matrix4x4.identity;
            var finish = new List<Action>();
            foreach (Layer layer in layers)
            {
                if (layer == null || layer.operations == null) continue;
                foreach (Operation op in layer.operations)
                {
                    if (op == null) continue;
                    Transform owner = Remapped(op.owner, replacements), target = Remapped(op.target, replacements);
                    Matrix4x4 parentBefore = ParentWorld(op.owner);
                    Matrix4x4 bindWorld = BindWorld(op.owner, op.bindOwnerLocalPosition, op.bindOwnerLocalRotation, op.bindOwnerLocalScale);
                    Quaternion targetBindWorld = (op.target != null && op.target.parent != null ? op.target.parent.rotation : Quaternion.identity) * op.bindTargetLocalRotation;
                    finish.Add(() => {
                        op.owner = owner; op.target = target;
                        if (owner != null && op.hasBindPose && !SameFrame(parentBefore, ParentWorld(owner)))
                        {
                            Matrix4x4 local = (owner.parent != null ? owner.parent.worldToLocalMatrix : Matrix4x4.identity) * bindWorld;
                            RequireBindTrs(local, out op.bindOwnerLocalPosition, out op.bindOwnerLocalRotation, out op.bindOwnerLocalScale);
                        }
                        if (target != null && op.hasBindPose)
                            op.bindTargetLocalRotation = (target.parent != null ? Quaternion.Inverse(target.parent.rotation) : Quaternion.identity) * targetBindWorld;
                        op.ownerPath = GetOperationPath(RigRoot, owner);
                        op.targetPath = GetOperationPath(RigRoot, target);
                    });
                }
            }
            foreach (DirectOperation op in directOperations)
            {
                if (op == null) continue;
                Transform owner = Remapped(op.owner, replacements), target = Remapped(op.target, replacements), up = Remapped(op.up, replacements);
                Matrix4x4 parentBefore = ParentWorld(op.owner);
                Matrix4x4 bindWorld = BindWorld(op.owner, op.bindLocalPosition, op.bindLocalRotation, op.bindLocalScale);
                foreach (DirectSource source in op.sources)
                {
                    Transform bone = Remapped(source.bone, replacements);
                    Matrix4x4 inverse = source.inverseBind * spaceChange.inverse;
                    finish.Add(() => { source.bone = bone; source.inverseBind = inverse; source.path = GetOperationPath(RigRoot, bone); });
                }
                finish.Add(() => {
                    op.owner = owner; op.target = target; op.up = up;
                    if (owner != null && !SameFrame(parentBefore, ParentWorld(owner)))
                    {
                        Matrix4x4 local = (owner.parent != null ? owner.parent.worldToLocalMatrix : Matrix4x4.identity) * bindWorld;
                        RequireBindTrs(local, out op.bindLocalPosition, out op.bindLocalRotation, out op.bindLocalScale);
                    }
                    op.ownerPath = GetOperationPath(RigRoot, owner);
                    op.targetPath = GetOperationPath(RigRoot, target);
                    op.upPath = GetOperationPath(RigRoot, up);
                });
            }
            return () => {
                rigRoot = nextRoot;
                directSpace = nextSpace;
                directSpacePath = GetOperationPath(RigRoot, directSpace);
                foreach (Action apply in finish) apply();
                needsBinding = !HasCompleteBindPose();
                directWarnings.Clear(); LastDirectError = null;
            };
        }

        private static Transform Remapped(Transform value, IReadOnlyDictionary<Transform, Transform> replacements)
        { return value != null && replacements.TryGetValue(value, out Transform next) ? next : value; }

        private bool MergeTouchesRig(IReadOnlyDictionary<Transform, Transform> replacements)
        {
            if (Touches(RigRoot, replacements) || Touches(directSpace, replacements)) return true;
            foreach (Layer layer in layers)
                if (layer != null && layer.operations != null)
                    foreach (Operation op in layer.operations)
                        if (op != null && (Touches(op.owner, replacements) || Touches(op.target, replacements))) return true;
            foreach (DirectOperation op in directOperations)
                if (op != null)
                {
                    if (Touches(op.owner, replacements) || Touches(op.target, replacements) || Touches(op.up, replacements)) return true;
                    foreach (DirectSource source in op.sources)
                        if (source != null && Touches(source.bone, replacements)) return true;
                }
            return false;
        }

        private static bool Touches(Transform value, IReadOnlyDictionary<Transform, Transform> replacements)
        {
            for (Transform t = value; t != null; t = t.parent)
                if (replacements.ContainsKey(t)) return true;
            return false;
        }

        private static Matrix4x4 ParentWorld(Transform owner)
        { return owner != null && owner.parent != null ? owner.parent.localToWorldMatrix : Matrix4x4.identity; }

        private static bool SameFrame(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > 1e-5f * Mathf.Max(1, Mathf.Abs(a[i]), Mathf.Abs(b[i]))) return false;
            return true;
        }

        private static Matrix4x4 BindWorld(Transform owner, Vector3 position, Quaternion rotation, Vector3 scale)
        { return ParentWorld(owner) * Matrix4x4.TRS(position, rotation, scale); }

        private static void RequireBindTrs(Matrix4x4 local, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (!TryDirectTrs(local, out position, out rotation, out scale))
                throw new InvalidOperationException("HoAuxRig 合并后的绑定姿态不能表示为 TRS；请保留骨架并使用约束模式。");
        }
    }
}
