using UnityEngine;

/// Physics-driven melee enemy AI.
/// - Patrols around its spawn until it sees the player (line of sight).
/// - Engaged, it holds a randomized spacing, re-rolled every few seconds.
/// - Attacks with a telegraphed wind-up (sometimes delayed to bait an early
///   parry), then lunges; the hit lands when its strike hitbox (an EnemyHitbox on a
///   child, made at runtime if Strike Hitbox is empty) reaches the player.
/// - Steps back when the player swings at it, sometimes waits out a held
///   block, retreats after taking hits, and jumps up to a player on a ledge.
/// - Never walks off ledges. A parried attack staggers it.
/// - Only its strikes deal damage. If a ContactDamage component is added, touching
///   it hurts too, except while it's lunging, recovering, or staggered.
/// - The player's hits knock it back and flinch it briefly. Mid-attack, the
///   flinch only pauses the wind-up or lunge; only a parry cancels an attack.
/// - While the player's combo keeps hitting it, it doesn't back off or dodge;
///   it can still attack back (through its wind-up armor).
[RequireComponent(typeof(Rigidbody), typeof(CombatTarget))]
public class EnemyAttacker : MonoBehaviour, IHittable
{
    enum State { Patrol, Pause, Engage, Retreat, Hover, Windup, Lunge, Recover, Stunned, Hurt }

    public ParryController target;
    public int strikeDamage = 1;
    [Tooltip("Training dummy: stands still and never attacks or deals contact damage, but still takes hits (flash, knockback, flinch). Untick for the full AI.")]
    public bool dummy = true;

    [Header("Senses")]
    public float detectRange = 9f;
    [Tooltip("Gives up the chase once the player is this far away (or out of sight and past detect range).")]
    public float loseRange = 13f;
    public LayerMask obstacleMask = ~0;

    [Header("Movement")]
    public float patrolSpeed = 1.5f;
    public float patrolRadius = 4f;
    public float chaseSpeed = 3.5f;
    public float retreatSpeed = 4.5f;
    public float acceleration = 30f;
    public float extraGravity = 15f;
    public float jumpSpeed = 9f;
    [Tooltip("Jumps toward the player when they're at least this much higher and close by.")]
    public float jumpHeightTrigger = 0.8f;
    public float jumpCooldown = 1.5f;

    [Header("Spacing")]
    [Tooltip("Preferred distance to the player, re-rolled every few seconds.")]
    public Vector2 spacingRange = new Vector2(1.6f, 2.3f);
    public Vector2 spacingRerollRange = new Vector2(1f, 2.5f);
    [Tooltip("Starts an attack when the player is within this horizontal distance.")]
    public float attackRange = 2.4f;
    public Vector2 attackCooldownRange = new Vector2(0.6f, 1.6f);

    [Header("Attack")]
    public float windup = 0.55f;
    public float windupJitter = 0.12f;
    [Tooltip("Chance the wind-up holds longer than usual, to bait an early parry.")]
    [Range(0f, 1f)] public float delayedAttackChance = 0.2f;
    public float delayedWindupExtra = 0.35f;
    public float lungeSpeed = 14f;
    public float lungeTime = 0.16f;
    [Tooltip("Size of the strike hitbox, placed just in front of the enemy on the side it lunges toward.")]
    public Vector3 strikeBox = new Vector3(0.9f, 1.8f, 1.5f);
    [Tooltip("The EnemyHitbox (with a BoxCollider) used for the lunge. Made as a child at runtime if empty.")]
    public EnemyHitbox strikeHitbox;
    public float recoverTime = 0.4f;
    public float stunTime = 1.2f;
    public float staggerSpeed = 6f;

    [Header("Reactions")]
    [Tooltip("Chance to step back when the player swings at it nearby.")]
    [Range(0f, 1f)] public float dodgeChance = 0.4f;
    [Tooltip("How long after the player starts a swing it reacts (decides whether to dodge).")]
    public float dodgeReactionTime = 0.2f;
    [Tooltip("Chance to hover instead of attacking while the player holds block.")]
    [Range(0f, 1f)] public float waitOutBlockChance = 0.5f;
    [Tooltip("Chance to back off after a hit (always does after two quick hits).")]
    [Range(0f, 1f)] public float retreatAfterHitChance = 0.6f;
    [Tooltip("After being hit, it can't back off or dodge for this long while the player keeps attacking, so a combo that's landing keeps landing. It reacts as usual once the combo ends.")]
    public float comboLockTime = 0.8f;
    [Tooltip("How long a normal hit stuns it (scaled by the hit's force). An attack in progress pauses for this long, then carries on.")]
    public float hitStunTime = 0.12f;

