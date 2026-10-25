using UnityEngine;

// Test fixture component, copied only into the disposable validation project.
public sealed class CombineArmatureTestReferences : MonoBehaviour
{
    public Transform bone;
    public GameObject boneObject;
    public Transform[] bones;
    public Transform sourceRoot;
    public Transform expectedTarget;
    public Transform constrainedBone;
    public bool useBindOffset;
    public Matrix4x4 bindOffset;
    public int samples;
    public float maxPositionError;
    public float maxRotationError;
    public float maxScaleError;

    private void LateUpdate()
    {
        if (expectedTarget == null || constrainedBone == null) return;
        samples++;
        Matrix4x4 expected = useBindOffset ? expectedTarget.localToWorldMatrix * bindOffset : expectedTarget.localToWorldMatrix;
        maxPositionError = Mathf.Max(maxPositionError, Vector3.Distance(expected.GetColumn(3), constrainedBone.position));
        maxRotationError = Mathf.Max(maxRotationError, Quaternion.Angle(expected.rotation, constrainedBone.rotation));
        maxScaleError = Mathf.Max(maxScaleError, Vector3.Distance(expected.lossyScale, constrainedBone.lossyScale));
    }
}
