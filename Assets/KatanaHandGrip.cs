using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class KatanaHandGrip : MonoBehaviour
{
    [SerializeField] private Transform wrist;
    [SerializeField] private Quaternion wristRotation = Quaternion.identity;
    [SerializeField] private Transform[] fingers;
    [SerializeField] private Quaternion[] gripRotations;
    [SerializeField] private Transform bladeModel;

    internal Transform BladeModel => bladeModel;

    private void LateUpdate()
    {
        if (wrist != null) wrist.localRotation = wristRotation;
        AlignCuttingEdge();
        if (fingers == null || gripRotations == null || fingers.Length != gripRotations.Length)
            return;
        for (int i = 0; i < fingers.Length; i++)
            if (fingers[i] != null)
                fingers[i].localRotation = gripRotations[i];
    }

    internal void AimBlade(Vector3 direction, float weight)
    {
        if (wrist == null || bladeModel == null)
            return;

        var aim = Quaternion.FromToRotation(bladeModel.up, direction) * wrist.rotation;
        wrist.rotation = Quaternion.Slerp(wrist.rotation, aim, weight);
        AlignCuttingEdge();
    }

    private void AlignCuttingEdge()
    {
        if (bladeModel == null)
            return;

        // Roll around the blade's length to keep its cutting edge underneath.
        var underside = Vector3.ProjectOnPlane(Vector3.down, bladeModel.up);
        if (underside.sqrMagnitude <= 0.0001f)
            return;

        float roll = Vector3.SignedAngle(bladeModel.right, underside.normalized, bladeModel.up);
        bladeModel.rotation = Quaternion.AngleAxis(roll, bladeModel.up) * bladeModel.rotation;
    }
}
