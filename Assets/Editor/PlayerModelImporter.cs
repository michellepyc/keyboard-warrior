using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// Applies Blender-FBX + Humanoid import settings to files dropped into the
/// player's art folders, and Blender-FBX prop settings (no rig) to files in
/// Equipment/ (the sword and shield). Only runs on a file's first import (no .meta yet),
/// so anything you change in the Inspector afterward sticks.
///
/// Animation files also get their clips renamed from their path, because
/// Mixamo names every clip "mixamo.com":
///   Animations/Block/Crouch_Impact.fbx  ->  clip "Block_Crouch_Impact"
/// See Animations/ANIMATIONS.md for the folder layout and naming rules.
public class PlayerModelImporter : AssetPostprocessor
{
    const string ModelFolder = "Assets/Art/Characters/Player/Model/";
    const string AnimationFolder = "Assets/Art/Characters/Player/Animations/";
    const string EquipmentFolder = "Assets/Art/Characters/Player/Equipment/";

    // Clips whose names contain any of these loop, unless they're a transition (contain "_To_").
    static readonly string[] LoopingKeywords = { "idle", "walk", "run", "loop" };

    bool IsPlayerModel => assetPath.StartsWith(ModelFolder);
    bool IsPlayerAnimation => assetPath.StartsWith(AnimationFolder);
    bool IsEquipment => assetPath.StartsWith(EquipmentFolder);
    bool ShouldConfigure => (IsPlayerModel || IsPlayerAnimation) && assetImporter.importSettingsMissing;

    void OnPreprocessModel()
    {
        if (IsEquipment && assetImporter.importSettingsMissing)
        {
            ConfigureEquipment((ModelImporter)assetImporter);
            return;
        }
        if (!ShouldConfigure) return;
        var importer = (ModelImporter)assetImporter;

        // Blender is Z-up; bake the conversion so the model comes in upright
        // with clean transforms instead of a -90 X rotation on the root.
        importer.bakeAxisConversion = true;
        importer.useFileScale = true;
        importer.globalScale = 1f;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        // Animation-only files don't need their own materials.
        importer.materialImportMode = IsPlayerModel
            ? ModelImporterMaterialImportMode.ImportViaMaterialDescription
            : ModelImporterMaterialImportMode.None;

        Debug.Log($"[PlayerModelImporter] Applied Blender FBX + Humanoid settings to {assetPath}");
    }

    /// A sword or shield: a static prop held in a hand socket, so no rig or animation.
    void ConfigureEquipment(ModelImporter importer)
    {
        importer.bakeAxisConversion = true;
        importer.useFileScale = true;
        importer.globalScale = 1f;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        Debug.Log($"[PlayerModelImporter] Applied Blender FBX prop settings to {assetPath}");
    }

    void OnPreprocessAnimation()
    {
        if (!IsPlayerAnimation || !assetImporter.importSettingsMissing) return;
        var importer = (ModelImporter)assetImporter;
        importer.clipAnimations = NameAndLoopClips(importer.defaultClipAnimations, assetPath);
    }

    /// Re-derives clip names and looping for every file in the Animations folder,
    /// e.g. after renaming or moving files. Resets any trimming done in the Inspector.
    [MenuItem("Tools/Player Animations/Re-apply Clip Names and Looping")]
    static void ReapplyClipNames()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
            importer.clipAnimations = NameAndLoopClips(importer.defaultClipAnimations, path);
            importer.SaveAndReimport();
            count++;
        }
        Debug.Log($"[PlayerModelImporter] Re-applied clip names and looping to {count} animation file(s).");
    }

    static ModelImporterClipAnimation[] NameAndLoopClips(ModelImporterClipAnimation[] clips, string path)
    {
        // Blender exports with "All Actions" also carry the rig's leftover
        // single-frame pose actions; drop those when the file has a real clip.
        var real = new List<ModelImporterClipAnimation>();
        foreach (ModelImporterClipAnimation clip in clips)
            if (clip.lastFrame - clip.firstFrame > 1f) real.Add(clip);
        if (real.Count > 0 && real.Count < clips.Length)
        {
            Debug.Log($"[PlayerModelImporter] Dropped {clips.Length - real.Count} single-frame clip(s) from {path}");
            clips = real.ToArray();
        }
        else if (real.Count == 0)
        {
            Debug.LogWarning($"[PlayerModelImporter] {path} has no clips longer than one frame. Re-export it with the actual animation.");
        }

        string baseName = ClipNameFor(path);
        bool loops = IsLooping(baseName);
        // The game moves the player itself (root motion is off), so any hip motion left
        // as root motion is thrown away: the hips wouldn't turn, step or drop. Baking it
        // all into the pose plays the clip exactly as authored. The exception is height
        // for takeoff and falling clips, where the game already moves the player up and down.
        string lower = baseName.ToLowerInvariant();
        bool bakeHeight = !(lower.Contains("jump_start") || lower.Contains("fall"));
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = clips.Length == 1 ? baseName : $"{baseName}_{i + 1}";
            clips[i].loopTime = loops;
            clips[i].loopPose = loops;
            clips[i].lockRootRotation = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].lockRootHeightY = bakeHeight;
            clips[i].keepOriginalPositionY = true;
            clips[i].heightFromFeet = false;
        }
        return clips;
    }

    /// "Animations/Block/Crouch_Impact.fbx" -> "Block_Crouch_Impact".
    /// A file already prefixed with its folder isn't doubled up ("Crouch/Crouch_Idle" -> "Crouch_Idle").
    static string ClipNameFor(string path)
    {
        string relative = path.Substring(AnimationFolder.Length);
        var parts = new List<string>(Path.GetDirectoryName(relative).Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
        string file = Path.GetFileNameWithoutExtension(relative);

        if (parts.Count > 0 && file.StartsWith(parts[parts.Count - 1], StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);
        parts.Add(file);

        return Regex.Replace(string.Join("_", parts), @"[\s\-_]+", "_");
    }

    static bool IsLooping(string clipName)
    {
        string name = clipName.ToLowerInvariant();
        if (name.Contains("_to_")) return false;
        foreach (string keyword in LoopingKeywords)
            if (name.Contains(keyword)) return true;
        return false;
    }
}
