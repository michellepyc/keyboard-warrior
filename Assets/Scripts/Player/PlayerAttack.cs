using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Melee combos. Which combo plays is picked when the first hit starts: one for
/// attacking while standing still, one while running, and one while (or just
/// after) blocking. Each swing is startup, a few active frames checking the sword's
/// (slightly enlarged) hitbox as it moves, then recovery, and lasts exactly as long as its slash
/// animation. Once a swing lands a hit, pressing attack starts the next hit right
/// away. A swing that hasn't landed plays out: pressing attack in its last part
/// queues the next hit and plays the rest of the swing faster; earlier presses
/// are ignored, so mashing doesn't speed anything up. The finisher always plays
/// out at normal speed. Jumping or blocking cancels a swing's recovery,
/// except the finisher's, which always plays out. Jumping ends the combo; tapping
/// block pauses it, and if that press parries, attacking again continues it from
/// the next hit (a plain block ends it). Getting hit cuts a swing short,
/// unless it has heavy armor (the slow finishers).
/// Each hit turns toward the closest enemy first and steps: forward a little
/// (stopping short of the enemy, so knockback doesn't carry it out of reach),
/// further when holding toward the enemy, or back when holding away. Every
/// attack press also opens a short sword/shield parry window (see ParryController).
/// Swinging interrupts a held block.
public class PlayerAttack : MonoBehaviour
{
    public enum HitboxSource { Sword, Shield, SwordAndShield }
    public enum ComboStart { Standing, Running, Shielding }

    [Serializable]
    public class AttackStep
    {
        public float startup = 0.06f;
        [Tooltip("How long the hit frames last: the hitbox hits anything it touches during them. The animator rebuild centres them on the swing's fastest moment.")]
        public float active = 0.25f;
        public float recovery = 0.2f;
        [Tooltip("Scales how hard this hit pushes the enemy back and how long it flinches.")]
        public float force = 1f;
        [Tooltip("What deals this hit: the sword, the shield, or either (for a shield strike that also swings the sword). An enemy is still hit at most once per swing.")]
        public HitboxSource hitbox = HitboxSource.Sword;
        [Tooltip("Heavy armor: getting hit during this swing doesn't stop it, flinch you or knock you back (you still take the damage).")]
        public bool armored;
        [Tooltip("The animation clip this hit plays (e.g. Attack_Jab). Empty uses the combo's Clip Prefix. Tools > Player Animations > Sync Combo Timings From Animator fills this in from the Animator.")]
        public string clip = "";
        [Tooltip("Playback speed of that clip (0 = its default).")]
        public float animSpeed;
    }

    [Serializable]
    public class ComboSet
    {
        public string name = "Combo";
        [Tooltip("How this combo starts: attack while standing still, while running, or while (or just after) blocking.")]
        public ComboStart start;
        [Tooltip("Its clips in Animations/Attack: hit 1 is <prefix>_1 (or just <prefix>), hit 2 <prefix>_2, and so on.")]
        public string clipPrefix = "Attack_Slash";
        [Tooltip("The hits in order; the last one is the finisher. Startup and recovery are set from the clips by Tools > Player Animations > Rebuild Player Animator, so each swing lasts as long as its animation. Active, force and hitbox are yours to tune.")]
        public AttackStep[] hits = new AttackStep[0];
    }

    [Header("Input")]
    public KeyCode attackKey = KeyCode.J;

