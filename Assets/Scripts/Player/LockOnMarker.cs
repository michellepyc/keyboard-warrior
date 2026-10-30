using UnityEngine;

/// A small glowing red bulb floating above the enemy you're locked on to (the closest
/// one in range, the same target attacks, blocking and the camera use), so it's clear
/// what's being attacked. It bobs gently, pops in when you lock on or switch targets,
/// and shrinks away when nothing is in range. It floats above the enemy's attack
/// warning so the two never overlap. Runs on real time, so hit-stop doesn't freeze it.
[RequireComponent(typeof(PlayerTargeting))]
public class LockOnMarker : MonoBehaviour
{
    public Color color = new Color(1f, 0.12f, 0.1f, 1f);
    [Tooltip("Diameter of the bulb (metres).")]
    public float size = 0.18f;
    [Tooltip("Height above the top of the enemy.")]
    public float height = 0.85f;
    [Tooltip("How far it bobs up and down, and how fast (bobs per second).")]
    public float bobHeight = 0.06f;
    public float bobSpeed = 1.2f;
    [Tooltip("Size of the soft glow around it, relative to the bulb.")]
    public float glowScale = 2.6f;
    [Range(0f, 1f)] public float glowAlpha = 0.45f;
    [Tooltip("Seconds to pop in and to shrink away.")]
    public float popTime = 0.15f;

    /// The enemy the bulb is over (null when nothing's locked on).
    public CombatTarget Target { get; private set; }
    public bool IsShowing => root != null && root.activeSelf;
    public Vector3 Position => root != null ? root.transform.position : Vector3.zero;

    PlayerTargeting targeting;
    GameObject root;
    Transform glow;
    Material bulbMaterial, glowMaterial;
    Texture2D dot;
    Camera cam;
    float shown;          // 0..1 pop
    float targetTop;      // height of the target's top above its origin
    Vector3 lastPosition;

    void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        Build();
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (bulbMaterial != null) Destroy(bulbMaterial);
        if (glowMaterial != null) Destroy(glowMaterial);
        if (dot != null) Destroy(dot);
    }

    void OnDisable()
    {
        if (root != null) root.SetActive(false);
        Target = null;
        shown = 0f;
    }

    void LateUpdate()
    {
        CombatTarget next = targeting.Closest();
        if (next != Target)
        {
            // Switching straight to another enemy pops in again over the new one.
            if (next != null) shown = 0f;
            Target = next;
            if (Target != null) targetTop = TopOf(Target);
        }

        float dt = Time.unscaledDeltaTime;
        float speed = popTime > 0f ? dt / popTime : 1f;
        shown = Mathf.Clamp01(shown + (Target != null ? speed : -speed));
        bool visible = shown > 0f;
        if (root.activeSelf != visible) root.SetActive(visible);
        if (!visible) return;

        if (Target != null)
        {
            float bob = Mathf.Sin(Time.unscaledTime * bobSpeed * Mathf.PI * 2f) * bobHeight;
            lastPosition = Target.transform.position + Vector3.up * (targetTop + height + bob);
        }
        root.transform.position = lastPosition;
        root.transform.localScale = Vector3.one * (size * EaseOutBack(shown));

        if (cam == null) cam = Camera.main;
        if (cam != null) glow.rotation = cam.transform.rotation;
        // A gentle pulse on the glow.
        float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 1.5f);
        glowMaterial.color = new Color(color.r, color.g, color.b, glowAlpha * pulse);
    }

    /// How far above its origin the enemy's top is: from its solid colliders, or its
    /// meshes if it has none. Effects parented to it (like the attack warning) don't count.
    static float TopOf(CombatTarget target)
    {
        bool found = false;
        Bounds b = default;
        foreach (Collider c in target.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !c.enabled) continue;
            if (!found) { b = c.bounds; found = true; } else b.Encapsulate(c.bounds);
        }
        if (!found)
        {
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.gameObject.name.Contains("(runtime)")) continue;
                if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
            }
        }
        return found ? b.max.y - target.transform.position.y : 2f;
    }

    void Build()
    {
        root = new GameObject("LockOnMarker (runtime)");

        // The bulb: a small unlit sphere, so it reads as a light rather than a shaded ball.
        // It and its glow draw over the scene and the typing box's background, but under
        // the box's letters, so they stay readable.
        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bulb.name = "Bulb";
        Destroy(bulb.GetComponent<Collider>());
        bulb.transform.SetParent(root.transform, false);
        MeshFilter filter = bulb.GetComponent<MeshFilter>();
        filter.sharedMesh = EnemyAttacker.WhiteVertexCopy(filter.sharedMesh);
        bulbMaterial = EnemyAttacker.OnTopMaterial(color, 3996);
        var bulbRenderer = bulb.GetComponent<MeshRenderer>();
        bulbRenderer.sharedMaterial = bulbMaterial;
        bulbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bulbRenderer.receiveShadows = false;

        // The glow: a soft dot that always faces the camera.
        dot = SoftDot();
        var quad = new GameObject("Glow");
        quad.transform.SetParent(root.transform, false);
        quad.transform.localScale = Vector3.one * glowScale;
        quad.AddComponent<MeshFilter>().sharedMesh = Quad();
        glowMaterial = EnemyAttacker.OnTopMaterial(color, 3995);
        glowMaterial.mainTexture = dot;
        var glowRenderer = quad.AddComponent<MeshRenderer>();
        glowRenderer.sharedMaterial = glowMaterial;
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
        glow = quad.transform;

        root.SetActive(false);
    }

    static Mesh Quad()
    {
        var mesh = new Mesh { name = "LockOnGlow" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "LockOnDot (runtime)", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
            float a = Mathf.Clamp01(1f - d);
            px[y * n + x] = new Color(1f, 1f, 1f, a * a);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
