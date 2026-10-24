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
    public int samples;
    public float maxPositionError;
    public float maxRotationError;
    public float maxScaleError;

    private void LateUpdate()
    {
        if (expectedTarget == null || constrainedBone == null) return;
        samples++;
        maxPositionError = Mathf.Max(maxPositionError, Vector3.Distance(expectedTarget.position, constrainedBone.position));
        maxRotationError = Mathf.Max(maxRotationError, Quaternion.Angle(expectedTarget.rotation, constrainedBone.rotation));
        maxScaleError = Mathf.Max(maxScaleError, Vector3.Distance(expectedTarget.lossyScale, constrainedBone.lossyScale));
    }
}
