using System;
using System.Collections;
using UnityEngine;

/// Player hit points with brief invulnerability after each hit, contact damage
/// from anything with an enabled ContactDamage (blocking toward it stops that and
/// just pushes you back), and respawn on death.
[RequireComponent(typeof(CharacterController))]
public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 5;
    [Tooltip("Seconds of invulnerability after taking damage.")]
    public float invulnerableTime = 0.8f;
    public float blinkInterval = 0.08f;
    [Tooltip("Extra distance beyond the capsule that counts as touching.")]
    public float contactSkin = 0.1f;
    [Tooltip("Blocking toward something that deals contact damage pushes you back this hard instead of hurting.")]
    public float contactBlockPush = 4f;
    [Tooltip("Long enough for the death animation to play out.")]
    public float respawnDelay = 2.5f;

    /// Fired with the damage amount after health drops.
    public event Action<int> Damaged;
    public event Action Died;
    public event Action Respawned;

    public int Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    CharacterController controller;
    PlayerMovement movement;
    ParryController defense;
    PlayerAttack attack;
    PlayerVisuals visuals;
    Vector3 spawnPoint;
    float invulnerableUntil;
    float nextContactPush;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        defense = GetComponent<ParryController>();
        attack = GetComponent<PlayerAttack>();
        visuals = GetComponent<PlayerVisuals>();
        Health = maxHealth;
        spawnPoint = transform.position;
    }

    void Update()
    {
        if (!IsDead) CheckContact();

        if (visuals != null)
        {
            bool blinkOn = Mathf.Repeat(Time.time, blinkInterval * 2f) < blinkInterval;
            // Stay visible while dead so the death animation shows.
            visuals.SetVisible(IsDead || !IsInvulnerable || blinkOn);
        }
    }

    public void TakeDamage(int amount, Vector3 sourcePosition, float knockback)
    {
        if (IsDead || IsInvulnerable) return;

        Health = Mathf.Max(0, Health - amount);
        invulnerableUntil = Time.time + invulnerableTime;
        // A heavy-armored swing (a slow finisher) isn't knocked back.
        if (movement != null && (attack == null || !attack.IsArmored))
            movement.Knockback(Mathf.Sign(transform.position.x - sourcePosition.x) * knockback);
        Damaged?.Invoke(amount);

        if (Health == 0) StartCoroutine(Respawn());
    }

    void CheckContact()
    {
        if (IsInvulnerable) return;

        Vector3 center = transform.position + controller.center;
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        float r = controller.radius + contactSkin;

        foreach (Collider c in Physics.OverlapCapsule(top, bottom, r, ~0, QueryTriggerInteraction.Collide))
        {
            ContactDamage source = c.GetComponentInParent<ContactDamage>();
            if (source != null && source.enabled)
            {
                Vector3 from = c.bounds.center;
                if (defense != null && defense.IsBlocking && defense.IsFacing(from))
                {
                    if (movement != null && Time.time >= nextContactPush)
                    {
                        movement.Knockback(Mathf.Sign(transform.position.x - from.x) * contactBlockPush);
                        nextContactPush = Time.time + 0.3f;
                    }
                    return;
                }
                TakeDamage(source.damage, from, source.knockback);
                return;
            }
        }
    }

    IEnumerator Respawn()
    {
        IsDead = true;
        SetControlsEnabled(false);
        Died?.Invoke();
        yield return new WaitForSeconds(respawnDelay);

        // CharacterController overrides direct position changes unless it's disabled.
        controller.enabled = false;
        transform.position = spawnPoint;
        controller.enabled = true;

        Health = maxHealth;
        IsDead = false;
        invulnerableUntil = Time.time + invulnerableTime;
        SetControlsEnabled(true);
        Respawned?.Invoke();
    }

    void SetControlsEnabled(bool on)
    {
        if (movement != null) movement.enabled = on;
        if (defense != null) defense.enabled = on;
        if (attack != null) attack.enabled = on;
    }
}
