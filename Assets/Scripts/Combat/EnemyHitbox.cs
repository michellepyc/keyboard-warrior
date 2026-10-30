using System;
using System.Collections.Generic;
using UnityEngine;

/// An enemy's strike: put it on the weapon or fist that hits (with a Box, Sphere or
/// Capsule collider shaped around it; it's made a trigger), anywhere under the
/// enemy's CombatTarget. It follows whatever bone it's parented to.
/// While open (see HitboxWindow, or call Open/Close from a script) it sweeps its
/// collider from last frame's pose to this one and:
/// - Touching the player's sword or shield while their attack-key parry window is
///   open parries it (their guard padding counts).
/// - Reaching the player's body waits out their late-parry grace, then lets their
///   ParryController decide: parry, block or hit. A hit deals Damage; a block pushes them back.
/// Each opening resolves at most once, then closes itself.
[RequireComponent(typeof(Collider))]
public class EnemyHitbox : MonoBehaviour
{
    [Tooltip("Name HitboxWindow uses to pick this hitbox (e.g. Sword, LeftFist).")]
    public string id = "Sword";
    public int damage = 1;
    [Tooltip("How hard a hit knocks the player back.")]
    public float hitKnockback = 8f;
    [Tooltip("How hard a blocked strike pushes the player back.")]
    public float blockKnockback = 4f;

    const int Substeps = 4;

    /// Every open hitbox, for the debug view.
    public static readonly List<EnemyHitbox> Active = new List<EnemyHitbox>();

    /// Fired once per opening: the result, or null if it closed without reaching the player.
    public event Action<ParryResult?> Resolved;

    public bool IsOpen { get; private set; }
    /// Who opened the current strike (e.g. a HitboxWindow), or null if a script did without saying.
    public object Opener { get; private set; }
    /// While paused (e.g. its owner is flinching) an open hitbox doesn't check anything.
    public bool Paused { get; set; }
    /// Reached the player's body and is waiting out their late-parry grace.
    public bool IsConnecting => IsOpen && connected;
    public CombatTarget Owner => owner;
    /// Where the strike comes from, for the player's facing check.
    public Vector3 Origin => ownerRoot.position;

    Collider shape;
    CombatTarget owner;
    Transform ownerRoot;
    int openDamage;
    float openHitKnockback, openBlockKnockback;
    Vector3 lastPosition;
    Quaternion lastRotation;
    bool connected;
    float connectTime;
    ParryController connectedTo;
    Vector3 contactPoint;
    readonly Collider[] results = new Collider[32];

    void Awake()
    {
        shape = GetComponent<Collider>();
        shape.isTrigger = true;
        owner = GetComponentInParent<CombatTarget>();
        if (owner != null) owner.Register(this);
        ownerRoot = owner != null ? owner.transform : transform.root;
    }

    void OnDisable() => Close();

    public void Open() => Open(damage, hitKnockback, blockKnockback);

    public void Open(int strikeDamage, float strikeHitKnockback, float strikeBlockKnockback) =>
        Open(strikeDamage, strikeHitKnockback, strikeBlockKnockback, null);

    /// Opens a strike on behalf of opener, which can then close only its own strike
    /// (two attack states blending into each other mustn't close each other's).
    public void Open(int strikeDamage, float strikeHitKnockback, float strikeBlockKnockback, object opener)
    {
        openDamage = strikeDamage;
        openHitKnockback = strikeHitKnockback;
        openBlockKnockback = strikeBlockKnockback;
        if (IsOpen) return;
        IsOpen = true;
        Opener = opener;
        connected = false;
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        Active.Add(this);
    }

