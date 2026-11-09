using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class WasdBlockMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float speed = 5f;
    [SerializeField, Min(0.01f)] private float dashSpeed = 22f;
    [SerializeField, Min(0.01f)] private float dashDuration = 0.15f;
    [SerializeField, Min(0f)] private float dashCooldown = 0.55f;
    [SerializeField] private Collider[] walkableGround;
    [SerializeField, Min(0.01f)] private float turnSharpness = 12f;

    private Vector3 dashDirection;
    private float dashTimeLeft;
    private float nextDashTime;

    private Transform characterVisual;
    private Quaternion targetRotation;
    private Animator characterAnimator;
    private Vector3 previousPosition;

    private void Awake()
    {
        characterVisual = transform.Find("Player_Character");
        targetRotation = characterVisual != null ? characterVisual.rotation : Quaternion.identity;
        characterAnimator = characterVisual != null ? characterVisual.GetComponent<Animator>() : null;
        previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (characterVisual == null)
            return;

        float blend = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
        characterVisual.rotation = Quaternion.Slerp(characterVisual.rotation, targetRotation, blend);

        if (characterAnimator != null)
            characterAnimator.SetBool("IsMoving", (transform.position - previousPosition).sqrMagnitude > 0.000001f);
        previousPosition = transform.position;
    }

    private void Update()
    {
        if (dashTimeLeft > 0f)
        {
            AdvanceDash();
            return;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        bool forward = keyboard.wKey.isPressed;
        bool backward = keyboard.sKey.isPressed;
        bool left = keyboard.aKey.isPressed;
        bool right = keyboard.dKey.isPressed;
        bool dashPressed = keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool forward = Input.GetKey(KeyCode.W);
        bool backward = Input.GetKey(KeyCode.S);
        bool left = Input.GetKey(KeyCode.A);
        bool right = Input.GetKey(KeyCode.D);
        bool dashPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
#else
        return;
#endif

#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
        float x = (right ? 1f : 0f) - (left ? 1f : 0f);
        float z = (forward ? 1f : 0f) - (backward ? 1f : 0f);
        var direction = new Vector3(x, 0f, z).normalized;
        if (direction == Vector3.zero)
            return;

        if (dashPressed && Time.time >= nextDashTime)
        {
            dashDirection = direction;
            dashTimeLeft = dashDuration;
            nextDashTime = Time.time + dashDuration + dashCooldown;
            AdvanceDash();
            return;
        }

        TryMove(direction * (speed * Time.deltaTime));
#endif
    }

    private void AdvanceDash()
    {
        float elapsed = Mathf.Min(Time.deltaTime, dashTimeLeft);
        dashTimeLeft -= elapsed;
        float distance = dashSpeed * elapsed;
        // Check the ground along the dash, even when one frame covers a long distance.
        const float maxStep = 0.2f;
        int steps = Mathf.CeilToInt(distance / maxStep);
        if (steps == 0)
            return;

        Vector3 offset = dashDirection * (distance / steps);
        for (int i = 0; i < steps; i++)
        {
            if (TryMove(offset))
                continue;
            dashTimeLeft = 0f;
            break;
        }
    }

    private bool TryMove(Vector3 offset)
    {
        if (characterVisual != null && offset.sqrMagnitude > 0f)
            targetRotation = Quaternion.LookRotation(offset, Vector3.up);

        Vector3 nextPosition = transform.position + offset;
        if (!IsWalkable(nextPosition))
            return false;

        transform.position = nextPosition;
        return true;
    }

    private bool IsWalkable(Vector3 position)
    {
        if (walkableGround == null || walkableGround.Length == 0)
            return true;

        var ray = new Ray(position + Vector3.up, Vector3.down);
        foreach (var ground in walkableGround)
            if (ground != null && ground.Raycast(ray, out _, 2f))
                return true;

        return false;
    }
}
