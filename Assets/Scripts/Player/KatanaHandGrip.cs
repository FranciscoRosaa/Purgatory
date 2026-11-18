using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class KatanaHandGrip : MonoBehaviour
{
    [SerializeField] private Transform wrist;
    [SerializeField] private Quaternion wristRotation = Quaternion.identity;
    [SerializeField] private Transform[] fingers;
    [SerializeField] private Quaternion[] gripRotations;
    [SerializeField] private Transform bladeModel;

    private const float BladeLengthMultiplier = 1.30f;
    private const float CarryInclination = -0.26f;
    private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
    private static readonly int Sprint = Animator.StringToHash("Base Layer.Sprint");
    private Animator animator;

    internal Transform BladeModel => bladeModel;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (bladeModel != null)
        {
            var scale = bladeModel.localScale;
            scale.y *= BladeLengthMultiplier;
            bladeModel.localScale = scale;
        }
    }

    private void LateUpdate()
    {
        if (wrist != null)
        {
            wrist.localRotation = wristRotation;
            TiltCarriedBlade();
        }
        AlignCuttingEdge();
        if (fingers == null || gripRotations == null || fingers.Length != gripRotations.Length)
            return;
        for (int i = 0; i < fingers.Length; i++)
            if (fingers[i] != null)
                fingers[i].localRotation = gripRotations[i];
    }

    private void TiltCarriedBlade()
    {
        if (bladeModel == null || animator == null)
            return;

        int state = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        if (state != Idle && state != Sprint)
            return;

        Vector3 axis = bladeModel.up;
        if (axis.y <= CarryInclination)
            return;

        Vector3 horizontal = Vector3.ProjectOnPlane(axis, Vector3.up);
        if (horizontal.sqrMagnitude < 0.0001f)
            horizontal = transform.right;
        Vector3 carry = horizontal.normalized * Mathf.Sqrt(1f - CarryInclination * CarryInclination)
            + Vector3.up * CarryInclination;
        wrist.rotation = Quaternion.FromToRotation(axis, carry) * wrist.rotation;
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
