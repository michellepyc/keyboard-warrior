using UnityEngine;

/// Side-scroller movement on the XY plane: walk, jump, crouch.
/// Blocking or attacking roots the player in place.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input")]
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode crouchKey = KeyCode.S;

    [Header("Walk")]
    public float moveSpeed = 9f;
    public float crouchSpeedMultiplier = 0.4f;
    public float groundAccel = 60f;
    public float airAccel = 25f;

    [Header("Jump")]
    public float jumpHeight = 1.8f;
    public float gravity = -35f;
    [Tooltip("Releasing jump early multiplies upward speed by this, for short hops.")]
    public float jumpCutMultiplier = 0.5f;
    [Tooltip("Can still jump this long after walking off a ledge.")]
    public float coyoteTime = 0.1f;
    [Tooltip("A jump pressed this long before landing still happens.")]
    public float jumpBuffer = 0.1f;

    [Header("Crouch")]
    public float standHeight = 2f;
    public float crouchHeight = 1.1f;
    [Tooltip("Visual that gets squashed when crouching.")]
    public Transform body;

    [Header("Knockback")]
    public float knockbackDecay = 25f;

    [Header("Ground check")]
    [Tooltip("CharacterController.isGrounded drops out for single frames at high frame rates. Stay grounded until it's been off this long (jumping still ends it right away).")]
    public float groundedGrace = 0.1f;

    public int Facing { get; private set; } = 1;
    /// Left/right input this frame: -1, 0 or 1.
    public float MoveInput { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsGrounded => Now - lastGroundedTime <= groundedGrace;
    /// Walking speed, not counting knockback.
    public float HorizontalSpeed => Mathf.Abs(velocity.x);
    public float VerticalSpeed => velocity.y;

    CharacterController controller;
    ParryController defense;
    PlayerAttack attack;
    PlayerInputContext inputContext;
    PlayerClock clock;
    // The player's own clock (PlayerClock): slows or freezes while typing a Smite.
    float Now => PlayerClock.NowOf(clock);
    float Dt => PlayerClock.DtOf(clock);
    Vector3 velocity;
    float knockback;
    float stepSpeed;
    float stepRemaining;
    float stepUntil = float.NegativeInfinity;
    Transform stepTarget;
    float stepStopDistance;
    float lastGroundedTime = float.NegativeInfinity;
    float lastJumpPressTime = float.NegativeInfinity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        defense = GetComponent<ParryController>();
        attack = GetComponent<PlayerAttack>();
        inputContext = GetComponent<PlayerInputContext>();
        clock = GetComponent<PlayerClock>();
        SetCrouch(false);
    }

    // Drop leftover momentum so it doesn't carry over (e.g. through a respawn).
    void OnDisable()
    {
        velocity = Vector3.zero;
        knockback = 0f;
        stepUntil = float.NegativeInfinity;
    }

    /// Shoves the player horizontally; positive is +X. Decays over time.
    public void Knockback(float speed) => knockback = speed;

    /// Moves the player up to maxDistance along X (signed) at speed, for at most
    /// duration, even while rooted. With a target, it holds while within
    /// stopDistance of it and closes in again if the target slides away.
    /// Used for the small step forward on each attack.
    public void Step(float maxDistance, float speed, float duration, Transform target = null, float stopDistance = 0f)
    {
        stepSpeed = Mathf.Sign(maxDistance) * Mathf.Abs(speed);
        stepRemaining = Mathf.Abs(maxDistance);
        stepUntil = Now + duration;
        stepTarget = target;
        stepStopDistance = stopDistance;
    }

    bool Key(KeyCode k) => PlayerInputContext.Key(inputContext, k);

    void Update()
    {
        // Keys go through the input context: while typing or smiting they all read as up.
        float input = (Key(rightKey) ? 1f : 0f) - (Key(leftKey) ? 1f : 0f);
        MoveInput = input;
        // Attacking and blocking both plant your feet, but a block can be cancelled
        // by jumping. A guard stun locks you in place entirely.
        bool attacking = attack != null && attack.IsAttacking;
        bool stunned = defense != null && defense.IsGuardStunned;
        bool rooted = attacking || stunned || (defense != null && defense.IsBlocking);

        if (!rooted && input != 0f) SetFacing(input > 0f ? 1 : -1);

        // Crouch only on the ground, and stay down if something is overhead.
        bool wantsCrouch = Key(crouchKey) && IsGrounded;
        if (IsCrouching && !wantsCrouch && !CanStand()) wantsCrouch = true;
        if (wantsCrouch != IsCrouching) SetCrouch(wantsCrouch);

        float speed = moveSpeed * (IsCrouching ? crouchSpeedMultiplier : 1f);
        float targetX = rooted ? 0f : input * speed;
        float accel = IsGrounded ? groundAccel : airAccel;
        // An attack in the air keeps the jump's momentum (no stopping, no steering).
        bool airAttack = attacking && !IsGrounded;
        if (!airAttack) velocity.x = Mathf.MoveTowards(velocity.x, targetX, accel * Dt);

        if (controller.isGrounded)
        {
            lastGroundedTime = Now;
            if (velocity.y < 0f) velocity.y = -2f;
        }
        if (PlayerInputContext.KeyDown(inputContext, jumpKey)) lastJumpPressTime = Now;

        bool canJump = Now - lastGroundedTime <= coyoteTime && !IsCrouching && !attacking && !stunned;
        if (canJump && Now - lastJumpPressTime <= jumpBuffer)
        {
            velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressTime = float.NegativeInfinity;
        }
        if (PlayerInputContext.KeyUp(inputContext, jumpKey) && velocity.y > 0f) velocity.y *= jumpCutMultiplier;

        velocity.y += gravity * Dt;
        knockback = Mathf.MoveTowards(knockback, 0f, knockbackDecay * Dt);

        float stepDelta = 0f;
        if (Now < stepUntil && stepRemaining > 0f)
        {
            // The attack step stops at a ledge rather than carrying you off it.
            if (IsGrounded && !GroundAhead(stepSpeed > 0f ? 1 : -1)) stepUntil = float.NegativeInfinity;
            else if (stepTarget == null || Mathf.Abs(stepTarget.position.x - transform.position.x) > stepStopDistance)
            {
                stepDelta = Mathf.Sign(stepSpeed) * Mathf.Min(Mathf.Abs(stepSpeed) * Dt, stepRemaining);
                stepRemaining -= Mathf.Abs(stepDelta);
            }
        }
        Vector3 move = new Vector3((velocity.x + knockback) * Dt + stepDelta, velocity.y * Dt, 0f);
        move.z = -transform.position.z; // stay on the side-scroll plane
        CollisionFlags flags = controller.Move(move);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
    }

    /// Turns to face a world X position (used by lock-on). Ignored if it's right on top of us.
    public void FaceTowards(float x)
    {
        float dx = x - transform.position.x;
        if (Mathf.Abs(dx) > 0.05f) SetFacing(dx > 0f ? 1 : -1);
    }

    /// Stops an attack step early (e.g. so a parry's recoil isn't cancelled by it).
    public void CancelStep() => stepUntil = float.NegativeInfinity;

    void SetFacing(int facing)
    {
        Facing = facing;
        transform.rotation = Quaternion.Euler(0f, facing > 0 ? 0f : 180f, 0f);
    }

    void SetCrouch(bool crouch)
    {
        IsCrouching = crouch;
        float h = crouch ? crouchHeight : standHeight;
        // Keep the bottom of the capsule planted; the root sits at the standing center.
        controller.height = h;
        controller.center = new Vector3(0f, (h - standHeight) * 0.5f, 0f);
        if (body != null)
        {
            body.localScale = new Vector3(1f, h / standHeight, 1f);
            body.localPosition = controller.center;
        }
    }

    bool GroundAhead(int dir)
    {
        Vector3 probe = transform.position + Vector3.right * (dir * (controller.radius + 0.15f));
        return Physics.Raycast(probe, Vector3.down, standHeight * 0.5f + 0.4f, ~0, QueryTriggerInteraction.Ignore);
    }

    bool CanStand()
    {
        float r = controller.radius * 0.95f;
        Vector3 bottom = transform.position + Vector3.up * (-standHeight * 0.5f + r + 0.05f);
        Vector3 top = transform.position + Vector3.up * (standHeight * 0.5f - r);
        foreach (Collider c in Physics.OverlapCapsule(bottom, top, r, ~0, QueryTriggerInteraction.Ignore))
            if (c != controller) return false;
        return true;
    }
}
