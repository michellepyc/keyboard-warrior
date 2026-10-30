using UnityEngine;

/// Feeds gameplay state into the model's Animator. The controller itself is built
/// by PlayerAnimatorBuilder (Tools > Player Animations > Rebuild Player Animator).
/// Turns itself off if there's no model with an Animator under ModelRoot.
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("How fast the Actions layer (attack/block/parry) fades in and out.")]
    public float actionBlendSpeed = 12f;
    public float speedDamping = 0.05f;
    [Tooltip("Playback speed of the moving clip (walk) at full speed (1 = as authored). Idle is unaffected. Can be tuned in Play mode.")]
    public float runAnimationSpeed = 1.35f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int RunAnimSpeedId = Animator.StringToHash("RunAnimSpeed");
    static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
    static readonly int GroundedId = Animator.StringToHash("Grounded");
    static readonly int CrouchingId = Animator.StringToHash("Crouching");
    static readonly int BlockingId = Animator.StringToHash("Blocking");
    static readonly int AttackingId = Animator.StringToHash("Attacking");
    static readonly int ComboStepId = Animator.StringToHash("ComboStep");
    static readonly int ComboIndexId = Animator.StringToHash("ComboIndex");
    static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    static readonly int DeadId = Animator.StringToHash("Dead");
    static readonly int HurtId = Animator.StringToHash("Hurt");
    static readonly int HurtIndexId = Animator.StringToHash("HurtIndex");
    static readonly int ParryId = Animator.StringToHash("Parry");
    static readonly int BlockImpactId = Animator.StringToHash("BlockImpact");

    PlayerVisuals visuals;
    PlayerMovement movement;
    ParryController defense;
    PlayerAttack attack;
    PlayerClock clock;
    PlayerHealth health;
    Animator animator;
    int actionLayer = -1;
    int hurtVariants = 1;

    void Awake()
    {
        visuals = GetComponent<PlayerVisuals>();
        movement = GetComponent<PlayerMovement>();
        defense = GetComponent<ParryController>();
        attack = GetComponent<PlayerAttack>();
        clock = GetComponent<PlayerClock>();
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        if (defense != null) defense.AttackResolved += OnAttackResolved;
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (defense != null) defense.AttackResolved -= OnAttackResolved;
        if (health != null) health.Damaged -= OnDamaged;
    }

    // Start, not Awake: PlayerVisuals finds the Animator in its own Awake.
    void Start()
    {
        animator = visuals != null ? visuals.Animator : null;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            enabled = false;
            return;
        }
        actionLayer = animator.GetLayerIndex("Actions");
        // Hit reactions are states Hurt, Hurt2, Hurt3... on the base layer.
        while (animator.HasState(0, Animator.StringToHash($"Hurt{hurtVariants + 1}"))) hurtVariants++;
    }

    void Update()
    {
        bool dead = health != null && health.IsDead;
        bool blocking = defense != null && defense.IsBlocking;
        bool attacking = attack != null && attack.IsAttacking;

        if (movement != null)
        {
            animator.SetFloat(SpeedId, movement.HorizontalSpeed, speedDamping, PlayerClock.DtOf(clock));
            // Scale only as much as the run clip is blended in, so idle keeps its pace.
            float run01 = movement.moveSpeed > 0f ? Mathf.Clamp01(animator.GetFloat(SpeedId) / movement.moveSpeed) : 0f;
            animator.SetFloat(RunAnimSpeedId, Mathf.Lerp(1f, runAnimationSpeed, run01));
            animator.SetFloat(VerticalSpeedId, movement.VerticalSpeed);
            animator.SetBool(GroundedId, movement.IsGrounded);
            animator.SetBool(CrouchingId, movement.IsCrouching);
        }
        animator.SetBool(BlockingId, blocking);
        animator.SetBool(AttackingId, attacking);
        if (attack != null)
        {
            animator.SetInteger(ComboStepId, attack.ComboStep);
            animator.SetInteger(ComboIndexId, attack.ComboIndex);
            animator.SetFloat(AttackSpeedId, attack.IsFastForwarding ? attack.tailSpeedup : 1f);
        }
        animator.SetBool(DeadId, dead);

        if (actionLayer >= 0)
        {
            // Actions play on the whole body, so step aside for the base layer's Hurt and Death.
            bool hurt = animator.GetCurrentAnimatorStateInfo(0).IsTag("Hurt");
            bool busy = !dead && !hurt && (blocking || attacking || ActionPlaying());
            float weight = Mathf.MoveTowards(animator.GetLayerWeight(actionLayer), busy ? 1f : 0f, actionBlendSpeed * PlayerClock.DtOf(clock));
            animator.SetLayerWeight(actionLayer, weight);
        }
    }

    bool ActionPlaying() =>
        animator.GetCurrentAnimatorStateInfo(actionLayer).IsTag("Action")
        || animator.GetNextAnimatorStateInfo(actionLayer).IsTag("Action");

    void OnAttackResolved(ParryResult result, float sinceInput)
    {
        if (animator == null) return;
        // A sword parry happens mid-swing, so the slash keeps playing.
        if (result == ParryResult.Parry && !defense.LastParryWasAttack) animator.SetTrigger(ParryId);
        else if (result == ParryResult.Blocked) animator.SetTrigger(BlockImpactId);
    }

    void OnDamaged(int amount)
    {
        // The killing blow plays Death instead (driven by the Dead bool). A heavy-armored
        // swing keeps playing rather than flinching.
        if (animator == null || health.Health <= 0) return;
        if (attack != null && attack.IsArmored) return;
        animator.SetInteger(HurtIndexId, Random.Range(0, hurtVariants));
        animator.SetTrigger(HurtId);
    }
}
