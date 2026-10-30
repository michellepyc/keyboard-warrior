using UnityEngine;

/// Basic lock-on: finds the closest enemy so attacks and blocking can turn toward it.
public class PlayerTargeting : MonoBehaviour
{
    [Tooltip("Only enemies within this horizontal distance are locked on to.")]
    public float lockRange = 8f;

    /// The nearest active enemy within lockRange, or null.
    public CombatTarget Closest()
    {
        CombatTarget best = null;
        float bestDist = lockRange;
        foreach (CombatTarget enemy in CombatTarget.All)
        {
            float dist = Mathf.Abs(enemy.transform.position.x - transform.position.x);
            if (dist <= bestDist)
            {
                best = enemy;
                bestDist = dist;
            }
        }
        return best;
    }
}
