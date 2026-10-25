using UnityEngine;

namespace Hollow.HoUnityTools.TransformConstraints
{
    internal static class HoTransformConstraintMath
    {
        internal static Vector3 Axis(HoSignedAxis axis)
        {
            switch(axis)
            {
                case HoSignedAxis.PositiveX:return Vector3.right;
                case HoSignedAxis.NegativeX:return Vector3.left;
                case HoSignedAxis.PositiveY:return Vector3.up;
                case HoSignedAxis.NegativeY:return Vector3.down;
                case HoSignedAxis.PositiveZ:return Vector3.forward;
                default:return Vector3.back;
            }
        }

        internal static bool TryAimRotation(HoAimConstraint c,out Quaternion rotation,out string error)
        {
            rotation=Quaternion.identity;error=null;
            Vector3 targetPoint=Position(c);Vector3 direction=targetPoint-c.TargetTransform.position;
            Vector3 up=c.UpMode==HoAimUpMode.SceneUp?Vector3.up:c.WorldUp;
            if(c.UpMode==HoAimUpMode.ObjectPosition)up=c.UpObject.position-c.TargetTransform.position;
            if(c.UpMode==HoAimUpMode.ObjectRotation)up=c.UpObject.TransformDirection(c.WorldUp);
            if(!HoTransformConstraint.Finite(direction)||!HoTransformConstraint.Finite(up)||direction.sqrMagnitude<1e-12f||Vector3.Cross(direction.normalized,up).sqrMagnitude<1e-12f)
            {error="Aim 目标重合或上方向退化";return false;}
            rotation=Quaternion.LookRotation(direction,up)*Quaternion.Inverse(Quaternion.LookRotation(Axis(c.AimAxis),Axis(c.UpAxis)));
            return true;
        }

        private static Vector3 Position(HoTransformConstraint c)
        {
            Vector3 result=Vector3.zero;double total=Total(c);
            foreach(var s in c.Sources)
            {
                if(s.weight==0)continue;
                Vector3 p=c.Space==HoTransformConstraintSpace.World?s.transform.TransformPoint(s.positionOffset):
                    Matrix4x4.TRS(s.transform.localPosition,s.transform.localRotation,s.transform.localScale).MultiplyPoint3x4(s.positionOffset);
                result+=p*(float)(s.weight/total);
            }
            return result;
        }
        private static Quaternion Rotation(HoTransformConstraint c)
        {
            Quaternion result=Quaternion.identity;double accumulated=0;
            foreach(var s in c.Sources)
            {
                if(s.weight==0)continue;
                Quaternion q=(c.Space==HoTransformConstraintSpace.World?s.transform.rotation:s.transform.localRotation)*Quaternion.Euler(s.rotationOffset);
                result=accumulated==0?q:Quaternion.Slerp(result,q,(float)(s.weight/(accumulated+s.weight)));
                accumulated+=s.weight;
            }
            return result;
        }
        private static Vector3 Scale(HoTransformConstraint c)
        {
            Vector3 result=Vector3.zero;double total=Total(c);
            foreach(var s in c.Sources)
                if(s.weight>0)result+=Vector3.Scale(c.Space==HoTransformConstraintSpace.World?s.transform.lossyScale:s.transform.localScale,s.scaleMultiplier)*(float)(s.weight/total);
            return result;
        }
        private static double Total(HoTransformConstraint c)
        {double sum=0;foreach(var s in c.Sources)sum+=s.weight;return sum;}
        private static Vector3 Mask(Vector3 input,Vector3 desired,HoTransformAxes mask)
        {return new Vector3((mask&HoTransformAxes.X)!=0?desired.x:input.x,(mask&HoTransformAxes.Y)!=0?desired.y:input.y,(mask&HoTransformAxes.Z)!=0?desired.z:input.z);}
        private static Quaternion MaskRotation(Quaternion input,Quaternion desired,HoTransformAxes mask)
        {
            if(mask==HoTransformAxes.All)return desired;
            if(mask==0)return input;
            return Quaternion.Euler(Mask(input.eulerAngles,desired.eulerAngles,mask));
        }
        internal static bool TryDivide(Vector3 a,Vector3 b,out Vector3 result)
        {
            result=Vector3.one;
            if(!HoTransformConstraint.Finite(a)||!HoTransformConstraint.Finite(b)||Mathf.Abs(b.x)<1e-8f||Mathf.Abs(b.y)<1e-8f||Mathf.Abs(b.z)<1e-8f)return false;
            result=new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);return HoTransformConstraint.Finite(result);
        }

        internal static bool Evaluate(HoTransformConstraint c,HoTransformLocalPose frameInput,HoTransformChannels alreadyWritten,out string error)
        {
            error=null;var target=c.TargetTransform;var parent=target.parent;
            var baseline=c.Input==HoTransformConstraintInput.RestPose?c.RestPose:frameInput;
            var current=HoTransformLocalPose.Read(target);
            // A later writer blends from the earlier writer, not its own bind pose.
            baseline.CopyFrom(current,alreadyWritten);
            bool world=c.Space==HoTransformConstraintSpace.World;
            Vector3 pos=baseline.position,scale=baseline.scale;
            Quaternion rot=baseline.rotation;
            if((c.WrittenChannels&HoTransformChannels.Position)!=0)
            {
                Vector3 start=world&&parent!=null?parent.TransformPoint(baseline.position):baseline.position;
                Vector3 desired=Position(c);
                Vector3 value=Mask(start,Vector3.Lerp(start,desired,c.Weight),c.PositionAxes);
                pos=world&&parent!=null?parent.InverseTransformPoint(value):value;
            }
            if((c.WrittenChannels&HoTransformChannels.Rotation)!=0)
            {
                Quaternion start=world&&parent!=null?parent.rotation*baseline.rotation:baseline.rotation;
                Quaternion desired;
                if(c is HoAimConstraint aim)
                {
                    if(!TryAimRotation(aim,out desired,out error))return false;
                    desired*=Quaternion.Euler(aim.RotationOffset);
                }
                else desired=Rotation(c);
                Quaternion value=MaskRotation(start,Quaternion.Slerp(start,desired,c.Weight),c.RotationAxes);
                rot=world&&parent!=null?Quaternion.Inverse(parent.rotation)*value:value;
            }
            if((c.WrittenChannels&HoTransformChannels.Scale)!=0)
            {
                Vector3 desired=Scale(c);
                if(world&&parent!=null)
                {
                    // Unity's lossyScale uses its Transform rotation decomposition,
                    // not simply the lengths of sheared matrix columns. Measure its
                    // current per-axis conversion so offsets use the same convention.
                    if(!TryDivide(target.lossyScale,target.localScale,out Vector3 factors)||!TryDivide(desired,factors,out desired))
                    {error="Target 或父级缩放退化，无法求解世界缩放；零缩放请使用 Local 空间";return false;}
                }
                scale=Mask(baseline.scale,Vector3.Lerp(baseline.scale,desired,c.Weight),c.ScaleAxes);
            }
            if(!HoTransformConstraint.Finite(pos)||!HoTransformConstraint.Finite(scale)||!Finite(rot))
            {error="求值结果包含非法数值";return false;}
            new HoTransformLocalPose {position=pos,rotation=rot,scale=scale}.Apply(target,c.WrittenChannels);
            return true;
        }
        private static bool Finite(Quaternion q)=>HoTransformConstraint.Finite(q.x)&&HoTransformConstraint.Finite(q.y)&&HoTransformConstraint.Finite(q.z)&&HoTransformConstraint.Finite(q.w);
    }
}
