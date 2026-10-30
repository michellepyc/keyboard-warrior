using System;
using System.Collections.Generic;
using UnityEngine;

/// Marks an enemy for the player's systems (lock-on, combo step, blocking turning
/// toward it, HUD, debug view) and ties its EnemyHitboxes together. Put it on the
/// enemy's root. See Assets/Scripts/Combat/ADDING_ENEMIES.md.
/// - Its strikes come from EnemyHitbox components on its weapons or fists, opened by
///   HitboxWindow behaviours on its Animator's attack states (or by its own script).
/// - Without an AI script of its own, it reacts through Animator triggers: a parried
///   strike sets Parried Trigger and stuns it; a hit from the player sets Hurt Trigger.
///   Triggers the Animator doesn't have are skipped.
/// - If another IHittable (like EnemyAttacker) is on the same object, hits go to that instead.
public class CombatTarget : MonoBehaviour, IHittable
{
    [Tooltip("Animator found in children if left empty.")]
    public Animator animator;
    [Tooltip("Set on the Animator when one of its strikes is parried.")]
    public string parriedTrigger = "Parried";
    [Tooltip("Set on the Animator when the player hits it.")]
    public string hurtTrigger = "Hurt";
    [Tooltip("How long a parried strike leaves it staggered (the player's hits count as counters meanwhile).")]
    public float stunTime = 1.2f;
    [Tooltip("Shown while an attack is about to land (HitboxWindow's warn lead). Optional.")]
    public GameObject warnMarker;
    [Tooltip("The player's hits push it back this fast (needs a non-kinematic Rigidbody). Ignored when another script handles hits.")]
    public float hitPushSpeed = 4f;

    /// Every active target, for the player's lock-on.
    public static readonly List<CombatTarget> All = new List<CombatTarget>();
    /// Fired when the player hits any target. The bool is whether it was staggered.
    public static event Action<CombatTarget, bool> AnyDamaged;

    /// Fired when the player hits this target. True if it was staggered at the time.
    public event Action<bool> Damaged;
    /// Fired when one of its strikes resolves: Parry, Blocked, or Hit. Not fired for a miss.
    public event Action<EnemyHitbox, ParryResult> StrikeResolved;

    public bool IsStunned => Time.time < stunnedUntil;
    public bool IsWarning => Time.time < warnUntil;
    /// Its hitboxes that are live right now.
    public bool IsStriking
    {
        get
        {
            foreach (EnemyHitbox h in hitboxes) if (h.IsOpen) return true;
            return false;
        }
    }

    readonly List<EnemyHitbox> hitboxes = new List<EnemyHitbox>();
    IHittable handler;
    Rigidbody body;
    float stunnedUntil = float.NegativeInfinity;
    float warnUntil = float.NegativeInfinity;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        body = GetComponent<Rigidbody>();
        foreach (IHittable h in GetComponents<IHittable>())
            if (!ReferenceEquals(h, this)) handler = h;
        GetComponentsInChildren(true, hitboxes);
        if (warnMarker != null) warnMarker.SetActive(false);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        if (warnMarker != null && warnMarker.activeSelf != IsWarning) warnMarker.SetActive(IsWarning);
    }

    /// The hitbox with this id, or the first one if the id is empty. Null if there's none.
    public EnemyHitbox GetHitbox(string id)
    {
        foreach (EnemyHitbox h in hitboxes)
            if (string.IsNullOrEmpty(id) || h.id == id) return h;
        return null;
    }

    public void Register(EnemyHitbox hitbox)
    {
        if (!hitboxes.Contains(hitbox)) hitboxes.Add(hitbox);
    }

    public void Stun(float duration) => stunnedUntil = Mathf.Max(stunnedUntil, Time.time + duration);
    public void EndStun() => stunnedUntil = float.NegativeInfinity;

    /// Shows the attack warning for this long (extends, never shortens it).
    public void WarnFor(float seconds) => warnUntil = Mathf.Max(warnUntil, Time.time + seconds);
    public void EndWarning() => warnUntil = float.NegativeInfinity;

    /// Called by its hitboxes once a strike lands on the player (or their guard).
    public void NotifyStrike(EnemyHitbox hitbox, ParryResult result)
    {
        if (result == ParryResult.Parry)
        {
            Stun(stunTime);
            SetTrigger(parriedTrigger);
            // A staggered enemy's other weapons stop too.
            foreach (EnemyHitbox h in hitboxes) if (h != hitbox) h.Close();
        }
        StrikeResolved?.Invoke(hitbox, result);
    }

    /// Lets the player's systems know it was hit (HUD counters and the like).
    public void ReportDamaged(bool staggered)
    {
        Damaged?.Invoke(staggered);
        AnyDamaged?.Invoke(this, staggered);
    }

    public void TakeHit(Vector3 direction, float force = 1f)
    {
        if (handler != null)
        {
            handler.TakeHit(direction, force);
            return;
        }
        ReportDamaged(IsStunned);
        SetTrigger(hurtTrigger);
        if (body != null && !body.isKinematic)
            body.velocity = new Vector3(Mathf.Sign(direction.x) * hitPushSpeed * force, body.velocity.y, body.velocity.z);
    }

    void SetTrigger(string trigger)
    {
        if (animator == null || string.IsNullOrEmpty(trigger) || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == trigger && p.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(trigger);
                return;
            }
        }
    }
}
