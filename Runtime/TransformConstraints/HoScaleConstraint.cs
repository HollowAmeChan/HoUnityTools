using UnityEngine;
namespace Hollow.HoUnityTools.TransformConstraints
{
    [DefaultExecutionOrder(30000)]
    [AddComponentMenu("HoUnityTools/Transform Constraints/Ho Scale Constraint")]
    public sealed class HoScaleConstraint : HoTransformConstraint
    {public override HoTransformConstraintKind Kind=>HoTransformConstraintKind.Scale;}
}
