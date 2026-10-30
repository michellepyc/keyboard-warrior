using UnityEngine;

/// Loops a music track from the sound bank (Assets/Audio/Music), fading it in.
/// Plays nothing if there's no music yet.
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    public CombatSoundBank sounds;
    [Tooltip("Which track in Assets/Audio/Music to play, in file-name order (0 = first).")]
    public int track;
    [Range(0f, 1f)] public float volume = 0.6f;
    [Tooltip("Seconds to fade the music in at the start.")]
    public float fadeInTime = 1.5f;

    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
    }

    void Start()
    {
        if (sounds == null || sounds.music.Length == 0) return;
        source.clip = sounds.music[Mathf.Clamp(track, 0, sounds.music.Length - 1)];
        source.volume = fadeInTime > 0f ? 0f : volume;
        source.Play();
    }

    void Update()
    {
        // Unscaled, so hit-stop doesn't stall the fade.
        if (source.isPlaying && source.volume < volume)
            source.volume = Mathf.MoveTowards(source.volume, volume, volume / Mathf.Max(fadeInTime, 0.01f) * Time.unscaledDeltaTime);
        else if (source.volume > volume)
            source.volume = volume;
    }
}