    [Header("Knockback")]
    public float playerHitKnockback = 8f;
    public float playerBlockKnockback = 4f;
    public float hitPushSpeed = 7f;
    public float counterPushSpeed = 9f;
    public float pushDecay = 30f;

    [Header("Colors")]
    public Renderer bodyRenderer;
    public Color idleColor = new Color(0.8f, 0.2f, 0.2f);
    public Color windupColor = new Color(1f, 0.85f, 0.1f);
    public Color strikeColor = Color.white;
    public Color stunColor = new Color(0.5f, 0.5f, 0.5f);
    public Color hitFlashColor = Color.white;
    public float hitFlashTime = 0.1f;

    [Header("Attack warning")]
    [Tooltip("For this long before each lunge it blinks Warn Color and shows a marker above its head. A delayed (bait) wind-up only warns at the real moment.")]
    public float warnLead = 0.2f;
    public Color warnColor = new Color(1f, 0.35f, 0.1f);

    public bool IsStunned => state == State.Stunned;
    public bool IsLunging => state == State.Lunge;
    /// World-space centre of the strike box (its size is strikeBox).
    public Vector3 StrikeCenter => transform.position + Vector3.right * (lungeDir * (halfWidth + strikeBox.x * 0.5f));

    Rigidbody rb;
    CombatTarget combat;
    ContactDamage contactDamage;
    PlayerHealth targetHealth;
    PlayerAttack targetAttack;
    float halfWidth, halfHeight;
    Vector3 home;

    State state;
    float stateTimer;
    bool engaged;
    int facing = 1;
    float desiredSpeed, moveX, push;
    float patrolGoal;
    float preferredSpacing, nextSpacingRoll;
    float nextAttackTime, nextJumpTime;
    float windupRamp, windupElapsed;
    int lungeDir;
    float lungeTimer;
    bool playerWasSwinging;
    int hitStreak;
    float lastHurtTime = float.NegativeInfinity;
    float lastHitTakenTime = float.NegativeInfinity;
    float flashUntil;
    float flinchUntil;
    float dodgeCheckAt = float.NegativeInfinity;
    GameObject warnMarker;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Frictionless so it slides along walls instead of sticking to them mid-jump.
        Collider col = GetComponent<Collider>();
        col.material = new PhysicMaterial("EnemyNoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
        };
        halfWidth = col.bounds.extents.x;
        halfHeight = col.bounds.extents.y;

        if (bodyRenderer == null) bodyRenderer = GetComponent<Renderer>();
        if (target == null) target = FindObjectOfType<ParryController>();
        targetHealth = target.GetComponent<PlayerHealth>();
        targetAttack = target.GetComponent<PlayerAttack>();
        contactDamage = GetComponent<ContactDamage>();
        combat = GetComponent<CombatTarget>();
        if (combat == null) combat = gameObject.AddComponent<CombatTarget>();
        if (strikeHitbox == null)
        {
            var go = new GameObject("StrikeHitbox");
            go.transform.SetParent(transform, false);
            go.AddComponent<BoxCollider>().isTrigger = true;
            strikeHitbox = go.AddComponent<EnemyHitbox>();
            strikeHitbox.id = "Strike";
        }
        warnMarker = CreateWarnMarker();

