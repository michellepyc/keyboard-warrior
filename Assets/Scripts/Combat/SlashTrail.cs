using System.Collections.Generic;
using UnityEngine;

/// A slash trail behind the sword during each swing's hit frames: a ribbon swept
/// between the tip and a point partway down the blade, textured with wind streaks
/// (Assets/Art/VFX/SlashWind.png, made by Tools > VFX > Generate Slash Wind Texture).
/// Like air the blade cut through, it keeps drifting along the swing and blows
/// outward as it fades, with its streaks flowing back toward the tail. It freezes
/// with the hit-stop.
public class SlashTrail : MonoBehaviour
{
    public PlayerAttack attack;
    [Tooltip("SlashTrail.mat: a particle-style (vertex colour, alpha blended) material with the wind texture.")]
    public Material material;
    public Color color = new Color(0.88f, 0.96f, 1f, 0.85f);
    [Tooltip("How much of the blade, from the tip, the trail covers.")]
    [Range(0.1f, 1f)] public float bladeCoverage = 0.65f;
    [Tooltip("Each part of the trail fades out over this long.")]
    public float lifetime = 0.3f;
    [Tooltip("How fast (m/s) the trail keeps travelling along the swing after the blade has passed.")]
    public float drift = 1.5f;
    [Tooltip("How fast (m/s) its outer edge blows outward as it fades.")]
    public float spread = 0.8f;
    [Tooltip("Metres of trail per repeat of the texture.")]
    public float streakLength = 1.4f;
    [Tooltip("How fast the streaks flow back toward the tail, in texture repeats per second.")]
    public float flowSpeed = 2.5f;
    [Tooltip("Points added between each pair of samples, so fast swings stay smooth.")]
    [Range(0, 8)] public int smoothing = 4;
    [Tooltip("The oldest part of each slash fades in over this fraction of its length.")]
    [Range(0f, 0.5f)] public float tailFade = 0.2f;

    struct Sample
    {
        public Vector3 tip, inner;
        public float time;
    }

    class Slash
    {
        public readonly List<Sample> samples = new List<Sample>();
    }

    const float MinStep = 0.01f;
    const int MaxSamples = 256;

    readonly List<Slash> slashes = new List<Slash>();
    Slash current;
    Mesh mesh;
    GameObject view;
    readonly List<Sample> smooth = new List<Sample>();
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<Color> colors = new List<Color>();
    readonly List<int> triangles = new List<int>();

    void Awake()
    {
        if (attack == null) attack = FindObjectOfType<PlayerAttack>();
        // Its own object at the world origin, since the mesh is built in world space.
        view = new GameObject("SlashTrail (runtime)") { hideFlags = HideFlags.DontSave };
        mesh = new Mesh { name = "SlashTrail" };
        mesh.MarkDynamic();
        view.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = view.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void OnEnable()
    {
        if (attack != null) attack.SwingStarted += OnSwingStarted;
        if (view != null) view.SetActive(true);
    }

    void OnDisable()
    {
        if (attack != null) attack.SwingStarted -= OnSwingStarted;
        if (view != null) view.SetActive(false);
    }

    void OnDestroy()
    {
        if (view != null) Destroy(view);
        if (mesh != null) Destroy(mesh);
    }

    void OnSwingStarted(int step) => current = null;

    // After animation, so the blade is sampled in this frame's pose.
    void LateUpdate()
    {
        if (attack != null && attack.IsAttacking && attack.IsHitActive && attack.TryGetBlade(out Vector3 tip, out Vector3 hilt))
        {
            if (current == null)
            {
                current = new Slash();
                slashes.Add(current);
            }
            List<Sample> s = current.samples;
            if (s.Count < MaxSamples && (s.Count == 0 || (tip - s[s.Count - 1].tip).sqrMagnitude >= MinStep * MinStep))
                s.Add(new Sample { tip = tip, inner = Vector3.Lerp(tip, hilt, bladeCoverage), time = Time.time });
        }
        else
        {
            current = null;
        }

        slashes.RemoveAll(sl => sl != current && (sl.samples.Count == 0 || Time.time - sl.samples[sl.samples.Count - 1].time > lifetime));
        BuildMesh();
    }

    void BuildMesh()
    {
        vertices.Clear();
        uvs.Clear();
        colors.Clear();
        triangles.Clear();
        float now = Time.time;

        foreach (Slash slash in slashes)
        {
            Smooth(slash.samples);
            int n = smooth.Count;
            if (n < 2) continue;

            float total = 0f;
            for (int i = 1; i < n; i++) total += Vector3.Distance(smooth[i - 1].tip, smooth[i].tip);
            float arc = 0f;
            int first = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Sample s = smooth[i];
                if (i > 0) arc += Vector3.Distance(smooth[i - 1].tip, s.tip);
                float age = now - s.time;
                float life = Mathf.Clamp01(age / lifetime);

                // The air keeps moving the way the blade went, and the edge blows outward.
                Vector3 along = (smooth[Mathf.Min(i + 1, n - 1)].tip - smooth[Mathf.Max(i - 1, 0)].tip).normalized;
                Vector3 outward = (s.tip - s.inner).normalized;
                Vector3 outer = s.tip + along * (drift * age) + outward * (spread * age);
                Vector3 inner = s.inner + along * (drift * age * 0.6f) + outward * (spread * age * 0.3f);

                float fade = (1f - life) * (1f - life);
                float tail = tailFade > 0f && total > 0f ? Mathf.Clamp01(arc / (total * tailFade)) : 1f;
                Color c = new Color(color.r, color.g, color.b, color.a * fade * tail);

                // Fixed to the trail, plus a flow back toward its start.
                float u = arc / Mathf.Max(streakLength, 0.01f) + now * flowSpeed;
                vertices.Add(inner);
                uvs.Add(new Vector2(u, 0f));
                colors.Add(c);
                vertices.Add(outer);
                uvs.Add(new Vector2(u, 1f));
                colors.Add(c);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = first + i * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 3);
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
            }
        }

        mesh.Clear();
        if (triangles.Count == 0) return;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    /// Catmull-Rom through the samples (tip and inner edge alike), so the curve is smooth between frames.
    void Smooth(List<Sample> p)
    {
        smooth.Clear();
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            smooth.Add(p[i]);
            if (i == n - 1) break;
            Sample p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, n - 1)];
            for (int k = 1; k <= smoothing; k++)
            {
                float t = k / (smoothing + 1f);
                smooth.Add(new Sample
                {
                    tip = CatmullRom(p0.tip, p1.tip, p2.tip, p3.tip, t),
                    inner = CatmullRom(p0.inner, p1.inner, p2.inner, p3.inner, t),
                    time = Mathf.Lerp(p1.time, p2.time, t),
                });
            }
        }
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }
}
