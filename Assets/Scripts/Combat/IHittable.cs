using UnityEngine;

/// Anything the player's attack can hit.
public interface IHittable
{
    /// direction is the attacker's forward at the moment of the hit. force scales
    /// the reaction (1 for a normal hit, more for a combo finisher).
    void TakeHit(Vector3 direction, float force = 1f);
}
