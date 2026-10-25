using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints
{
    public sealed partial class HoAuxRig
    {
        [Serializable] public sealed class DirectSource
        {
            public Transform bone;
            public string path;
            public float weight;
            public Matrix4x4 inverseBind = Matrix4x4.identity;
        }

        [Serializable] public sealed class DirectOperation
        {
            public string type, ownerPath, targetPath, upPath;
            public int stackIndex;
            public Transform owner, target, up;
            public float weight = 1;
            public bool positionOnly;
            public Vector3 bindLocalPosition, bindLocalScale = Vector3.one;
            public Quaternion bindLocalRotation = Quaternion.identity;
            public Quaternion rotationCorrection = Quaternion.identity;
            public Vector3 aimAxis = Vector3.forward, upAxis = Vector3.up;
            public List<DirectSource> sources = new List<DirectSource>();
        }

        [SerializeField] private Transform directSpace;
        [SerializeField] private string directSpacePath;
        [SerializeField] private List<DirectOperation> directOperations = new List<DirectOperation>();
        private readonly HashSet<string> directWarnings = new HashSet<string>();
        public IList<DirectOperation> DirectOperations { get { return directOperations; } }
        public string LastDirectError { get; private set; }

        public void SetDirectSpace(Transform value)
        {
            directSpace = value;
            directSpacePath = GetOperationPath(RigRoot, value);
        }

        public void AddDirectOperation(DirectOperation operation)
        {
            if (operation == null || operation.owner == null) throw new ArgumentException("Direct owner missing");
            operation.ownerPath = GetOperationPath(RigRoot, operation.owner);
            operation.targetPath = GetOperationPath(RigRoot, operation.target);
            operation.upPath = GetOperationPath(RigRoot, operation.up);
            foreach (DirectSource source in operation.sources)
                source.path = GetOperationPath(RigRoot, source.bone);
            directOperations.Add(operation);
        }

        private void ResolveDirectReferences()
        {
            if (directSpace == null && !string.IsNullOrEmpty(directSpacePath)) directSpace = FindRelativeTransform(directSpacePath);
            foreach (DirectOperation op in directOperations)
            {
                if (op.owner == null && !string.IsNullOrEmpty(op.ownerPath)) op.owner = FindRelativeTransform(op.ownerPath);
                if (op.target == null && !string.IsNullOrEmpty(op.targetPath)) op.target = FindRelativeTransform(op.targetPath);
                if (op.up == null && !string.IsNullOrEmpty(op.upPath)) op.up = FindRelativeTransform(op.upPath);
                foreach (DirectSource source in op.sources)
                    if (source.bone == null && !string.IsNullOrEmpty(source.path)) source.bone = FindRelativeTransform(source.path);
            }
        }

        private void RestoreDirectOwners()
        {
            var seen = new HashSet<Transform>();
            foreach (DirectOperation op in directOperations)
            {
                if (op.owner == null || !seen.Add(op.owner)) continue;
                op.owner.localPosition = op.bindLocalPosition;
                op.owner.localRotation = op.bindLocalRotation;
                op.owner.localScale = op.bindLocalScale;
            }
        }

        private void DirectError(string message)
        {
            LastDirectError = message;
            if (directWarnings.Add(message)) Debug.LogWarning("HoAuxRig direct: " + message, this);
        }

        private void EvaluateDirectOperations()
        {
            if (directOperations.Count == 0) return;
            ResolveDirectReferences();
            if (directSpace == null) { DirectError("Missing rig-space transform"); return; }
            LastDirectError = null;
            var groups = new Dictionary<Transform, List<DirectOperation>>();
            foreach (DirectOperation op in directOperations)
            {
                if (op.owner == null) { DirectError("Missing owner reference"); return; }
                if (!groups.TryGetValue(op.owner, out List<DirectOperation> list)) groups.Add(op.owner, list = new List<DirectOperation>());
                list.Add(op);
            }
            foreach (var list in groups.Values) list.Sort((a,b) => a.stackIndex.CompareTo(b.stackIndex));
            var order = new List<Transform>(); var visiting = new HashSet<Transform>(); var done = new HashSet<Transform>();
            foreach (Transform owner in groups.Keys)
                if (!VisitDirect(owner, groups, visiting, done, order)) { DirectError("Cyclic direct-constraint dependency"); return; }
            var values = new Dictionary<Transform, Matrix4x4>();
            Matrix4x4 toWorld = directSpace.localToWorldMatrix;
            foreach (Transform owner in order)
            {
                var ops = groups[owner]; var first = ops[0];
                Matrix4x4 parent = ReadDirect(owner.parent, values);
                Matrix4x4 current = parent * Matrix4x4.TRS(first.bindLocalPosition, first.bindLocalRotation, first.bindLocalScale);
                bool valid = true;
                foreach (DirectOperation op in ops)
                {
                    Vector3 p = current.GetColumn(3);
                    if (op.type == "COPY_LOCATION")
                    {
                        if (op.target == null) { valid = false; break; }
                        current.SetColumn(3, new Vector4(p.x, p.y, p.z, 1));
                        Vector3 result = Vector3.Lerp(p, ReadDirect(op.target, values).GetColumn(3), op.weight);
                        current.SetColumn(3, new Vector4(result.x,result.y,result.z,1));
                    }
                    else if (op.type == "COPY_ROTATION")
                    {
                        if (op.target == null) { valid = false; break; }
                        if(!TryDirectTrs(current,out _,out Quaternion before,out Vector3 size)||
                           !TryDirectTrs(ReadDirect(op.target,values),out _,out Quaternion sourceRotation,out _))
                        {valid=false;DirectError("Copy Rotation needs non-degenerate TRS input: "+owner.name);break;}
                        Quaternion q = sourceRotation * op.rotationCorrection;
                        current = Matrix4x4.TRS(p, Quaternion.Slerp(before, q, op.weight), size);
                    }
                    else if (op.type == "ARMATURE")
                    {
                        Matrix4x4 delta = Matrix4x4.zero; float total = 0;
                        foreach (DirectSource source in op.sources)
                        {
                            if (source.bone == null) { valid = false; break; }
                            Matrix4x4 d = ReadDirect(source.bone, values) * source.inverseBind;
                            for (int i=0;i<16;i++) delta[i] += d[i] * source.weight;
                            total += source.weight;
                        }
                        if (!valid || total <= 0) { valid = false; break; }
                        for (int i=0;i<16;i++) delta[i] /= total;
                        current = delta * current;
                    }
                    else if (op.type == "AIM")
                    {
                        if (op.target == null || op.up == null) { valid = false; break; }
                        if(!TryDirectTrs(current,out _,out _,out Vector3 aimScale))
                        {valid=false;DirectError("Aim needs non-degenerate TRS input: "+owner.name);break;}
                        Vector3 aim = (Vector3)ReadDirect(op.target, values).GetColumn(3) - p;
                        Vector3 up = (Vector3)ReadDirect(op.up, values).GetColumn(3) - p;
                        if (aim.sqrMagnitude < 1e-12f || Vector3.Cross(aim,up).sqrMagnitude < 1e-16f)
                        { valid = false; DirectError("Degenerate Aim: " + owner.name); break; }
                        Quaternion q = Quaternion.LookRotation(aim,up) * Quaternion.Inverse(Quaternion.LookRotation(op.aimAxis,op.upAxis));
                        current = Matrix4x4.TRS(p,q,aimScale);
                    }
                    else { valid = false; break; }
                }
                if (!valid) { DirectError("Invalid direct operation on " + owner.name); continue; }
                values[owner] = current;
                if (ops.Count == 1 && first.type == "ARMATURE" && first.positionOnly)
                { owner.position = toWorld.MultiplyPoint3x4(current.GetColumn(3)); continue; }
                Matrix4x4 local = parent.inverse * current;
                if (!TryDirectTrs(local, out Vector3 pos, out Quaternion rot, out Vector3 scale))
                { DirectError("Non-TRS affine result cannot be written exactly: " + owner.name); continue; }
                owner.localPosition = pos; owner.localRotation = rot; owner.localScale = scale;
            }
        }

        private Matrix4x4 ReadDirect(Transform value, Dictionary<Transform, Matrix4x4> values)
        {
            if (value == null) return directSpace.worldToLocalMatrix;
            if (value == directSpace) return Matrix4x4.identity;
            if (values.TryGetValue(value, out Matrix4x4 matrix)) return matrix;
            if (value.IsChildOf(directSpace))
                return ReadDirect(value.parent, values) * Matrix4x4.TRS(value.localPosition,value.localRotation,value.localScale);
            return directSpace.worldToLocalMatrix * value.localToWorldMatrix;
        }

        private static bool VisitDirect(Transform owner, Dictionary<Transform,List<DirectOperation>> groups,
            HashSet<Transform> visiting, HashSet<Transform> done, List<Transform> order)
        {
            if (done.Contains(owner)) return true;
            if (!visiting.Add(owner)) return false;
            var dependencies = new HashSet<Transform>();
            for (Transform p=owner.parent;p!=null;p=p.parent) if(groups.ContainsKey(p)) { dependencies.Add(p); break; }
            foreach (DirectOperation op in groups[owner])
            {
                if(op.target!=null) dependencies.Add(op.target);
                if(op.up!=null) dependencies.Add(op.up);
                foreach(DirectSource source in op.sources) if(source.bone!=null) dependencies.Add(source.bone);
            }
            foreach(Transform d in dependencies)
            {
                for(Transform p=d;p!=null;p=p.parent)
                    if(groups.ContainsKey(p)) { if(!VisitDirect(p,groups,visiting,done,order))return false; break; }
            }
            visiting.Remove(owner);done.Add(owner);order.Add(owner);return true;
        }

        private static Vector3 DirectScale(Matrix4x4 m)
        { return new Vector3(m.GetColumn(0).magnitude,m.GetColumn(1).magnitude,m.GetColumn(2).magnitude); }

        public static bool TryDirectTrs(Matrix4x4 m, out Vector3 p, out Quaternion q, out Vector3 s)
        {
            p=m.GetColumn(3);s=DirectScale(m);q=Quaternion.identity;
            if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||!Finite(s.x)||!Finite(s.y)||!Finite(s.z)||Mathf.Min(s.x,Mathf.Min(s.y,s.z))<1e-8f||m.determinant<=0)return false;
            Vector3 x=(Vector3)m.GetColumn(0)/s.x,y=(Vector3)m.GetColumn(1)/s.y,z=(Vector3)m.GetColumn(2)/s.z;
            if(Mathf.Abs(Vector3.Dot(x,y))>0.0005f||Mathf.Abs(Vector3.Dot(x,z))>0.0005f||Mathf.Abs(Vector3.Dot(y,z))>0.0005f)return false;
            q=Quaternion.LookRotation(z,y);return true;
        }
        private static bool Finite(float f) { return !float.IsNaN(f)&&!float.IsInfinity(f); }
    }
}
