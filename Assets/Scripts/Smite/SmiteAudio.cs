using UnityEngine;

/// The Smite's sounds, from the CombatSoundBank (folders under Assets/Audio/SFX, see
/// Assets/Audio/README.md). Empty folders stay silent.
/// - TypeCorrect: a soft tick per correct letter, rising in pitch as the sentence
///   fills, so finishing it builds up.
/// - TypeError: a low thud on a wrong key.
/// - SentenceComplete: as the last letter is typed (the build-up into the charge).
/// - SmiteCharged: when the letters reach the sword.
/// - SmiteReadyLoop: a quiet loop while the Smite is ready, faded out when it ends.
/// - TypeFail: typing failed (time ran out, cancelled, hit).
/// - SmitePerfect: on top of SmiteCharged when the sentence had no mistakes.
/// Kept quiet by default: typing makes a lot of sounds in a row.
[RequireComponent(typeof(AudioSource))]
public class SmiteAudio : MonoBehaviour
{
    public TypingController typing;
    public TypingDisplay display;
    public PlayerStateMachine states;
    [Tooltip("Assets/Audio/CombatSoundBank. Rebuilt automatically when sound files change.")]
    public CombatSoundBank sounds;

    [Header("Volume")]
    [Range(0f, 1f)] public float typeVolume = 0.35f;
    [Range(0f, 1f)] public float errorVolume = 0.5f;
    [Range(0f, 1f)] public float completeVolume = 0.8f;
    [Range(0f, 1f)] public float chargedVolume = 0.8f;
    [Range(0f, 1f)] public float readyLoopVolume = 0.3f;
    [Range(0f, 1f)] public float failVolume = 0.7f;
    [Range(0f, 1f)] public float perfectVolume = 0.8f;

    [Header("Pitch")]
    [Tooltip("Correct-letter ticks rise by this much from the first letter to the last.")]
    [Range(0f, 1f)] public float typePitchRise = 0.25f;
    [Tooltip("Wrong-key sounds play this much lower.")]
    [Range(0f, 0.5f)] public float errorPitchDrop = 0.1f;
    [Range(0f, 0.2f)] public float pitchVariation = 0.03f;
    [Tooltip("Seconds to fade the ready loop out.")]
    public float loopFadeTime = 0.3f;

    /// The pitch the last sound was played at (for tuning and tests).
    public float LastPitch { get; private set; }

    AudioSource source;
    AudioSource loop;
    bool loopWanted;
    bool completed, perfect;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        loop = gameObject.AddComponent<AudioSource>();
        loop.playOnAwake = false;
        loop.loop = true;
        loop.spatialBlend = 0f;
        loop.volume = 0f;
        if (typing == null) typing = FindObjectOfType<TypingController>();
        if (display == null) display = FindObjectOfType<TypingDisplay>();
        if (states == null && typing != null) states = typing.GetComponent<PlayerStateMachine>();
    }

    void OnEnable()
    {
        if (typing != null)
        {
            typing.SessionStarted += OnStarted;
            typing.CorrectCharacter += OnCorrect;
            typing.WrongCharacter += OnWrong;
            typing.SentenceCompleted += OnCompleted;
            typing.SessionEnded += OnEnded;
        }
        if (display != null) display.LettersArrived += OnCharged;
        if (states != null) states.Changed += OnStateChanged;
    }

    void OnDisable()
    {
        if (typing != null)
        {
            typing.SessionStarted -= OnStarted;
            typing.CorrectCharacter -= OnCorrect;
            typing.WrongCharacter -= OnWrong;
            typing.SentenceCompleted -= OnCompleted;
            typing.SessionEnded -= OnEnded;
        }
        if (display != null) display.LettersArrived -= OnCharged;
        if (states != null) states.Changed -= OnStateChanged;
        loopWanted = false;
        loop.Stop();
    }

    void OnCorrect(char c, int index)
    {
        float progress = typing.Session != null ? typing.Session.Progress : 0f;
        Play(sounds?.typeCorrect, typeVolume, 1f + typePitchRise * progress);
    }

    void OnWrong(char expected, char received) => Play(sounds?.typeError, errorVolume, 1f - errorPitchDrop);

    void OnStarted(TypingSession session) => completed = perfect = false;

    void OnCompleted()
    {
        completed = true;
        perfect = typing.Session != null && typing.Session.Mistakes == 0;
        Play(sounds?.sentenceComplete, completeVolume, 1f);
    }

    // Ended without being finished: failed.
    void OnEnded()
    {
        if (!completed) Play(sounds?.typeFail, failVolume, 1f);
    }

    void OnCharged(Vector3 at)
    {
        Play(sounds?.smiteCharged, chargedVolume, 1f);
        if (perfect) Play(sounds?.smitePerfect, perfectVolume, 1f);
        if (states == null || states.State == PlayerState.SmiteReady) StartLoop();
    }

    void OnStateChanged(PlayerState from, PlayerState to)
    {
        if (to != PlayerState.SmiteReady) loopWanted = false;
    }

    void StartLoop()
    {
        AudioClip[] clips = sounds?.smiteReadyLoop;
        if (clips == null || clips.Length == 0 || clips[0] == null) return;
        loop.clip = clips[0];
        loop.volume = readyLoopVolume;
        loop.Play();
        loopWanted = true;
    }

    void Update()
    {
        if (loopWanted || !loop.isPlaying) return;
        loop.volume = Mathf.MoveTowards(loop.volume, 0f, readyLoopVolume / Mathf.Max(0.01f, loopFadeTime) * Time.unscaledDeltaTime);
        if (loop.volume <= 0f) loop.Stop();
    }

    void Play(AudioClip[] clips, float volume, float pitch)
    {
        LastPitch = pitch;
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        source.pitch = pitch + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(clip, volume);
    }
}
