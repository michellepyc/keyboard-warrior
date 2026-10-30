using TMPro;
using UnityEngine;

/// A row of pips by the player, one per Smite charge (up to Max Charges): gold when
/// banked, faint when empty. A charge gained pops its pip in with a white flash; one
/// spent or lost shrinks away; a full row pulses gently. While a charge can be spent
/// (SmiteAvailable) a small key cap shows the Smite key. The row shows while there are
/// charges (and for a moment after the count changes), and hides while typing, when the
/// sentence sits above the player. Drawn over the scene; animates on real time.
[RequireComponent(typeof(SmiteController))]
public class ChargePips : MonoBehaviour
{
    [Tooltip("Where the row floats, relative to the player's centre.")]
    public Vector3 offset = new Vector3(0f, 1.75f, -0.3f);
    [Tooltip("Pip diameter and spacing (metres).")]
    public float pipSize = 0.18f;
    public float spacing = 0.26f;

    [Header("Colours")]
    public Color filledColor = new Color(1f, 0.82f, 0.3f, 1f);
    public Color emptyColor = new Color(1f, 0.85f, 0.45f, 0.4f);
    public Color flashColor = Color.white;

    [Header("Animation (real seconds)")]
    [Tooltip("How long a pip takes to pop in or shrink away.")]
    public float popTime = 0.25f;
    [Tooltip("After the count changes, the row stays up at least this long (even at 0).")]
    public float lingerTime = 1f;
    [Tooltip("Seconds to fade the whole row in or out.")]
    public float fadeTime = 0.15f;

    [Header("Key hint")]
    [Tooltip("Show the Smite key beside the pips while a charge can be spent.")]
    public bool showKeyHint = true;
    public float keyHintSize = 0.24f;
    public Color keyCapColor = new Color(0.08f, 0.07f, 0.06f, 0.85f);
    public Color keyTextColor = new Color(1f, 0.85f, 0.4f, 1f);

    /// Whether the row is showing (fully or fading); for tests.
    public bool IsShowing => root != null && root.activeSelf;
    /// Whether the key hint is showing; for tests.
    public bool IsKeyHintShowing => keyRoot != null && keyRoot.activeSelf;
    /// How many pips are drawn filled right now; for tests.
    public int FilledShown { get; private set; }

    SmiteController smite;
    PlayerStateMachine states;
    GameObject root, keyRoot;
    Transform[] pips;
    Material[] pipMaterials;
    bool[] filled;
    float[] changedAt;
    Material keyCapMaterial;
    TextMeshPro keyText;
    Texture2D dot, roundRect;
    Camera cam;
    float alpha, keyAlpha;
    float visibleUntil = float.NegativeInfinity;

    void Awake()
    {
        smite = GetComponent<SmiteController>();
        states = GetComponent<PlayerStateMachine>();
        Build();
    }

    void OnEnable() => smite.ChargesChanged += OnChargesChanged;
    void OnDisable() => smite.ChargesChanged -= OnChargesChanged;