    [Header("Combos")]
    [Tooltip("Each combo, and what starts it. Re-run Tools > Player Animations > Rebuild Player Animator after adding a combo or a hit.")]
    public ComboSet[] combos = DefaultCombos();
    [Tooltip("Attacking within this long after blocking (or a block's stun) ends still starts the Shielding combo.")]
    public float shieldComboWindow = 0.4f;
    [Tooltip("Moving faster than this (m/s) when a combo starts counts as running.")]
    public float runComboSpeed = 1f;
    [Tooltip("Pressing attack in this last fraction of a swing queues the next hit. Earlier presses are ignored.")]
    [Range(0f, 1f)] public float chainWindow = 0.2f;
    [Tooltip("Once the next hit is queued, the rest of the current swing (animation and timing) plays this much faster.")]
    public float tailSpeedup = 2.5f;
    [Tooltip("After a swing finishes, pressing attack within this long still continues the combo.")]
    public float comboWindow = 0.3f;
    [Tooltip("Tapping block mid-combo pauses the combo. If that block press parries, attacking within this long continues it from the next hit; a plain block (or a tap that catches nothing) ends it.")]
    public float parryResumeWindow = 1f;

    [Header("Step")]
    [Tooltip("Each combo hit steps forward up to this far, through its wind-up and hit frames.")]
    public float stepDistance = 1f;
    [Tooltip("Holding toward the locked-on enemy (or the way you face) steps this much further.")]
    public float forwardStepBonus = 0.6f;
    [Tooltip("Holding away from the locked-on enemy steps back this far instead.")]
    public float backStepDistance = 0.5f;
    [Tooltip("The step holds once the locked-on enemy is this close (centre to centre), and closes in again if it slides away.")]
    public float stepStopDistance = 1.4f;
    [Tooltip("How fast the step moves, in metres per second.")]
    public float stepSpeed = 6f;

    [Header("Hitbox")]
    [Tooltip("The sword's collider. Hits are checked against it, grown by Hitbox Padding, as it moves during each swing's active frames. Point it at the real sword's collider when the model is swapped in.")]
    public BoxCollider swordHitbox;
    [Tooltip("The shield's collider, used by combo hits set to Hitbox: Shield or Sword And Shield.")]
    public BoxCollider shieldHitbox;
    [Tooltip("Extra size added on every side of the hitbox collider, in metres.")]
    public float hitboxPadding = 0.15f;
    public LayerMask hitMask = ~0;
    // The sword can cover a lot of ground in one frame; check this many poses in between.
    const int SweepSteps = 4;

    /// Fired once per target struck.
    public event Action<IHittable> HitLanded;
    /// Fired when each combo hit begins, with its step (1-based).
    public event Action<int> SwingStarted;
    /// Where the most recent hit connected.
    public Vector3 LastHitPoint { get; private set; }

    public bool IsAttacking { get; private set; }
    /// True during a swing's active frames: its hitbox is live, and a strike caught
    /// on the sword or shield now is parried.
    public bool IsHitActive { get; private set; }
    public float TimeSinceSwingStart => Now - swingStartTime;
    /// True while the tail of a swing is sped up because the next hit is queued.
    public bool IsFastForwarding { get; private set; }
    /// The current (or last) swing's place in the combo, starting at 1. 0 before any swing.
    public int ComboStep { get; private set; }
    /// Which entry in combos is (or was last) playing.
    public int ComboIndex { get; private set; }
    public bool IsFinisher => ComboStep > 1 && ComboStep == Hits.Length;
    /// Mid-swing on a hit with heavy armor: damage doesn't interrupt it.
    public bool IsArmored => IsAttacking && ComboStep > 0 && ComboStep <= Hits.Length && Hits[ComboStep - 1].armored;

    // The current combo's hits.
    AttackStep[] Hits => combos.Length > 0 ? combos[Mathf.Clamp(ComboIndex, 0, combos.Length - 1)].hits : NoHits;
    static readonly AttackStep[] NoHits = new AttackStep[0];

    ParryController defense;
    PlayerMovement movement;
    PlayerHealth health;
    PlayerTargeting targeting;
    PlayerInputContext inputContext;
    PlayerClock clock;
    // The player's own clock (PlayerClock): slows or freezes while typing a Smite.
    float Now => PlayerClock.NowOf(clock);
    float Dt => PlayerClock.DtOf(clock);
    SmiteController smite;
    float swingStartTime = float.NegativeInfinity;
    float swingDuration;
    float lastSwingEndTime = float.NegativeInfinity;
    bool queued;
    bool inRecovery;
    // A combo paused by tapping block, resumable once that press parries.
    int pausedCombo = -1, pausedStep;
    float resumeUntil = float.NegativeInfinity;
    // This swing has hit something, so the next press can cut straight to the next hit.
    bool landed;
    bool chainNow;

