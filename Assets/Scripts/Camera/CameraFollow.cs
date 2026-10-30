using UnityEngine;

/// Side-scroller follow cam. Put this on a rig object and parent the camera
/// under it, so screen shake can offset the camera locally.
/// - Pans ahead of where the player is walking.
/// - Vertically, follows the ground the player stands on and ignores small jumps;
///   it only rises once a jump goes well above where it started, and drops
///   quickly when the player falls below it.
/// - With an enemy in lock-on range, it swings round slightly behind the player
///   (over the shoulder), frames the enemy too, and pulls back as the gap grows.
/// - While typing a Smite it moves in closer and further over the shoulder, then
///   eases back out. That move runs on real time, so the slow motion doesn't drag it.
/// - Punch(metres) gives a quick zoom-in that eases back out (e.g. when a hit lands);
///   it shows during hit-stop too.
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 1.2f, -9f);
    [Tooltip("Shifts the view this far ahead of where the player is facing.")]
    public float lookAhead = 3.5f;
    [Tooltip("Seconds of continuous walking to pan the view all the way ahead. A quick tap only pans it a little.")]
    public float lookAheadBuildTime = 0.8f;
    [Tooltip("Extra easing on top of the pan, in seconds. Higher feels floatier.")]
    public float lookAheadSmoothTime = 1.35f;
    [Tooltip("Below this horizontal speed the player counts as standing still, and the look-ahead holds where it is.")]
    public float movingThreshold = 0.5f;
    public float smoothTime = 0.15f;
    [Tooltip("Slower vertical follow so height changes don't jerk the camera.")]
    public float verticalSmoothTime = 0.3f;

    [Header("Jumps and falls")]
    [Tooltip("In the air the camera holds its height until the player rises this far above the ground they left.")]
    public float jumpBand = 2f;
    [Tooltip("Vertical follow speed while the player falls below the ground they left (off a ledge), in seconds.")]
    public float fallSmoothTime = 0.12f;

    [Header("Lock-on framing")]
    public bool lockOnFraming = true;
    [Tooltip("Degrees the camera swings round behind the player toward a locked-on enemy.")]
    public float lockYaw = 7f;
    [Tooltip("How far the framing shifts toward the enemy: 0 keeps the player centred, 0.5 centres between them.")]
    [Range(0f, 1f)] public float lockFrameBias = 0.35f;
    [Tooltip("The camera pulls back once the enemy is further than this...")]
    public float lockZoomStart = 2.5f;
    [Tooltip("...by this much per extra metre...")]
    public float lockZoomPerMeter = 0.4f;
    [Tooltip("...up to this much in total.")]
    public float lockZoomMax = 2f;
    [Tooltip("How quickly the camera eases into and out of lock-on framing, in seconds.")]
    public float lockBlendTime = 0.35f;

    [Header("Typing (Smite)")]
    [Tooltip("Extra degrees the camera swings round behind the player while typing, toward the enemy (or the way the player faces).")]
    public float typingYaw = 15f;
    [Tooltip("How much closer (metres) the camera moves while typing.")]
    public float typingZoomIn = 1.5f;
    [Tooltip("Camera height change while typing (metres). Raised a little, with Typing Pitch, so the sentence above the player stays in frame up close.")]
    public float typingHeight = 0.6f;
    [Tooltip("Degrees the camera tilts down while typing.")]
    public float typingPitch = 6f;
    [Tooltip("Real seconds to ease into and out of the typing view.")]
    public float typingBlendTime = 0.3f;

    [Header("Zoom punch")]
    [Tooltip("Real seconds for a zoom punch to push in...")]
    public float punchInTime = 0.05f;
    [Tooltip("...and to ease back out.")]
    public float punchOutTime = 0.3f;

    PlayerMovement movement;
    PlayerTargeting targeting;
    PlayerStateMachine states;
    float typingWeight, typingVel;
    float punchSize, punchFrom, punchAt = float.NegativeInfinity;
    float velX, velY;
    float aheadGoal, currentAhead, aheadVel;
    float lastTargetX;
    float focusX, focusY;
    float groundY;
    float lockWeight, lockWeightVel;
    float yaw, yawVel;
    float zoom, zoomVel;
    float lockSide = 1f;

    void Awake()
    {
        if (target == null)
        {
            PlayerMovement player = FindObjectOfType<PlayerMovement>();
            if (player != null) target = player.transform;
        }
        if (target != null)
        {
            movement = target.GetComponent<PlayerMovement>();
            targeting = target.GetComponent<PlayerTargeting>();
            states = target.GetComponent<PlayerStateMachine>();
        }
    }

    /// How far into the typing view the camera is (0..1).
    public float TypingBlend => typingWeight;
    /// The zoom punch right now (metres pushed in).
    public float PunchNow
    {
        get
        {
            float t = Time.unscaledTime - punchAt;
            if (t < 0f) return 0f;
            if (t < punchInTime) return Mathf.Lerp(punchFrom, punchSize, t / punchInTime);
            float o = punchOutTime > 0f ? Mathf.Clamp01((t - punchInTime) / punchOutTime) : 1f;
            return punchSize * (1f - o * o * (3f - 2f * o));
        }
    }

    /// A quick zoom-in by this many metres that eases back out. A new punch during
    /// one continues from where that one is, so rapid hits don't jitter.
    public void Punch(float metres)
    {
        if (metres <= 0f) return;
        punchFrom = PunchNow;
        punchSize = Mathf.Max(metres, punchFrom);
        punchAt = Time.unscaledTime;
    }

    void Start()
    {
        if (target == null) return;
        aheadGoal = currentAhead = movement != null ? movement.Facing * lookAhead : 0f;
        lastTargetX = target.position.x;
        focusX = target.position.x + currentAhead;
        focusY = groundY = target.position.y;
        Place();
    }

    void LateUpdate()
    {
        if (target == null) return;
        // The typing view and zoom punches run on real time, even through hit-stop.
        bool typingNow = states != null && states.State == PlayerState.Typing;
        typingWeight = Mathf.SmoothDamp(typingWeight, typingNow ? 1f : 0f, ref typingVel, typingBlendTime, Mathf.Infinity, Time.unscaledDeltaTime);

        float dt = Time.deltaTime;
        if (dt > 0f) Follow(dt); // frozen during hit-stop
        Place();
    }

    void Follow(float dt)
    {
        // Lock-on: ease toward framing the closest enemy in range.
        CombatTarget enemy = lockOnFraming && targeting != null ? targeting.Closest() : null;
        if (enemy != null)
        {
            float dx = enemy.transform.position.x - target.position.x;
            if (Mathf.Abs(dx) > 0.3f) lockSide = Mathf.Sign(dx);
        }
        lockWeight = Mathf.SmoothDamp(lockWeight, enemy != null ? 1f : 0f, ref lockWeightVel, lockBlendTime);
        float gap = enemy != null ? Mathf.Abs(enemy.transform.position.x - target.position.x) : 0f;
        float zoomGoal = enemy != null ? Mathf.Clamp((gap - lockZoomStart) * lockZoomPerMeter, 0f, lockZoomMax) : 0f;
        zoom = Mathf.SmoothDamp(zoom, zoomGoal, ref zoomVel, lockBlendTime);
        yaw = Mathf.SmoothDampAngle(yaw, enemy != null ? lockSide * lockYaw : 0f, ref yawVel, lockBlendTime);

        // Pan only while walking, at a rate that takes lookAheadBuildTime to cross
        // the full distance: holding a direction pans all the way, a tap barely moves it.
        if (movement != null && movement.HorizontalSpeed > movingThreshold)
            aheadGoal = Mathf.MoveTowards(aheadGoal, movement.Facing * lookAhead, lookAhead / lookAheadBuildTime * dt);
        currentAhead = Mathf.SmoothDamp(currentAhead, aheadGoal, ref aheadVel, lookAheadSmoothTime);

        // SmoothDamp trails a moving target by about velocity * smoothTime, which
        // would eat the look-ahead while running; lead by that much to cancel it.
        float targetVelX = (target.position.x - lastTargetX) / dt;
        lastTargetX = target.position.x;

        // While locked on, frame toward the enemy instead of looking ahead.
        float lockShift = enemy != null ? lockFrameBias * (enemy.transform.position.x - target.position.x) : 0f;
        float goalX = target.position.x + Mathf.Lerp(currentAhead, lockShift, lockWeight) + targetVelX * smoothTime;
        focusX = Mathf.SmoothDamp(focusX, goalX, ref velX, smoothTime);

        // Height: track the ground; in the air only follow big jumps and falls.
        float y = target.position.y;
        // Not rising: the ground check has a short grace period after takeoff.
        bool grounded = movement == null || (movement.IsGrounded && movement.VerticalSpeed <= 0.1f);
        float goalY = y;
        float ySmooth = verticalSmoothTime;
        if (grounded) groundY = y;
        else if (y < groundY) ySmooth = fallSmoothTime;
        else goalY = Mathf.Max(groundY, y - jumpBand);
        focusY = Mathf.SmoothDamp(focusY, goalY, ref velY, ySmooth);
    }

    void Place()
    {
        // While typing, swing further round toward the enemy (or the facing side).
        float side = targeting != null && targeting.Closest() != null ? lockSide : (movement != null ? movement.Facing : 1f);
        Quaternion turn = Quaternion.Euler(0f, yaw + side * typingYaw * typingWeight, 0f);
        float distance = zoom - typingZoomIn * typingWeight - PunchNow;
        Vector3 pulledBack = offset + new Vector3(0f, typingHeight * typingWeight, -distance);
        transform.position = new Vector3(focusX, focusY, 0f) + turn * pulledBack;
        transform.rotation = turn * Quaternion.Euler(typingPitch * typingWeight, 0f, 0f);
    }
}