        home = transform.position;
        preferredSpacing = Random.Range(spacingRange.x, spacingRange.y);
        Enter(State.Pause, Random.Range(0.5f, 1.5f));
    }

    void OnEnable() => strikeHitbox.Resolved += OnStrikeResolved;
    void OnDisable() => strikeHitbox.Resolved -= OnStrikeResolved;

    bool FlinchingMidAttack => Time.time < flinchUntil && (state == State.Windup || state == State.Lunge);

    void Update()
    {
        strikeHitbox.Paused = FlinchingMidAttack;
        if (dummy)
        {
            UpdateDummy();
            UpdateColor();
            return;
        }
        // A flinch mid-attack freezes it in place; the attack resumes afterwards.
        if (FlinchingMidAttack)
        {
            desiredSpeed = 0f;
            UpdateColor();
            return;
        }
        stateTimer -= Time.deltaTime;

        if (CanSeePlayer()) engaged = true;
        else if (!PlayerAlive || Dist > loseRange || Dist > detectRange) engaged = false;

        // React to a swing a moment after it starts, like a person would, rather than on the press.
        bool playerSwinging = targetAttack != null && targetAttack.IsAttacking;
        if (playerSwinging && !playerWasSwinging) dodgeCheckAt = Time.time + dodgeReactionTime;
        playerWasSwinging = playerSwinging;
        bool reactToSwing = false;
        if (Time.time >= dodgeCheckAt)
        {
            reactToSwing = playerSwinging;
            dodgeCheckAt = float.PositiveInfinity;
        }

        switch (state)
        {
            case State.Patrol: UpdatePatrol(); break;
            case State.Pause: UpdatePause(); break;
            case State.Engage: UpdateEngage(reactToSwing); break;
            case State.Retreat: UpdateRetreat(); break;
            case State.Hover: UpdateHover(reactToSwing); break;
            case State.Windup: UpdateWindup(); break;
            case State.Lunge: break; // driven from FixedUpdate
            case State.Recover:
                desiredSpeed = 0f;
                if (stateTimer <= 0f) Enter(State.Engage);
                break;
            case State.Stunned:
                desiredSpeed = 0f;
                if (stateTimer <= 0f) Enter(!BeingComboed && Random.value < 0.5f ? State.Retreat : State.Engage, 0.4f);
                break;
            case State.Hurt: UpdateHurt(); break;
        }

        UpdateColor();
    }

    bool Warning => state == State.Windup && stateTimer <= warnLead;

    void UpdateColor()
    {
        if (bodyRenderer != null)
            bodyRenderer.material.color = Time.time < flashUntil ? hitFlashColor : StateColor();
        if (warnMarker != null && warnMarker.activeSelf != Warning) warnMarker.SetActive(Warning);
        if (Warning) combat.WarnFor(stateTimer);
    }

    /// A small glowing ball above its head, shown just before each lunge.
    GameObject CreateWarnMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "AttackWarning (runtime)";
        Destroy(marker.GetComponent<Collider>());
        marker.transform.SetParent(transform, false);
        // Undo the body's non-uniform scale so the marker stays a round 0.3 m ball.
        Vector3 s = transform.lossyScale;
        marker.transform.localScale = new Vector3(0.3f / s.x, 0.3f / s.y, 0.3f / s.z);
        marker.transform.localPosition = new Vector3(0f, (halfHeight + 0.4f) / s.y, 0f);
        // Drawn over the scene and the typing box's background (but under its letters, so
        // they stay readable), so an incoming attack is never hidden.
        MeshFilter filter = marker.GetComponent<MeshFilter>();
        filter.sharedMesh = WhiteVertexCopy(filter.sharedMesh);
        Renderer r = marker.GetComponent<Renderer>();
        r.material = OnTopMaterial(warnColor, 3996);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        marker.SetActive(false);
        return marker;
    }

    /// An unlit material (UI/Default, always in builds) that draws over everything,
    /// after the given render queue's turn. UI/Default tints by vertex colour, so use it
    /// on a mesh with white vertex colours (WhiteVertexCopy).
    public static Material OnTopMaterial(Color color, int renderQueue)
    {
        var m = new Material(Shader.Find("UI/Default")) { color = color, renderQueue = renderQueue };
        m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        return m;
    }

    /// A copy of the mesh with every vertex coloured white.
    public static Mesh WhiteVertexCopy(Mesh source)
    {
        Mesh copy = Instantiate(source);
        var colors = new Color[copy.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        copy.colors = colors;
        return copy;
    }

    /// Stands in place: only flinches when hit, then goes back to standing.
    void UpdateDummy()
    {
        stateTimer -= Time.deltaTime;
        desiredSpeed = 0f;
        if (state == State.Hurt ? stateTimer <= 0f : state != State.Pause) Enter(State.Pause);
        if (contactDamage != null) contactDamage.enabled = false;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (FlinchingMidAttack)
        {
            moveX = 0f;
        }
        else if (state == State.Lunge)
        {
            lungeTimer -= dt;
            // Hold still while a connected strike waits out the player's late-parry grace.
            moveX = strikeHitbox.IsConnecting ? 0f : lungeDir * lungeSpeed;
            // Missed: closing it reports null, which ends the lunge.
            if (lungeTimer <= 0f && !strikeHitbox.IsConnecting) strikeHitbox.Close();
        }
        else
        {
            moveX = Mathf.MoveTowards(moveX, desiredSpeed, acceleration * dt);
        }

        // Don't walk (or lunge) off ledges. Knockback can still push it off.
        if (moveX != 0f && Grounded() && !GroundAhead(moveX > 0f ? 1 : -1)) moveX = 0f;

        push = Mathf.MoveTowards(push, 0f, pushDecay * dt);
        rb.velocity = new Vector3(moveX + push, rb.velocity.y, 0f);
        rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
    }

    // ---------------------------------------------------------------- states

    void Enter(State next, float duration = 0f)
    {
        state = next;
        stateTimer = duration;
        if (next != State.Lunge) strikeHitbox.Close();
        if (next == State.Stunned) combat.Stun(duration);
        else combat.EndStun();
        if (contactDamage != null)
            contactDamage.enabled = next != State.Lunge && next != State.Recover && next != State.Stunned;

        switch (next)
        {
            case State.Patrol:
                patrolGoal = home.x + Random.Range(-patrolRadius, patrolRadius);
                break;
            case State.Hover:
                desiredSpeed = (Random.value < 0.5f ? -1f : 1f) * 0.8f;
                break;
            case State.Windup:
                desiredSpeed = 0f;
                windupRamp = windup + Random.Range(-windupJitter, windupJitter);
                windupElapsed = 0f;
                stateTimer = windupRamp + (Random.value < delayedAttackChance ? delayedWindupExtra : 0f);
                break;
            case State.Lunge:
                lungeDir = facing;
                lungeTimer = lungeTime;
                PlaceStrikeHitbox();
                strikeHitbox.Open(strikeDamage, playerHitKnockback, playerBlockKnockback);
                break;
        }
    }

    void UpdatePatrol()
    {
        if (engaged) { Enter(State.Engage); return; }

        int dir = patrolGoal > X ? 1 : -1;
        facing = dir;
        desiredSpeed = dir * patrolSpeed;
        if (Mathf.Abs(patrolGoal - X) < 0.2f || PathBlocked(dir) || stateTimer <= 0f)
            Enter(State.Pause, Random.Range(0.5f, 2f));
    }

    void UpdatePause()
    {
        desiredSpeed = 0f;
        if (engaged) Enter(State.Engage);
        else if (stateTimer <= 0f) Enter(State.Patrol, 4f);
    }

    void UpdateEngage(bool swingSeen)
    {
        if (!engaged) { Enter(State.Pause, Random.Range(0.5f, 1.5f)); return; }
        facing = DirToPlayer;

        if (swingSeen && !BeingComboed && Dist < attackRange + 0.5f && Random.value < dodgeChance)
        {
            Enter(State.Retreat, 0.35f);
            return;
        }

        bool grounded = Grounded();
        if (target.transform.position.y - transform.position.y > jumpHeightTrigger
            && Dist < 3.5f && grounded && Time.time >= nextJumpTime)
        {
            rb.velocity = new Vector3(rb.velocity.x, jumpSpeed, 0f);
            nextJumpTime = Time.time + jumpCooldown;
        }

        if (Dist <= attackRange && grounded && Time.time >= nextAttackTime)
        {
            if (target.IsBlocking && Random.value < waitOutBlockChance)
                Enter(State.Hover, Random.Range(0.3f, 0.8f));
            else
                Enter(State.Windup);
            return;
        }

        if (Time.time >= nextSpacingRoll)
        {
            preferredSpacing = Random.Range(spacingRange.x, spacingRange.y);
            nextSpacingRoll = Time.time + Random.Range(spacingRerollRange.x, spacingRerollRange.y);
        }

        float error = Dist - preferredSpacing;
        // Mid-combo it holds its ground rather than backing out to its preferred spacing.
        if (Mathf.Abs(error) < 0.15f || (error < 0f && BeingComboed)) desiredSpeed = 0f;
        else if (error > 0f) desiredSpeed = DirToPlayer * chaseSpeed;
        else desiredSpeed = -DirToPlayer * retreatSpeed * 0.6f;
    }

    void UpdateRetreat()
    {
        facing = DirToPlayer;
        desiredSpeed = -DirToPlayer * retreatSpeed;
        if (stateTimer <= 0f) Enter(State.Engage);
    }

    void UpdateHover(bool swingSeen)
    {
        facing = DirToPlayer;
        if (swingSeen && !BeingComboed && Dist < attackRange + 0.5f && Random.value < dodgeChance)
            Enter(State.Retreat, 0.35f);
        else if (stateTimer <= 0f)
            Enter(State.Engage);
    }

    void UpdateWindup()
    {
        desiredSpeed = 0f;
        windupElapsed += Time.deltaTime;
        if (stateTimer <= 0f) Enter(State.Lunge);
    }

    void UpdateHurt()
    {
        desiredSpeed = 0f;
        if (stateTimer > 0f) return;

        if (!BeingComboed && (hitStreak >= 2 || Random.value < retreatAfterHitChance))
        {
            Enter(State.Retreat, Random.Range(0.35f, 0.6f));
        }
        else
        {
            nextAttackTime = Time.time; // retaliate right away
            Enter(State.Engage);
        }
    }

    /// Moves the strike hitbox in front of the enemy, on the lunge side, at strikeBox size.
    void PlaceStrikeHitbox()
    {
        Transform t = strikeHitbox.transform;
        t.SetPositionAndRotation(StrikeCenter, Quaternion.identity);
        if (strikeHitbox.TryGetComponent(out BoxCollider box))
        {
            Vector3 s = t.lossyScale;
            box.center = Vector3.zero;
            box.size = new Vector3(strikeBox.x / Mathf.Abs(s.x), strikeBox.y / Mathf.Abs(s.y), strikeBox.z / Mathf.Abs(s.z));
        }
    }

    void OnStrikeResolved(ParryResult? result)
    {
        if (state == State.Lunge) EndLunge(result);
    }

    /// The hitbox has already dealt damage or pushed the player for a hit or a block.
    void EndLunge(ParryResult? result)
    {
        moveX = 0f;
        desiredSpeed = 0f;
        nextAttackTime = Time.time + Random.Range(attackCooldownRange.x, attackCooldownRange.y);
        preferredSpacing = Random.Range(spacingRange.x, spacingRange.y);

        switch (result)
        {
            case ParryResult.Parry:
                push = -lungeDir * staggerSpeed;
                Enter(State.Stunned, stunTime);
                return;
            case ParryResult.Blocked:
                push = -lungeDir * 2f;
                break;
        }
        Enter(State.Recover, recoverTime);
    }

    /// Hit recently by a player who's still swinging: no backing off until the combo ends.
    bool BeingComboed => Time.time - lastHitTakenTime < comboLockTime && targetAttack != null && targetAttack.IsAttacking;

    public void TakeHit(Vector3 direction, float force = 1f)
    {
        lastHitTakenTime = Time.time;
        bool staggered = IsStunned;
        flashUntil = Time.time + hitFlashTime;
        combat.ReportDamaged(staggered);

        push = Mathf.Sign(direction.x) * (staggered ? counterPushSpeed : hitPushSpeed) * force;
        // Staggered stays staggered.
        if (staggered) return;

        // Only a parry stops an attack: mid wind-up or lunge, a hit just pauses it.
        if (state == State.Windup || state == State.Lunge)
        {
            flinchUntil = Time.time + hitStunTime * force;
            return;
        }
        hitStreak = Time.time - lastHurtTime < 1f ? hitStreak + 1 : 1;
        lastHurtTime = Time.time;
        Enter(State.Hurt, hitStunTime * force);
    }

    // ---------------------------------------------------------------- senses

    float X => transform.position.x;
    float Dist => Mathf.Abs(target.transform.position.x - X);
    int DirToPlayer => target.transform.position.x >= X ? 1 : -1;
    bool PlayerAlive => targetHealth == null || !targetHealth.IsDead;

    bool CanSeePlayer()
    {
        if (!PlayerAlive || Dist > detectRange) return false;
        Vector3 eye = transform.position + Vector3.up * (halfHeight * 0.5f);
        Vector3 toPlayer = target.transform.position - eye;
        if (Physics.Raycast(eye, toPlayer.normalized, out RaycastHit hit, toPlayer.magnitude, obstacleMask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<ParryController>() == target;
        return true;
    }

    bool Grounded() =>
        Physics.Raycast(transform.position, Vector3.down, halfHeight + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore);

    bool GroundAhead(int dir)
    {
        Vector3 probe = transform.position + Vector3.right * (dir * (halfWidth + 0.3f));
        return Physics.Raycast(probe, Vector3.down, halfHeight + 0.6f, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    bool PathBlocked(int dir)
    {
        if (!GroundAhead(dir)) return true;
        return Physics.Raycast(transform.position, Vector3.right * dir, halfWidth + 0.3f, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    Color StateColor()
    {
        switch (state)
        {
            // Ramp over the normal wind-up, then hold: a delayed attack stays yellow longer.
            case State.Windup:
                // Blink in the last moment before the lunge; until then ramp (and hold, for a bait).
                if (Warning) return Mathf.Repeat(Time.time, 0.1f) < 0.05f ? warnColor : windupColor;
                return Color.Lerp(idleColor, windupColor, windupElapsed / windupRamp);
            case State.Lunge: return strikeColor;
            case State.Stunned: return stunColor;
            default: return idleColor;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position + Vector3.right * (facing * (halfWidth + strikeBox.x * 0.5f)), strikeBox);
    }
}