    // Startup and recovery are fitted to the clips by Sync Combo Timings From Animator.
    public static ComboSet[] DefaultCombos() => new[]
    {
        new ComboSet
        {
            name = "Standing", start = ComboStart.Standing, clipPrefix = "",
            hits = new[]
            {
                new AttackStep { startup = 0.09f, active = 0.25f, recovery = 0.02f, force = 1f, hitbox = HitboxSource.SwordAndShield, clip = "Attack_Shield_Bash", animSpeed = 2.5f },
                new AttackStep { startup = 0.29f, active = 0.25f, recovery = 0.09f, force = 1f, clip = "Attack_Rising_Slash", animSpeed = 2f },
                new AttackStep { startup = 0.35f, active = 0.25f, recovery = 0.27f, force = 1f, clip = "Attack_Wide_Slash", animSpeed = 2.6f },
                new AttackStep { startup = 0.65f, active = 0.25f, recovery = 0.77f, force = 3f, armored = true, clip = "Attack_Low_Cleave", animSpeed = 1.8f },
            },
        },
        new ComboSet
        {
            name = "Running", start = ComboStart.Running, clipPrefix = "",
            hits = new[]
            {
                new AttackStep { startup = 0.22f, active = 0.25f, recovery = 0.73f, force = 1f, clip = "Attack_Sweep_Slash", animSpeed = 2f },
                new AttackStep { startup = 0.12f, active = 0.25f, recovery = 0.08f, force = 1f, hitbox = HitboxSource.SwordAndShield, clip = "Attack_Shield_Bash", animSpeed = 2f },
                new AttackStep { startup = 0.31f, active = 0.25f, recovery = 0.44f, force = 1f, clip = "Attack_Turning_Cleave", animSpeed = 2f },
                new AttackStep { startup = 0.20f, active = 0.25f, recovery = 0.47f, force = 3f, armored = true, clip = "Attack_Jab", animSpeed = 1.2f },
            },
        },
        new ComboSet
        {
            name = "Shielding", start = ComboStart.Shielding, clipPrefix = "",
            hits = new[]
            {
                new AttackStep { startup = 0.37f, active = 0.25f, recovery = 0.38f, force = 1f, clip = "Attack_Lunge_Slash", animSpeed = 1.5f },
                new AttackStep { startup = 0.14f, active = 0.25f, recovery = 0.30f, force = 1f, clip = "Attack_Jab", animSpeed = 1.6f },
                new AttackStep { startup = 0.20f, active = 0.25f, recovery = 0.24f, force = 1f, hitbox = HitboxSource.SwordAndShield, clip = "Attack_Shield_Bash", animSpeed = 1.3f },
                new AttackStep { startup = 0.46f, active = 0.25f, recovery = 0.26f, force = 2.25f, clip = "Attack_Rising_Slash", animSpeed = 1.3f },
            },
        },
    };

    void Awake()
    {
        defense = GetComponent<ParryController>();
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        targeting = GetComponent<PlayerTargeting>();
        inputContext = GetComponent<PlayerInputContext>();
        clock = GetComponent<PlayerClock>();
        smite = GetComponent<SmiteController>();
        if (swordHitbox == null) Debug.LogWarning("[PlayerAttack] No Sword Hitbox assigned, so sword hits can't land.", this);
        foreach (ComboSet set in combos)
            foreach (AttackStep step in set.hits)
                if (step.hitbox != HitboxSource.Sword && shieldHitbox == null)
                {
                    Debug.LogWarning($"[PlayerAttack] The {set.name} combo uses the shield, but no Shield Hitbox is assigned.", this);
                    return;
                }
    }

