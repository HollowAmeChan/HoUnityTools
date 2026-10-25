using UnityEngine;
namespace Hollow.HoUnityTools.TransformConstraints
{
    [DefaultExecutionOrder(30000)]
    [AddComponentMenu("HoUnityTools/Transform Constraints/Ho Rotation Constraint")]
    public sealed class HoRotationConstraint : HoTransformConstraint
    {public override HoTransformConstraintKind Kind=>HoTransformConstraintKind.Rotation;}
}
