using System;
using System.Collections.Generic;
using UnityEngine;

public enum ParryResult { Parry, Blocked, Hit }

/// Player-side defense. Holding the block key guards; the first moments after
/// pressing it are a parry window. Parry attempts have a cooldown, so mashing the
/// key only gives you a plain block. Blocks and key parries only work against
/// attacks from the side the player is facing; blocking turns toward the closest
/// enemy. The attack key also parries: each press opens a short window in which a
/// strike that meets the sword or shield is parried, even mid-combo (with its own
/// cooldown, so mashing doesn't hold it open).
/// Blocking a hit (rather than parrying it) guard-stuns the player, and the
/// key must be pressed again afterwards, so holding block forever doesn't work.
public class ParryController : MonoBehaviour
{
    [Header("Input")]
    public KeyCode blockKey = KeyCode.K;

    [Header("Timing (seconds)")]
    [Tooltip("How long the parry stays active after pressing the key.")]
    public float parryWindow = 0.2f;
    [Tooltip("After a parry window closes, pressing again within this long only blocks.")]
    public float parryCooldown = 0.35f;
    [Tooltip("An enemy strike that reaches the player waits this long before landing, so a parry pressed (or a sword swing that meets it) just after impact still counts.")]
    public float lateParryGrace = 0.03f;
    [Tooltip("Pressing this soon after getting hit reports how late you were.")]
    public float lateReportWindow = 0.3f;
    [Tooltip("After blocking a hit, the player is stuck in idle this long: no blocking, parrying, attacking, moving or jumping. Blocking then needs a fresh press.")]
    public float guardStunTime = 0.6f;

    [Header("Sword/shield parry (attack key)")]
    [Tooltip("Trigger colliders on the sword and shield. An enemy strike touching one within Attack Parry Window of an attack press is parried. Point these at the real weapon colliders when the models are swapped in.")]
    [SerializeField] Collider[] guardColliders = new Collider[0];
    [Tooltip("How long after pressing attack a strike caught on the sword or shield is parried.")]
    public float attackParryWindow = 0.2f;
    [Tooltip("After that window closes, attack presses within this long don't open a new one.")]
    public float attackParryCooldown = 0.35f;
    [Tooltip("Strikes this close to the sword or shield (metres) still count as touching them.")]
    public float guardPadding = 0.15f;
    [Tooltip("A sword/shield parry pushes the player back this hard, for a clash.")]
    public float attackParryRecoil = 3f;

    [Header("Visuals")]
    [Tooltip("Always visible; tinted while the parry window is open or while blocking.")]
    public GameObject shield;
    public Color idleColor = new Color(0.25f, 0.55f, 1f);
    public Color cooldownColor = new Color(0.15f, 0.22f, 0.4f);
    public Color parryShieldColor = new Color(0.5f, 1f, 1f);
    public Color blockShieldColor = new Color(0.45f, 0.5f, 0.6f);

    /// Fired when an incoming attack lands. The float is seconds since the
    /// parry press, or since the swing if the attack parried it.
    public event Action<ParryResult, float> AttackResolved;
    /// Fired when a tapped parry window closes without catching anything.
    public event Action Whiffed;
    /// Fired when the key is pressed shortly after getting hit. The float is seconds late.
    public event Action<float> LatePress;
    /// Fired on every block-key press.
    public event Action BlockPressed;

    /// Every active player, for enemy hitboxes.
    public static readonly List<ParryController> All = new List<ParryController>();

    public bool IsBlocking { get; private set; }
    public bool IsParryWindowOpen => Time.time - pressTime <= parryWindow;
    public bool ParryReady => Time.time >= pressTime + parryWindow + parryCooldown;
    public bool IsGuardStunned => Time.time < guardStunnedUntil;
    /// The last time the player was blocking (or stunned from a block).
    public float LastGuardTime { get; private set; } = float.NegativeInfinity;
    /// True if the most recent parry came from an attack swing rather than the block key.
    public bool LastParryWasAttack { get; private set; }
    /// Where the most recent incoming attack connected.
    public Vector3 LastContactPoint { get; private set; }
    /// Whether a strike touching the sword or shield right now would be parried.
    public bool CanGuardParry => Time.time - attackPressTime <= attackParryWindow && !attackWindowUsed;