    void OnEnable()
    {
        if (health != null) health.Damaged += OnDamaged;
        if (defense != null)
        {
            defense.BlockPressed += OnBlockPressed;
            defense.AttackResolved += OnAttackResolved;
            defense.Whiffed += ClearPause;
        }
    }

    void OnDisable()
    {
        if (health != null) health.Damaged -= OnDamaged;
        if (defense != null)
        {
            defense.BlockPressed -= OnBlockPressed;
            defense.AttackResolved -= OnAttackResolved;
            defense.Whiffed -= ClearPause;
        }
        Interrupt();
    }

    // Tapping block during a swing's recovery (or just after a swing) cancels the
    // swing and pauses the combo; the finisher can't be cancelled.
    void OnBlockPressed()
    {
        bool hasNext = ComboStep > 0 && ComboStep < Hits.Length;
        if (IsAttacking && inRecovery && hasNext)
        {
            PauseCombo();
            StopAllCoroutines();
            IsAttacking = false;
            IsFastForwarding = false;
            inRecovery = false;
            ComboStep = 0;
            queued = false;
            chainNow = false;
        }
        else if (!IsAttacking && hasNext && Now - lastSwingEndTime <= comboWindow)
        {
            PauseCombo();
        }
    }

    void PauseCombo()
    {
        pausedCombo = ComboIndex;
        pausedStep = ComboStep;
        resumeUntil = float.NegativeInfinity;
    }

    void ClearPause()
    {
        pausedCombo = -1;
        resumeUntil = float.NegativeInfinity;
    }

    // A block-key parry lets the paused combo carry on; a plain block or a hit ends it.
    void OnAttackResolved(ParryResult result, float sinceInput)
    {
        if (pausedCombo < 0) return;
        if (result == ParryResult.Parry && !defense.LastParryWasAttack) resumeUntil = Now + parryResumeWindow;
        else ClearPause();
    }

    void OnDamaged(int amount)
    {
        if (!IsArmored) Interrupt();
    }

    /// Cuts the current swing short and ends the combo.
    public void Interrupt()
    {
        StopAllCoroutines();
        IsAttacking = false;
        IsHitActive = false;
        IsFastForwarding = false;
        inRecovery = false;
        ComboStep = 0;
        queued = false;
        landed = false;
        chainNow = false;
        ClearPause();
    }

    void Update()
    {
        // Through the input context: ignored while typing or smiting.
        if (!PlayerInputContext.KeyDown(inputContext, attackKey)) return;
        // With a Smite ready, the next attack is the Smite.
        if (smite != null && smite.TryUnleash()) return;
        PressAttack();
    }

    /// What pressing the attack key does: start a swing, or queue the next combo hit.
    public void PressAttack()
    {
        // Every press can parry, whether or not it starts or chains a swing.
        if (defense != null) defense.PressAttackParry();
        bool stunned = defense != null && defense.IsGuardStunned;
        if (stunned || combos.Length == 0) return;

        if (IsAttacking)
        {
            if (landed && ComboStep < Hits.Length) chainNow = true;
            else if (Now >= swingStartTime + swingDuration * (1f - chainWindow)) queued = true;
            return;
        }
        // After a block-key parry, pick the paused combo back up at its next hit.
        if (pausedCombo >= 0 && pausedCombo < combos.Length && Now <= resumeUntil && pausedStep < combos[pausedCombo].hits.Length)
        {
            ComboIndex = pausedCombo;
            int next = pausedStep + 1;
            ClearPause();
            StartCoroutine(Swing(next));
            return;
        }
        bool continues = ComboStep > 0 && ComboStep < Hits.Length && Now - lastSwingEndTime <= comboWindow;
        if (!continues) ComboIndex = ChooseCombo();
        if (Hits.Length == 0) return;
        StartCoroutine(Swing(continues ? ComboStep + 1 : 1));
    }

