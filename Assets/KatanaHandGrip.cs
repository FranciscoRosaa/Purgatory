using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class KatanaHandGrip : MonoBehaviour
{
    [SerializeField] private Transform wrist;
    [SerializeField] private Quaternion wristRotation = Quaternion.identity;
    [SerializeField] private Transform[] fingers;
    [SerializeField] private Quaternion[] gripRotations;
    [SerializeField] private Transform bladeModel;

    private void LateUpdate()
    {
        if (wrist != null) wrist.localRotation = wristRotation;
        if (bladeModel != null)
        {
            // Roll around the blade's length: keep its cutting edge underneath.
            var underside = Vector3.ProjectOnPlane(Vector3.down, bladeModel.up);
            if (underside.sqrMagnitude > 0.0001f)
            {
                float roll = Vector3.SignedAngle(bladeModel.right, underside.normalized, bladeModel.up);
                bladeModel.rotation = Quaternion.AngleAxis(roll, bladeModel.up) * bladeModel.rotation;
            }
        }
        if (fingers == null || gripRotations == null || fingers.Length != gripRotations.Length)
            return;
        for (int i = 0; i < fingers.Length; i++)
            if (fingers[i] != null)
                fingers[i].localRotation = gripRotations[i];
    }
}
