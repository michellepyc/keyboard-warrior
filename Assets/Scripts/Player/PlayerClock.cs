using UnityEngine;

/// The player's own clock, so the player can run slower than the rest of the game.
/// PlayerAttack, PlayerMovement, PlayerAnimator and PlayerVisuals time themselves with
/// it (Now / Dt instead of Time.time / Time.deltaTime), and the player's Animator plays
/// at the same rate. Normally it reads exactly the game clock.
/// While typing a Smite:
/// - if the player was in the middle of an animation (a swing, a flinch, a jump or
///   landing, anything not looping), they freeze on that exact frame: pose, swing
///   timing, movement and gravity all hold until typing ends, then carry on;
/// - otherwise they run at Typing Speed (80%) of the already-slowed world.
[DefaultExecutionOrder(-90)]
public class PlayerClock : MonoBehaviour
{
    [Tooltip("While typing, the player runs at this fraction of the (already slowed) world's speed.")]
    [Range(0f, 1f)] public float typingSpeed = 0.8f;
    [Tooltip("If typing starts mid-animation, freeze on that frame until typing ends.")]
    public bool freezeMidAnimation = true;

    /// The player's speed relative to the game (1 normally; 0 when frozen).
    public float Scale { get; private set; } = 1f;
    /// Whether the player is frozen mid-animation right now.
    public bool IsFrozen => Scale <= 0f;
    /// The player's time: the game's, minus whatever was held back while slowed or frozen.
    public float Now => Time.time - lost;
    /// This frame's step of the player's time.
    public float Dt => Time.deltaTime * Scale;

    /// For scripts that may run without a clock (they then use the game clock).
    public static float NowOf(PlayerClock clock) => clock != null ? clock.Now : Time.time;
    public static float DtOf(PlayerClock clock) => clock != null ? clock.Dt : Time.deltaTime;

    PlayerStateMachine states;
    PlayerVisuals visuals;
    PlayerAttack attack;
    PlayerMovement movement;
    float lost;
    float animatorSpeed = 1f;
    bool slowedForTyping;

    void Awake()
    {
        states = GetComponent<PlayerStateMachine>();
        visuals = GetComponent<PlayerVisuals>();
        attack = GetComponent<PlayerAttack>();
        movement = GetComponent<PlayerMovement>();
    }

    void OnEnable()
    {
        if (states != null) states.Changed += OnStateChanged;
    }

    void OnDisable()
    {
        if (states != null) states.Changed -= OnStateChanged;
        Restore();
    }

    // Runs before the other player scripts, so they all see this frame's clock.
    void Update() => lost += Time.deltaTime * (1f - Scale);

    void OnStateChanged(PlayerState from, PlayerState to)
    {
        if (to == PlayerState.Typing) BeginTyping();
        else if (from == PlayerState.Typing) Restore();
    }

    void BeginTyping()
    {
        Animator animator = visuals != null ? visuals.Animator : null;
        string busy = freezeMidAnimation ? MidAnimation(animator) : null;
        float scale = busy != null ? 0f : typingSpeed;
        if (states != null && states.logTransitions)
            Debug.Log(busy != null ? $"[PlayerClock] Typing: frozen mid-animation ({busy})" : $"[PlayerClock] Typing: player at {scale:0.##}x of the world", this);
        SetScale(scale, animator);
        slowedForTyping = true;
    }

    void Restore()
    {
        if (!slowedForTyping) return;
        slowedForTyping = false;
        SetScale(1f, visuals != null ? visuals.Animator : null);
    }

    void SetScale(float scale, Animator animator)
    {
        if (animator != null)
        {
            if (Scale == 1f) animatorSpeed = animator.speed; // what to go back to
            animator.speed = animatorSpeed * scale;
        }
        Scale = scale;
    }

    /// What's mid-animation right now (null if nothing): a swing, being in the air, or
    /// any one-shot animation still playing or blending on a visible layer.
    string MidAnimation(Animator animator)
    {
        if (attack != null && attack.IsAttacking) return "attacking";
        if (movement != null && !movement.IsGrounded) return "in the air";
        if (animator == null) return null;
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            if (layer > 0 && animator.GetLayerWeight(layer) < 0.01f) continue;
            if (animator.IsInTransition(layer)) return $"blending on layer {layer}";
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(layer);
            if (!info.loop && info.normalizedTime < 1f) return $"one-shot animation on layer {layer}";
        }
        return null;
    }
}
