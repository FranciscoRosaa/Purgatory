using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(50)]
[RequireComponent(typeof(WasdBlockMovement))]
public sealed class PlayerMeleeCombo : MonoBehaviour
{
    private const int MaxHits = 4;
    private static readonly int AttackState = Animator.StringToHash("Base Layer.M1 Attack");
    private static readonly int FinisherState = Animator.StringToHash("Base Layer.M1 Finisher");

    [SerializeField, Min(0f)] private float comboWindow = 0.8f;
    [SerializeField, Min(0f)] private float finisherCooldown = 0.65f;

    private WasdBlockMovement movement;
    private Animator animator;
    private int comboHits;
    private bool attacking;
    private bool buffered;
    private float comboExpires;
    private float nextAllowedAttack;

    private void Awake()
    {
        movement = GetComponent<WasdBlockMovement>();
        animator = transform.Find("Player_Character")?.GetComponent<Animator>();
        enabled = animator != null && animator.HasState(0, AttackState) && animator.HasState(0, FinisherState);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool pressed = Mouse.current?.leftButton.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetMouseButtonDown(0);
#else
        bool pressed = false;
#endif
        TickCombo(pressed, Time.time);
    }

    private void TickCombo(bool pressed, float now)
    {
        if (movement.IsDodging)
        {
            // A dash can interrupt the finisher but still consumes its cooldown.
            if (comboHits == MaxHits)
                nextAllowedAttack = now + finisherCooldown;

            comboHits = 0;
            attacking = false;
            buffered = false;
            return;
        }

        if (attacking)
        {
            int state = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            if (state == AttackState || state == FinisherState)
            {
                if (pressed && comboHits < MaxHits)
                    buffered = true;
                return;
            }

            attacking = false;
            if (comboHits == MaxHits)
            {
                comboHits = 0;
                nextAllowedAttack = now + finisherCooldown;
                return;
            }

            comboExpires = now + comboWindow;
        }

        if (now < nextAllowedAttack || (!pressed && !buffered))
            return;

        if (now > comboExpires)
            comboHits = 0;

        buffered = false;
        attacking = true;
        comboHits++;
        animator.Play(comboHits == MaxHits ? FinisherState : AttackState, 0, 0f);
    }
}
