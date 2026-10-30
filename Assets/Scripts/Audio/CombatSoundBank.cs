using UnityEngine;

/// Every game sound, grouped by what triggers it. Filled in automatically from the
/// folders under Assets/Audio (see Assets/Audio/README.md) by AudioFolderImporter,
/// so don't edit it by hand: add or remove files in the folders instead.
public class CombatSoundBank : ScriptableObject
{
    [Header("SFX (Assets/Audio/SFX/...)")]
    public AudioClip[] swing = new AudioClip[0];
    public AudioClip[] hit = new AudioClip[0];
    public AudioClip[] finisherHit = new AudioClip[0];
    public AudioClip[] parry = new AudioClip[0];
    public AudioClip[] attackParry = new AudioClip[0];
    public AudioClip[] block = new AudioClip[0];
    public AudioClip[] hurt = new AudioClip[0];
    public AudioClip[] whiff = new AudioClip[0];

    [Header("Smite (Assets/Audio/SFX/...)")]
    public AudioClip[] typeCorrect = new AudioClip[0];
    public AudioClip[] typeError = new AudioClip[0];
    public AudioClip[] sentenceComplete = new AudioClip[0];
    public AudioClip[] smiteCharged = new AudioClip[0];
    public AudioClip[] smiteReadyLoop = new AudioClip[0];
    public AudioClip[] typeFail = new AudioClip[0];
    public AudioClip[] smitePerfect = new AudioClip[0];

    [Header("Music (Assets/Audio/Music)")]
    public AudioClip[] music = new AudioClip[0];
}
