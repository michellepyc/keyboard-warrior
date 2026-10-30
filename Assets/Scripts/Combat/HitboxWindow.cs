using UnityEngine;

/// Put on an attack state in an enemy's Animator (Add Behaviour in the state's
/// Inspector). Opens one of its EnemyHitboxes for part of the animation, so the
/// strike is live exactly while the weapon swings through, at any playback speed.
/// The attack warning shows for Warn Lead seconds before it opens. Leaving the
/// state (interrupted, parried...) closes it. Looping states open once per loop.
/// Each window only closes the strike it opened: when two attack states sharing a
/// hitbox blend into each other, the second waits for the first's strike to end
/// instead of cutting it off, and the first can't close the second's.
public class HitboxWindow : StateMachineBehaviour
{
    [Tooltip("Id of the EnemyHitbox to open (empty = the enemy's first hitbox).")]
    public string hitbox = "Sword";
    [Tooltip("Opens at this point of the animation (0 = start, 1 = end).")]
    [Range(0f, 1f)] public float openAt = 0.4f;
    [Tooltip("Closes at this point, if it hasn't landed yet.")]
    [Range(0f, 1f)] public float closeAt = 0.55f;
    [Tooltip("Seconds of warning before it opens (0 = none).")]
    public float warnLead = 0.2f;

    [Header("Overrides (-1 = use the hitbox's own)")]
    public int damage = -1;
    public float hitKnockback = -1f;
    public float blockKnockback = -1f;

    EnemyHitbox target;
    CombatTarget owner;
    int doneLoop = -1;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (target == null)
        {
            owner = animator.GetComponentInParent<CombatTarget>();
            target = owner != null ? owner.GetHitbox(hitbox) : null;
            if (target == null)
                foreach (EnemyHitbox h in animator.GetComponentsInChildren<EnemyHitbox>())
                    if (string.IsNullOrEmpty(hitbox) || h.id == hitbox) { target = h; break; }
            if (target == null) Debug.LogWarning($"[HitboxWindow] No EnemyHitbox \"{hitbox}\" under {animator.name}.", animator);
        }
        doneLoop = -1;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (target == null) return;
        float t = stateInfo.normalizedTime;
        int loop = Mathf.FloorToInt(t);
        if (!stateInfo.loop && loop > 0) { t = 1f; loop = 0; }
        float phase = t - loop;

        if (target.IsOpen)
        {
            if (Equals(target.Opener, this) && (phase >= closeAt || loop != doneLoop)) target.Close(this);
            return; // our strike is live, or another state's is (wait for it to end)
        }
        if (loop == doneLoop) return; // already struck (or missed) this loop

        if (phase >= openAt && phase < closeAt)
        {
            doneLoop = loop;
            target.Open(damage >= 0 ? damage : target.damage,
                hitKnockback >= 0f ? hitKnockback : target.hitKnockback,
                blockKnockback >= 0f ? blockKnockback : target.blockKnockback, this);
            if (owner != null) owner.EndWarning();
        }
        else if (phase < openAt && owner != null && warnLead > 0f)
        {
            float secondsToOpen = (openAt - phase) * stateInfo.length;
            if (secondsToOpen <= warnLead) owner.WarnFor(secondsToOpen);
        }
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (target != null) target.Close(this);
        if (owner != null) owner.EndWarning();
    }
}
