using System.Collections;
using UnityEngine;

/// Game-feel layer: hit-stop and camera shake when an attack resolves,
/// the player takes damage, or the player lands a hit, which also punches the
/// camera in a little (CameraFollow.Punch).
public class ParryFeedback : MonoBehaviour
{
    public ParryController player;
    public PlayerAttack playerAttack;
    public PlayerHealth playerHealth;
    [Tooltip("The camera itself (a child of the follow rig), so shake is a local offset.")]
    public Transform cameraTransform;

    [Header("Hit-stop (real seconds)")]
    public float parryHitStop = 0.08f;
    [Tooltip("A parry caught on the sword or shield (attack key) gets a longer freeze, for a clash.")]
    public float attackParryHitStop = 0.14f;
    public float blockHitStop = 0.03f;
    public float hitHitStop = 0.04f;
    public float attackHitStop = 0.04f;
    public float finisherHitStop = 0.09f;

    [Header("Camera shake")]
    public float parryShake = 0.22f;
    public float attackParryShake = 0.32f;
    public float blockShake = 0.04f;
    public float hitShake = 0.2f;
    public float attackShake = 0.12f;
    public float finisherShake = 0.22f;
    public float shakeDuration = 0.15f;

    [Header("Zoom punch (metres the camera pushes in)")]
    [Tooltip("When one of your hits lands.")]
    public float attackZoom = 0.25f;
    [Tooltip("When a combo finisher lands.")]
    public float finisherZoom = 0.5f;
    [Tooltip("The camera rig that does the punch (found automatically).")]
    public CameraFollow cameraFollow;

    Vector3 cameraHome;
    Coroutine hitStopRoutine;
    Coroutine shakeRoutine;

    void Awake()
    {
        if (player == null) player = FindObjectOfType<ParryController>();
        if (playerAttack == null && player != null) playerAttack = player.GetComponent<PlayerAttack>();
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        if (cameraTransform != null) cameraHome = cameraTransform.localPosition;
        if (cameraFollow == null) cameraFollow = FindObjectOfType<CameraFollow>();
    }

    void OnEnable()
    {
        player.AttackResolved += OnAttackResolved;
        if (playerAttack != null) playerAttack.HitLanded += OnHitLanded;
        if (playerHealth != null) playerHealth.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        player.AttackResolved -= OnAttackResolved;
        if (playerAttack != null) playerAttack.HitLanded -= OnHitLanded;
        if (playerHealth != null) playerHealth.Damaged -= OnDamaged;
        GameTime.EndHitStop(this);
        if (cameraTransform != null) cameraTransform.localPosition = cameraHome;
    }

    void OnAttackResolved(ParryResult result, float sincePress)
    {
        switch (result)
        {
            case ParryResult.Parry:
                if (player.LastParryWasAttack) Play(attackParryHitStop, attackParryShake);
                else Play(parryHitStop, parryShake);
                break;
            case ParryResult.Blocked: Play(blockHitStop, blockShake); break;
            // Hits are handled by OnDamaged so contact damage gets the same feel.
        }
    }

    void OnDamaged(int amount) => Play(hitHitStop, hitShake);

    void OnHitLanded(IHittable target)
    {
        bool finisher = playerAttack.IsFinisher;
        if (finisher) Play(finisherHitStop, finisherShake);
        else Play(attackHitStop, attackShake);
        if (cameraFollow != null) cameraFollow.Punch(finisher ? finisherZoom : attackZoom);
    }

    /// A camera shake on its own, without hit-stop (for other effects).
    public void ShakeCamera(float strength)
    {
        if (cameraTransform == null) return;
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(Shake(strength));
    }

    void Play(float hitStop, float shake)
    {
        if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
        hitStopRoutine = StartCoroutine(HitStop(hitStop));

        if (cameraTransform == null) return;
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(Shake(shake));
    }

    // Through GameTime, so the game comes back to any slow motion rather than to 1x.
    IEnumerator HitStop(float duration)
    {
        GameTime.BeginHitStop(this);
        yield return new WaitForSecondsRealtime(duration);
        GameTime.EndHitStop(this);
    }

    IEnumerator Shake(float strength)
    {
        for (float t = 0f; t < shakeDuration; t += Time.unscaledDeltaTime)
        {
            float falloff = 1f - t / shakeDuration;
            cameraTransform.localPosition = cameraHome + (Vector3)Random.insideUnitCircle * (strength * falloff);
            yield return null;
        }
        cameraTransform.localPosition = cameraHome;
    }
}
