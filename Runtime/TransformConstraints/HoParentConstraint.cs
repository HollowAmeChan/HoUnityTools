using UnityEngine;
namespace Hollow.HoUnityTools.TransformConstraints
{
    [DefaultExecutionOrder(30000)]
    [AddComponentMenu("HoUnityTools/Transform Constraints/Ho Parent Constraint")]
    public sealed class HoParentConstraint : HoTransformConstraint
    {public override HoTransformConstraintKind Kind=>HoTransformConstraintKind.Parent;}
}