    PlayerMovement movement;
    PlayerAttack attack;
    PlayerVisuals visuals;
    PlayerTargeting targeting;
    PlayerInputContext inputContext;
    Renderer shieldRenderer;
    Color shieldBaseColor = Color.white;
    float pressTime = float.NegativeInfinity;
    float lastHitTime = float.NegativeInfinity;
    bool windowUsed = true;
    float attackPressTime = float.NegativeInfinity;
    bool attackWindowUsed = true;
    float guardStunnedUntil = float.NegativeInfinity;
    bool needsRepress;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        attack = GetComponent<PlayerAttack>();
        visuals = GetComponent<PlayerVisuals>();
        targeting = GetComponent<PlayerTargeting>();
        inputContext = GetComponent<PlayerInputContext>();
        if (shield != null) shieldRenderer = shield.GetComponentInChildren<Renderer>(true);
        if (shieldRenderer != null) shieldBaseColor = shieldRenderer.material.color;
    }

    void OnEnable() => All.Add(this);

    void OnDisable()
    {
        All.Remove(this);
        IsBlocking = false;
        if (shieldRenderer != null) shieldRenderer.material.color = shieldBaseColor;
    }

    void Update()
    {
        // A swing or a jump interrupts the guard; it comes back on its own if the
        // key is still held once the swing ends or the player lands. A guard stun
        // is different: the key has to be released and pressed again.
        // Through the input context: the block key reads as up while typing or smiting.
        bool keyHeld = PlayerInputContext.Key(inputContext, blockKey);
        if (!keyHeld) needsRepress = false;
        bool interrupted = (attack != null && attack.IsAttacking) || (movement != null && !movement.IsGrounded)
            || IsGuardStunned || needsRepress;
        IsBlocking = keyHeld && !interrupted;

        if (PlayerInputContext.KeyDown(inputContext, blockKey)) PressBlock();
        if (IsBlocking) FaceClosestEnemy();
        if (IsBlocking || IsGuardStunned) LastGuardTime = Time.time;

        // Holding the guard isn't a missed parry, so only report taps.
        if (!windowUsed && !IsParryWindowOpen)
        {
            windowUsed = true;
            if (!keyHeld) Whiffed?.Invoke();
        }

        UpdateVisuals();
    }

    /// What pressing the block key does: open a parry window (if it's off cooldown)
    /// and turn toward the closest enemy.
    public void PressBlock()
    {
        if (Time.time - lastHitTime <= lateReportWindow)
            LatePress?.Invoke(Time.time - lastHitTime);
        BlockPressed?.Invoke();

        if (ParryReady && !IsGuardStunned)
        {
            pressTime = Time.time;
            windowUsed = false;
        }
        FaceClosestEnemy();
    }

    /// Called on every attack-key press: opens the sword/shield parry window if it's
    /// off cooldown. Doesn't turn the player (the swing does that).
    public void PressAttackParry()
    {
        if (IsGuardStunned) return;
        if (Time.time < attackPressTime + attackParryWindow + attackParryCooldown) return;
        attackPressTime = Time.time;
        attackWindowUsed = false;
    }

    void FaceClosestEnemy()
    {
        CombatTarget enemy = targeting != null ? targeting.Closest() : null;
        if (enemy != null && movement != null) movement.FaceTowards(enemy.transform.position.x);
    }

    public bool IsGuardCollider(Collider c) => Array.IndexOf(guardColliders, c) >= 0;
    public Collider[] GuardColliders => guardColliders;

    /// Whether a strike from this position would be parried by the block key right now.
    public bool CanKeyParry(Vector3 attackerPosition) => IsFacing(attackerPosition) && IsParryWindowOpen && !windowUsed;

    /// Whether the player is facing toward this position (left/right only).
    public bool IsFacing(Vector3 attackerPosition)
    {
        int facing = movement != null ? movement.Facing : 1;
        return Mathf.Sign(attackerPosition.x - transform.position.x) == facing;
    }

    /// Called by an attacker at the moment its hit lands. guardContact means it
    /// struck the sword or shield rather than the body. contactPoint is where it
    /// connected (for effects); it defaults to between the two.
    public ParryResult ResolveIncomingAttack(Vector3 attackerPosition, bool guardContact = false, Vector3? contactPoint = null)
    {
        LastContactPoint = contactPoint ?? (attackerPosition + transform.position) * 0.5f;
        float sincePress = Time.time - pressTime;
        bool facingAttacker = IsFacing(attackerPosition);
        ParryResult result;

        if (guardContact && CanGuardParry)
        {
            // Touching the sword already proves which way it came from, so no facing check.
            sincePress = Time.time - attackPressTime;
            attackWindowUsed = true;
            LastParryWasAttack = true;
            result = ParryResult.Parry;
            if (movement != null)
            {
                movement.CancelStep();
                movement.Knockback(Mathf.Sign(transform.position.x - attackerPosition.x) * attackParryRecoil);
            }
        }
        else if (CanKeyParry(attackerPosition))
        {
            windowUsed = true;
            LastParryWasAttack = false;
            result = ParryResult.Parry;
        }
        else if (facingAttacker && IsBlocking)
        {
            result = ParryResult.Blocked;
            IsBlocking = false;
            guardStunnedUntil = Time.time + guardStunTime;
            needsRepress = true;
        }
        else
        {
            result = ParryResult.Hit;
            lastHitTime = Time.time;
            // Report how early the latest parry attempt was, with either key.
            sincePress = Time.time - Mathf.Max(pressTime, attackPressTime);
        }

        AttackResolved?.Invoke(result, sincePress);
        return result;
    }

    void UpdateVisuals()
    {
        bool parrying = IsParryWindowOpen;
        if (shieldRenderer != null)
            shieldRenderer.material.color = parrying ? parryShieldColor : IsBlocking ? blockShieldColor : shieldBaseColor;

        if (visuals != null)
            visuals.SetTint(parrying || ParryReady ? idleColor : cooldownColor);
    }
}
