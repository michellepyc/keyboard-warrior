using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Watches Assets/Audio. Sound files dropped into its folders get import settings
/// suited to their kind on first import, and the CombatSoundBank is rebuilt from
/// the folder contents whenever anything there is added, removed or moved, so new
/// sounds play in game with no other setup. See Assets/Audio/README.md.
public class AudioFolderImporter : AssetPostprocessor
{
    const string AudioRoot = "Assets/Audio";
    const string BankPath = AudioRoot + "/CombatSoundBank.asset";
    const string SfxRoot = AudioRoot + "/SFX/";
    const string MusicRoot = AudioRoot + "/Music";

    /// Folder under Assets/Audio/SFX for each bank slot.
    static readonly string[] SfxSlots = { "Swing", "Hit", "FinisherHit", "Parry", "AttackParry", "Block", "Hurt", "Whiff",
        "TypeCorrect", "TypeError", "SentenceComplete", "SmiteCharged", "SmiteReadyLoop", "TypeFail", "SmitePerfect" };

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(AudioRoot + "/") || !assetImporter.importSettingsMissing) return;
        var importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        if (assetPath.StartsWith(MusicRoot + "/"))
        {
            // Long tracks: stream from disk instead of holding them decoded in memory.
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.loadInBackground = true;
        }
        else
        {
            // Short effects: fully loaded so they play the instant they're triggered.
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
        }
        importer.defaultSampleSettings = settings;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        // Rebuild right away: the clips are imported by now. (A delayed call can sit
        // unrun while the editor is in the background, e.g. when files come from Finder.)
        bool touchesAudio = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
            .Any(p => p.StartsWith(AudioRoot + "/") && p != BankPath);
        if (touchesAudio) RebuildBank();
    }

    [MenuItem("Tools/Audio/Rebuild Sound Bank")]
    public static void RebuildBank()
    {
        var bank = AssetDatabase.LoadAssetAtPath<CombatSoundBank>(BankPath);
        if (bank == null)
        {
            if (!AssetDatabase.IsValidFolder(AudioRoot)) AssetDatabase.CreateFolder("Assets", "Audio");
            bank = ScriptableObject.CreateInstance<CombatSoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }

        bank.swing = ClipsIn(SfxRoot + "Swing");
        bank.hit = ClipsIn(SfxRoot + "Hit");
        bank.finisherHit = ClipsIn(SfxRoot + "FinisherHit");
        bank.parry = ClipsIn(SfxRoot + "Parry");
        bank.attackParry = ClipsIn(SfxRoot + "AttackParry");
        bank.block = ClipsIn(SfxRoot + "Block");
        bank.hurt = ClipsIn(SfxRoot + "Hurt");
        bank.whiff = ClipsIn(SfxRoot + "Whiff");
        bank.typeCorrect = ClipsIn(SfxRoot + "TypeCorrect");
        bank.typeError = ClipsIn(SfxRoot + "TypeError");
        bank.sentenceComplete = ClipsIn(SfxRoot + "SentenceComplete");
        bank.smiteCharged = ClipsIn(SfxRoot + "SmiteCharged");
        bank.smiteReadyLoop = ClipsIn(SfxRoot + "SmiteReadyLoop");
        bank.typeFail = ClipsIn(SfxRoot + "TypeFail");
        bank.smitePerfect = ClipsIn(SfxRoot + "SmitePerfect");
        bank.music = ClipsIn(MusicRoot);

        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();

        var counts = SfxSlots.Select(s => $"{s} {ClipsIn(SfxRoot + s).Length}").ToList();
        counts.Add($"Music {bank.music.Length}");
        Debug.Log("[AudioFolderImporter] Sound bank updated: " + string.Join(", ", counts));
    }

    /// Clips directly in a folder (and its subfolders), in name order.
    static AudioClip[] ClipsIn(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return new AudioClip[0];
        var clips = new List<AudioClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null) clips.Add(clip);
        }
        return clips.ToArray();
    }
}
