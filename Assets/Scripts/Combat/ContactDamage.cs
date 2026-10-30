using UnityEngine;

/// Hurts the player on touch. Disable the component to make it harmless
/// (e.g. while an enemy is staggered).
public class ContactDamage : MonoBehaviour
{
    public int damage = 1;
    public float knockback = 8f;
}
