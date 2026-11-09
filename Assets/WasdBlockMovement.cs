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
    [SerializeField] private Material dashWindMaterial;

    private static readonly int RollState = Animator.StringToHash("Base Layer.Roll");

    private Vector3 dashDirection;
    private float dashTimeLeft;
    private float nextDashTime;
    private bool dashedThisFrame;

    private Transform characterVisual;
    private Quaternion targetRotation;
    private Animator characterAnimator;
    private Vector3 previousPosition;
    private DashPresentation dashPresentation;

    internal bool UsesDashGrounding { get; private set; }
    internal bool IsDodging => dashTimeLeft > 0f || dashedThisFrame || UsesDashGrounding;

    private void Awake()
    {
        characterVisual = transform.Find("Player_Character");
        targetRotation = characterVisual != null ? characterVisual.rotation : Quaternion.identity;
        characterAnimator = characterVisual != null ? characterVisual.GetComponent<Animator>() : null;
        if (characterVisual != null)
            dashPresentation = new DashPresentation(characterVisual, transform, dashWindMaterial);
        previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (characterVisual == null)
            return;

        bool rolling = characterAnimator != null && characterAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == RollState;
        UsesDashGrounding = rolling || dashedThisFrame;
        if (dashTimeLeft > 0f || rolling)
        {
            characterVisual.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);
        }
        else
        {
            float blend = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
            characterVisual.rotation = Quaternion.Slerp(characterVisual.rotation, targetRotation, blend);
        }

        if (characterAnimator != null)
            characterAnimator.SetBool("IsMoving", (transform.position - previousPosition).sqrMagnitude > 0.000001f);
        previousPosition = transform.position;

        float groundHeight = transform.position.y;
        if (UsesDashGrounding)
            TryGetGroundHeight(transform.position, out groundHeight);
        dashPresentation.Update(transform.position, dashDirection, groundHeight, rolling, dashedThisFrame);
    }

    private void OnDisable()
    {
        dashTimeLeft = 0f;
        dashedThisFrame = false;
        UsesDashGrounding = false;
        dashPresentation?.Reset();
    }

    private void OnDestroy() => dashPresentation?.Dispose();

    private void Update()
    {
        dashedThisFrame = false;
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
            BeginDash(direction);
            return;
        }

        TryMove(direction * (speed * Time.deltaTime));
#endif
    }

    private void BeginDash(Vector3 direction)
    {
        dashDirection = direction;
        dashTimeLeft = dashDuration;
        nextDashTime = Time.time + dashDuration + dashCooldown;
        if (characterVisual != null)
            characterVisual.rotation = Quaternion.LookRotation(direction, Vector3.up);
        if (characterAnimator != null && characterAnimator.HasState(0, RollState))
            characterAnimator.Play(RollState, 0, 0f);
        dashPresentation?.Begin(transform.position, direction);
        AdvanceDash();
    }

    private void AdvanceDash()
    {
        float elapsed = Mathf.Min(Time.deltaTime, dashTimeLeft);
        dashTimeLeft -= elapsed;
        dashedThisFrame = elapsed > 0f;
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
        if (!TryGetGroundHeight(nextPosition, out _))
            return false;

        transform.position = nextPosition;
        return true;
    }

    private bool TryGetGroundHeight(Vector3 position, out float height)
    {
        height = position.y;
        if (walkableGround == null || walkableGround.Length == 0)
            return true;

        var ray = new Ray(position + Vector3.up, Vector3.down);
        foreach (var ground in walkableGround)
        {
            if (ground != null && ground.Raycast(ray, out var hit, 2f))
            {
                height = hit.point.y;
                return true;
            }
        }

        return false;
    }
}
