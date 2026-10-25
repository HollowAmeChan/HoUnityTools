using UnityEngine;
namespace Hollow.HoUnityTools.TransformConstraints
{
    [DefaultExecutionOrder(30000)]
    [AddComponentMenu("HoUnityTools/Transform Constraints/Ho Position Constraint")]
    public sealed class HoPositionConstraint : HoTransformConstraint
    {public override HoTransformConstraintKind Kind=>HoTransformConstraintKind.Position;}
}
