using System.IO;
using UnityEditor;
using UnityEngine;

/// Paints the wind-streak image the slash trail uses (Assets/Art/VFX/SlashWind.png)
/// and makes its material. Across the image (v) runs from the blade's inner edge
/// (bottom) to the tip's path (top); along it (u) is the swing, and it tiles that way.
/// It's white with the shape in alpha, so the trail's colour tints it. Paint over or
/// replace the PNG freely; re-running the menu overwrites it.
public static class SlashTextureGenerator
{
    const string Folder = "Assets/Art/VFX";
    const string TexturePath = Folder + "/SlashWind.png";
    const string MaterialPath = Folder + "/SlashTrail.mat";
    const int Width = 512, Height = 128;

    [MenuItem("Tools/VFX/Generate Slash Wind Texture")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art", "VFX");

        var alpha = new float[Width, Height];
        PaintSheet(alpha);
        PaintStreaks(alpha);

        var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var pixels = new Color[Width * Height];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            pixels[y * Width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha[x, y]));
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexturePath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
            material = new Material(shader);
            // Pale blue, and stronger than 1x (0.5) so it reads against the bright sky.
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.56f, 0.64f, 0.75f, 0.85f));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SlashTextureGenerator] Wrote {TexturePath} and {MaterialPath}");
    }

    /// A faint body that thickens toward the outer edge, with a soft bright leading
    /// edge just inside it, and a quick fade at the very edges.
    static void PaintSheet(float[,] alpha)
    {
        for (int y = 0; y < Height; y++)
        {
            float v = y / (Height - 1f);
            float body = Mathf.Pow(v, 2.4f) * 0.28f;
            float edge = Mathf.Exp(-Sq((v - 0.86f) / 0.045f)) * 0.32f;
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.93f, v)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, v));
            for (int x = 0; x < Width; x++)
            {
                // Gentle unevenness along the swing, so it reads as moving air (tileable).
                float u = x / (float)Width;
                float gust = 0.8f + 0.12f * Mathf.Sin(u * Mathf.PI * 2f * 3f + 1.3f) + 0.08f * Mathf.Sin(u * Mathf.PI * 2f * 7f + 4.1f);
                alpha[x, y] += (body + edge) * gust * rim;
            }
        }
    }

    /// Thin wind lines of varying length, brightness and thickness, bunched toward the
    /// outer edge, each tapering at both ends and broken up into gusts. They wrap around in u.
    static void PaintStreaks(float[,] alpha)
    {
        var rng = new System.Random(486);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        for (int s = 0; s < 70; s++)
        {
            float vc = 1f - Mathf.Pow(R(0f, 1f), 1.7f) * 0.9f;   // centre line, biased outward
            vc = Mathf.Min(vc, 0.95f);
            float thick = R(0.5f, 2.4f) * (0.6f + vc);           // pixels
            int length = (int)(R(0.12f, 0.65f) * Width);
            int start = rng.Next(Width);
            float strength = R(0.15f, 0.7f) * (0.4f + 0.4f * vc);
            float phase = R(0f, 10f), breaks = R(2f, 6f);
            float cy = vc * (Height - 1);
            for (int i = 0; i < length; i++)
            {
                float along = i / (float)length;
                float taper = Mathf.Pow(Mathf.Sin(along * Mathf.PI), 0.8f);
                float gust = Mathf.Clamp01(0.55f + 0.6f * Mathf.Sin(along * Mathf.PI * breaks + phase));
                float a = strength * taper * gust;
                if (a <= 0.001f) continue;
                int x = (start + i) % Width;
                // Streaks drift a little across as they go, like wind curling off the edge.
                float y0 = cy - along * thick * 1.5f;
                int reach = Mathf.CeilToInt(thick * 3f);
                for (int dy = -reach; dy <= reach; dy++)
                {
                    int y = Mathf.RoundToInt(y0) + dy;
                    if (y < 0 || y >= Height) continue;
                    alpha[x, y] += a * Mathf.Exp(-Sq((y - y0) / thick));
                }
            }
        }
    }

    static float Sq(float f) => f * f;
}