    /// Which combo a fresh attack starts: Shielding while (or just after) blocking,
    /// Running while moving, otherwise Standing. Falls back to the first combo.
    int ChooseCombo()
    {
        ComboStart start = ComboStart.Standing;
        if (defense != null && (defense.IsBlocking || Time.time - defense.LastGuardTime <= shieldComboWindow))
            start = ComboStart.Shielding;
        else if (movement != null && movement.HorizontalSpeed > runComboSpeed)
            start = ComboStart.Running;
        for (int i = 0; i < combos.Length; i++)
            if (combos[i].start == start) return i;
        return 0;
    }

    IEnumerator Swing(int step)
    {
        AttackStep s = Hits[step - 1];
        IsAttacking = true;
        ComboStep = step;
        queued = false;
        landed = false;
        chainNow = false;
        inRecovery = false;
        swingStartTime = Now;
        swingDuration = s.startup + s.active + s.recovery;
        CombatTarget lockTarget = targeting != null ? targeting.Closest() : null;
        if (lockTarget != null && movement != null) movement.FaceTowards(lockTarget.transform.position.x);
        StepForward(lockTarget, s);
        SwingStarted?.Invoke(step);
        for (float w = 0f; w < s.startup; w += Dt) yield return null;

        var struck = new HashSet<IHittable>();
        IsHitActive = true;
        var boxes = new List<BoxCollider>();
        CurrentHitboxes(boxes);
        var lastCenters = new Vector3[boxes.Count];
        var lastRotations = new Quaternion[boxes.Count];
        for (int b = 0; b < boxes.Count; b++) GetHitbox(boxes[b], out lastCenters[b], out lastRotations[b], out _);
        for (float t = 0f; t < s.active; t += Dt)
        {
            // Frozen (typing): the swing holds, and doesn't hit anything meanwhile.
            if (Dt <= 0f)
            {
                yield return null;
                continue;
            }
            for (int b = 0; b < boxes.Count; b++)
            {
                GetHitbox(boxes[b], out Vector3 center, out Quaternion rotation, out Vector3 size);
                // Sweep from last frame's pose to this one so a fast swing can't skip past.
                for (int i = 1; i <= SweepSteps; i++)
                {
                    float k = i / (float)SweepSteps;
                    Vector3 c = Vector3.Lerp(lastCenters[b], center, k);
                    Quaternion r = Quaternion.Slerp(lastRotations[b], rotation, k);
                    foreach (Collider hit in Physics.OverlapBox(c, size * 0.5f, r, hitMask, QueryTriggerInteraction.Ignore))
                    {
                        IHittable target = hit.GetComponentInParent<IHittable>();
                        if (target != null && struck.Add(target))
                        {
                            landed = true;
                            LastHitPoint = hit.ClosestPoint(c);
                            target.TakeHit(transform.right, s.force);
                            HitLanded?.Invoke(target);
                        }
                    }
                }
                lastCenters[b] = center;
                lastRotations[b] = rotation;
            }
            if (chainNow) break;
            yield return null;
        }
        IsHitActive = false;
        if (chainNow)
        {
            StartCoroutine(Swing(step + 1));
            yield break;
        }

        bool finisher = step == Hits.Length;
        inRecovery = true;
        for (float t = 0f; t < s.recovery; t += Dt * (IsFastForwarding ? tailSpeedup : 1f))
        {
            if (!finisher && CancelPressed())
            {
                // Jumping out ends the combo. (Tapping block is handled in OnBlockPressed.)
                IsAttacking = false;
                inRecovery = false;
                ComboStep = 0;
                yield break;
            }
            if (chainNow)
            {
                IsFastForwarding = false;
                StartCoroutine(Swing(step + 1));
                yield break;
            }
            IsFastForwarding = queued && !finisher;
            yield return null;
        }
        IsFastForwarding = false;
        inRecovery = false;

        if (queued && !finisher)
        {
            StartCoroutine(Swing(step + 1));
            yield break;
        }
        IsAttacking = false;
        lastSwingEndTime = Now;
        queued = false;
    }