    /// Ends the strike, whoever opened it. If it hadn't resolved yet, reports a miss.
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Opener = null;
        Active.Remove(this);
        Resolved?.Invoke(null);
    }

    /// Ends the strike only if opener is the one that opened it.
    public void Close(object opener)
    {
        if (IsOpen && Equals(Opener, opener)) Close();
    }

    // After animation, so a weapon on a bone is checked in this frame's pose.
    void LateUpdate()
    {
        if (!IsOpen || Time.deltaTime <= 0f) return; // frozen during hit-stop
        Physics.SyncTransforms();
        if (!Paused) Check();
        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }

    void Check()
    {
        float padding = 0f;
        foreach (ParryController p in ParryController.All) padding = Mathf.Max(padding, p.guardPadding);

        Collider guardHit = null, bodyHit = null;
        ParryController guardOwner = null, bodyOwner = null;
        for (int i = 1; i <= Substeps; i++)
        {
            float f = i / (float)Substeps;
            Vector3 position = Vector3.Lerp(lastPosition, transform.position, f);
            Quaternion rotation = Quaternion.Slerp(lastRotation, transform.rotation, f);

            int n = Overlap(position, rotation, 0f, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n && bodyHit == null; k++)
            {
                if (results[k].transform.IsChildOf(ownerRoot)) continue;
                ParryController player = results[k].GetComponentInParent<ParryController>();
                if (player == null) continue;
                bodyHit = results[k];
                bodyOwner = player;
            }
            // Growing the strike by the padding is the same as padding the sword and shield.
            n = Overlap(position, rotation, padding, QueryTriggerInteraction.Collide);
            for (int k = 0; k < n && guardHit == null; k++)
            {
                ParryController player = results[k].GetComponentInParent<ParryController>();
                if (player == null || !player.IsGuardCollider(results[k])) continue;
                guardHit = results[k];
                guardOwner = player;
            }
        }

        Vector3 center = shape.bounds.center;
        if (guardHit != null && guardOwner.CanGuardParry)
        {
            Resolve(guardOwner, true, guardHit.ClosestPoint(center));
            return;
        }
        if (bodyHit != null)
        {
            contactPoint = bodyHit.ClosestPoint(center);
            if (!connected)
            {
                connected = true;
                connectTime = Time.time;
                connectedTo = bodyOwner;
            }
        }
        if (!connected) return;

        // Reaching the body doesn't land the hit straight away: a parry pressed (or a
        // swing that meets this strike) within the grace still counts.
        if (!connectedTo.CanKeyParry(Origin) && Time.time - connectTime < connectedTo.lateParryGrace) return;
        Resolve(connectedTo, false, contactPoint);
    }

    void Resolve(ParryController player, bool guardContact, Vector3 point)
    {
        IsOpen = false;
        Opener = null;
        Active.Remove(this);
        ParryResult result = player.ResolveIncomingAttack(Origin, guardContact, point);
        if (result == ParryResult.Hit && player.TryGetComponent(out PlayerHealth health))
            health.TakeDamage(openDamage, Origin, openHitKnockback);
        else if (result == ParryResult.Blocked && player.TryGetComponent(out PlayerMovement movement))
            movement.Knockback((player.transform.position.x >= Origin.x ? 1f : -1f) * openBlockKnockback);
        if (owner != null) owner.NotifyStrike(this, result);
        Resolved?.Invoke(result);
    }

    /// The collider's shape at this pose (grown by padding), into results. Returns the count.
    int Overlap(Vector3 position, Quaternion rotation, float padding, QueryTriggerInteraction triggers)
    {
        Vector3 scale = transform.lossyScale;
        switch (shape)
        {
            case BoxCollider box:
            {
                Vector3 center = position + rotation * Vector3.Scale(box.center, scale);
                Vector3 half = Abs(Vector3.Scale(box.size, scale)) * 0.5f + Vector3.one * padding;
                return Physics.OverlapBoxNonAlloc(center, half, results, rotation, ~0, triggers);
            }
            case SphereCollider sphere:
            {
                Vector3 center = position + rotation * Vector3.Scale(sphere.center, scale);
                float radius = sphere.radius * MaxAbs(scale) + padding;
                return Physics.OverlapSphereNonAlloc(center, radius, results, ~0, triggers);
            }
            case CapsuleCollider capsule:
            {
                Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
                Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 s = Abs(scale);
                float along = capsule.direction == 0 ? s.x : capsule.direction == 1 ? s.y : s.z;
                float across = capsule.direction == 0 ? Mathf.Max(s.y, s.z) : capsule.direction == 1 ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.y);
                float radius = capsule.radius * across;
                float reach = Mathf.Max(capsule.height * along * 0.5f - radius, 0f);
                Vector3 offset = rotation * axis * reach;
                return Physics.OverlapCapsuleNonAlloc(center + offset, center - offset, radius + padding, results, ~0, triggers);
            }
            default:
            {
                Bounds b = shape.bounds;
                return Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * padding, results, Quaternion.identity, ~0, triggers);
            }
        }
    }

    /// The hitbox as a world-space box (spheres and capsules as their bounding box), for drawing.
    public void GetWorldBox(out Vector3 center, out Quaternion rotation, out Vector3 size)
    {
        Vector3 scale = transform.lossyScale;
        rotation = transform.rotation;
        switch (shape)
        {
            case BoxCollider box:
                center = transform.TransformPoint(box.center);
                size = Abs(Vector3.Scale(box.size, scale));
                return;
            case SphereCollider sphere:
                center = transform.TransformPoint(sphere.center);
                size = Vector3.one * (sphere.radius * MaxAbs(scale) * 2f);
                return;
            case CapsuleCollider capsule:
            {
                center = transform.TransformPoint(capsule.center);
                Vector3 s = Abs(scale);
                float d = capsule.radius * 2f;
                Vector3 local = capsule.direction == 0 ? new Vector3(capsule.height, d, d)
                    : capsule.direction == 1 ? new Vector3(d, capsule.height, d) : new Vector3(d, d, capsule.height);
                size = Vector3.Scale(local, s);
                return;
            }
            default:
                center = shape.bounds.center;
                rotation = Quaternion.identity;
                size = shape.bounds.size;
                return;
        }
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
}