    void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (pipMaterials != null) foreach (Material m in pipMaterials) if (m != null) Destroy(m);
        if (keyCapMaterial != null) Destroy(keyCapMaterial);
        if (dot != null) Destroy(dot);
        if (roundRect != null) Destroy(roundRect);
    }

    void OnChargesChanged(int charges)
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < pips.Length; i++)
        {
            bool full = i < charges;
            if (full == filled[i]) continue;
            filled[i] = full;
            changedAt[i] = now;
        }
        visibleUntil = now + lingerTime;
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        PlayerState state = states != null ? states.State : PlayerState.Normal;
        bool want = (smite.Charges > 0 || now < visibleUntil) && state != PlayerState.Typing;
        alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, fadeTime > 0f ? dt / fadeTime : 1f);
        bool keyWant = want && showKeyHint && state == PlayerState.SmiteAvailable && smite.Charges > 0;
        keyAlpha = Mathf.MoveTowards(keyAlpha, keyWant ? 1f : 0f, fadeTime > 0f ? dt / fadeTime : 1f);

        bool visible = alpha > 0f;
        if (root.activeSelf != visible) root.SetActive(visible);
        if (keyRoot.activeSelf != keyAlpha > 0f) keyRoot.SetActive(keyAlpha > 0f);
        if (!visible) return;

        if (cam == null) cam = Camera.main;
        root.transform.SetPositionAndRotation(transform.position + offset, cam != null ? cam.transform.rotation : Quaternion.identity);

        bool rowFull = smite.Charges >= pips.Length && pips.Length > 0;
        float pulse = rowFull ? 1f + 0.08f * Mathf.Sin(now * Mathf.PI * 2f * 1.2f) : 1f;
        int shown = 0;
        for (int i = 0; i < pips.Length; i++)
        {
            float t = popTime > 0f ? Mathf.Clamp01((now - changedAt[i]) / popTime) : 1f;
            float scale;
            Color color;
            if (filled[i])
            {
                // Pops in (overshooting a little), flashing white, then settles gold.
                scale = pipSize * Mathf.Lerp(0.6f, 1f, EaseOutBack(t)) * pulse;
                color = Color.Lerp(flashColor, filledColor, t);
                shown++;
            }
            else
            {
                // Shrinks from a full pip down to a faint empty one.
                scale = pipSize * Mathf.Lerp(1f, 0.75f, t);
                color = Color.Lerp(filledColor, emptyColor, t);
            }
            pips[i].localScale = Vector3.one * scale;
            pipMaterials[i].color = new Color(color.r, color.g, color.b, color.a * alpha);
        }
        FilledShown = shown;

        if (keyRoot.activeSelf)
        {
            keyCapMaterial.color = new Color(keyCapColor.r, keyCapColor.g, keyCapColor.b, keyCapColor.a * keyAlpha * alpha);
            keyText.alpha = keyAlpha * alpha;
            string key = smite.smiteKey.ToString();
            if (keyText.text != key) keyText.text = key;
        }
    }

    void Build()
    {
        dot = SoftDot();
        roundRect = RoundRect();
        root = new GameObject("ChargePips (runtime)");
        int n = Mathf.Max(0, smite.maxCharges);
        pips = new Transform[n];
        pipMaterials = new Material[n];
        filled = new bool[n];
        changedAt = new float[n];
        for (int i = 0; i < n; i++)
        {
            var pip = new GameObject("Pip " + (i + 1));
            pip.transform.SetParent(root.transform, false);
            pip.transform.localPosition = new Vector3((i - (n - 1) * 0.5f) * spacing, 0f, 0f);
            pip.AddComponent<MeshFilter>().sharedMesh = Quad();
            pipMaterials[i] = EnemyAttacker.OnTopMaterial(emptyColor, 4040);
            pipMaterials[i].mainTexture = dot;
            var r = pip.AddComponent<MeshRenderer>();
            r.sharedMaterial = pipMaterials[i];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            pips[i] = pip.transform;
            changedAt[i] = float.NegativeInfinity;
        }

        // The key hint: a dark rounded key cap with the key's letter, right of the pips.
        keyRoot = new GameObject("KeyHint");
        keyRoot.transform.SetParent(root.transform, false);
        keyRoot.transform.localPosition = new Vector3(((n - 1) * 0.5f) * spacing + spacing * 0.5f + keyHintSize * 0.75f, 0f, 0f);
        var cap = new GameObject("Cap");
        cap.transform.SetParent(keyRoot.transform, false);
        cap.transform.localScale = Vector3.one * keyHintSize;
        cap.AddComponent<MeshFilter>().sharedMesh = Quad();
        keyCapMaterial = EnemyAttacker.OnTopMaterial(keyCapColor, 4041);
        keyCapMaterial.mainTexture = roundRect;
        var capRenderer = cap.AddComponent<MeshRenderer>();
        capRenderer.sharedMaterial = keyCapMaterial;
        capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var label = new GameObject("Key");
        label.transform.SetParent(keyRoot.transform, false);
        label.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        keyText = label.AddComponent<TextMeshPro>();
        keyText.text = smite.smiteKey.ToString();
        keyText.alignment = TextAlignmentOptions.Center;
        keyText.enableWordWrapping = false;
        keyText.fontStyle = FontStyles.Bold;
        keyText.fontSize = keyHintSize * 7f;
        keyText.color = keyTextColor;
        keyText.rectTransform.sizeDelta = Vector2.one * keyHintSize;
        Material m = keyText.fontMaterial;
        m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4042;

        keyRoot.SetActive(false);
        root.SetActive(false);
    }

    static Mesh Quad()
    {
        var mesh = new Mesh { name = "ChargePip" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // A round pip: solid in the middle with a soft rim.
    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ChargePipDot (runtime)", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
            px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 4f));
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // A rounded square for the key cap.
    static Texture2D RoundRect()
    {
        const int n = 32;
        const float r = 8f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ChargeKeyCap (runtime)", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - (n - 1) * 0.5f) - ((n - 1) * 0.5f - r));
            float dy = Mathf.Max(0f, Mathf.Abs(y - (n - 1) * 0.5f) - ((n - 1) * 0.5f - r));
            float a = Mathf.Clamp01(r - new Vector2(dx, dy).magnitude + 0.5f);
            px[y * n + x] = new Color(1f, 1f, 1f, a);
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
