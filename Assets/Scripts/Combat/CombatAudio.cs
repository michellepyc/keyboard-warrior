using UnityEngine;

/// Plays combat sounds from the CombatSoundBank, which is filled automatically from
/// the folders under Assets/Audio/SFX (see Assets/Audio/README.md). Each event
/// plays a random clip from its folder with a little pitch variation. Empty
/// folders stay silent.
[RequireComponent(typeof(AudioSource))]
public class CombatAudio : MonoBehaviour
{
    public PlayerAttack attack;
    public ParryController defense;
    public PlayerHealth health;

    [Tooltip("Assets/Audio/CombatSoundBank. Rebuilt automatically when sound files change.")]
    public CombatSoundBank sounds;

    [Header("Playback")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Each play picks a pitch within this much of 1.")]
    [Range(0f, 0.5f)] public float pitchVariation = 0.05f;

    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        if (attack == null) attack = FindObjectOfType<PlayerAttack>();
        if (defense == null && attack != null) defense = attack.GetComponent<ParryController>();
        if (health == null && attack != null) health = attack.GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        if (attack != null)
        {
            attack.SwingStarted += OnSwingStarted;
            attack.HitLanded += OnHitLanded;
        }
        if (defense != null)
        {
            defense.AttackResolved += OnAttackResolved;
            defense.Whiffed += OnWhiffed;
        }
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (attack != null)
        {
            attack.SwingStarted -= OnSwingStarted;
            attack.HitLanded -= OnHitLanded;
        }
        if (defense != null)
        {
            defense.AttackResolved -= OnAttackResolved;
            defense.Whiffed -= OnWhiffed;
        }
        if (health != null) health.Damaged -= OnDamaged;
    }

    void OnSwingStarted(int step) => Play(sounds?.swing);

    // The finisher falls back to the normal hit sounds while its folder is empty.
    void OnHitLanded(IHittable target) =>
        Play(attack.IsFinisher && Has(sounds?.finisherHit) ? sounds.finisherHit : sounds?.hit);

    void OnAttackResolved(ParryResult result, float sinceInput)
    {
        // A sword/shield parry falls back to the normal parry sounds while its folder is empty.
        if (result == ParryResult.Parry)
            Play(defense.LastParryWasAttack && Has(sounds?.attackParry) ? sounds.attackParry : sounds?.parry);
        else if (result == ParryResult.Blocked) Play(sounds?.block);
        // A hit that lands plays through OnDamaged, so contact damage sounds the same.
    }

    void OnWhiffed() => Play(sounds?.whiff);

    void OnDamaged(int amount) => Play(sounds?.hurt);

    static bool Has(AudioClip[] clips) => clips != null && clips.Length > 0;

    /// Plays a random clip from the slot. Returns false if the slot is empty.
    public bool Play(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return false;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return false;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(clip, volume);
        return true;
    }
}