    // Steps through the wind-up and hit frames, by what's held as the hit starts:
    // away from the enemy steps back; nothing or toward it steps forward (further if
    // toward), following the target and holding at stepStopDistance, so an enemy
    // still sliding back from the last hit stays in reach.
    void StepForward(CombatTarget target, AttackStep s)
    {
        if (movement == null) return;
        float duration = s.startup + s.active;
        int toward = target != null ? (target.transform.position.x >= transform.position.x ? 1 : -1) : movement.Facing;
        float held = movement.MoveInput * toward;
        if (held < 0f)
        {
            movement.Step(-toward * backStepDistance, stepSpeed, duration);
            return;
        }
        float distance = stepDistance + (held > 0f ? forwardStepBonus : 0f);
        movement.Step(toward * distance, stepSpeed, duration, target != null ? target.transform : null, stepStopDistance);
    }

    bool CancelPressed() =>
        movement != null && PlayerInputContext.KeyDown(inputContext, movement.jumpKey) && movement.IsGrounded;

    /// Fills boxes with the colliders dealing the current (or, between swings, the
    /// next) hit: the sword, the shield, or both. Unassigned ones are skipped.
    public void CurrentHitboxes(List<BoxCollider> boxes)
    {
        boxes.Clear();
        AttackStep[] hits = Hits;
        if (hits.Length == 0) return;
        HitboxSource source = hits[Mathf.Clamp(ComboStep - 1, 0, hits.Length - 1)].hitbox;
        if (source != HitboxSource.Shield && swordHitbox != null) boxes.Add(swordHitbox);
        if (source != HitboxSource.Sword && shieldHitbox != null) boxes.Add(shieldHitbox);
    }

    /// The blade's two ends right now: the ends of the sword collider's long axis,
    /// tip being the one farther from the hand. False if there's no sword hitbox.
    public bool TryGetBlade(out Vector3 tip, out Vector3 hilt)
    {
        if (swordHitbox == null)
        {
            tip = hilt = transform.position;
            return false;
        }
        Transform t = swordHitbox.transform;
        Vector3 size = Vector3.Scale(swordHitbox.size, t.lossyScale);
        float x = Mathf.Abs(size.x), y = Mathf.Abs(size.y), z = Mathf.Abs(size.z);
        Vector3 axis = x >= y && x >= z ? Vector3.right * swordHitbox.size.x
            : y >= z ? Vector3.up * swordHitbox.size.y : Vector3.forward * swordHitbox.size.z;
        Vector3 a = t.TransformPoint(swordHitbox.center + axis * 0.5f);
        Vector3 b = t.TransformPoint(swordHitbox.center - axis * 0.5f);
        Vector3 hand = t.parent != null ? t.parent.position : transform.position;
        bool aIsTip = (a - hand).sqrMagnitude >= (b - hand).sqrMagnitude;
        tip = aIsTip ? a : b;
        hilt = aIsTip ? b : a;
        return true;
    }

    /// A hitbox collider in world space right now, padded. False if it's null.
    public bool GetHitbox(BoxCollider box, out Vector3 center, out Quaternion rotation, out Vector3 size)
    {
        if (box == null)
        {
            center = transform.position;
            rotation = transform.rotation;
            size = Vector3.zero;
            return false;
        }
        Transform t = box.transform;
        center = t.TransformPoint(box.center);
        rotation = t.rotation;
        Vector3 scale = t.lossyScale;
        size = new Vector3(Mathf.Abs(scale.x * box.size.x), Mathf.Abs(scale.y * box.size.y), Mathf.Abs(scale.z * box.size.z))
            + Vector3.one * (hitboxPadding * 2f);
        return true;
    }

    void OnDrawGizmosSelected()
    {
        var boxes = new List<BoxCollider>();
        CurrentHitboxes(boxes);
        Gizmos.color = Color.yellow;
        foreach (BoxCollider box in boxes)
        {
            GetHitbox(box, out Vector3 center, out Quaternion rotation, out Vector3 size);
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
