using TMPro;
using UnityEngine;

/// The Smite's charge-up visuals (built in code, no assets needed):
/// - A small spark where each letter of the finished sentence reaches the sword, and
///   a bigger burst when the last one does.
/// - While the Smite is ready: the sword glows (a pulsing light, and embers drifting
///   off the blade) and an aura of motes swirls up around the player. They fade out
///   when it's used or runs out.
/// - Rewards: a small camera shake as the charge lands, and for a sentence typed with
///   no mistakes a "PERFECT!" pop and a bigger burst.
/// Kept light on purpose, so the player can still read what's happening.
public class SmiteEffects : MonoBehaviour
{
    public PlayerStateMachine states;
    public TypingDisplay display;
    public TypingController typing;
    [Tooltip("For the camera shake (found automatically).")]
    public ParryFeedback feedback;
    [Tooltip("Whose sword glows (the player, found automatically).")]
    public PlayerAttack swordOwner;

    [Header("Sparks")]
    public Color sparkColor = new Color(1f, 0.85f, 0.45f);
    [Tooltip("Sparks per letter that reaches the sword.")]
    public int sparksPerLetter = 2;
    [Tooltip("Sparks when the last letter arrives.")]
    public int chargedBurst = 40;

    [Header("Aura (while the Smite is ready)")]
    public Color auraColor = new Color(1f, 0.82f, 0.3f, 1f);
    [Tooltip("Motes per second.")]
    public float auraRate = 40f;
    [Tooltip("Radius (metres) of the ring the motes rise from.")]
    public float auraRadius = 0.6f;
    [Tooltip("Where the ring sits relative to the player's centre.")]
    public Vector3 auraOffset = new Vector3(0f, -0.7f, 0f);
    [Tooltip("How fast the motes swirl around the player.")]
    public float auraSwirl = 2.5f;

    [Header("Rewards")]
    [Tooltip("Camera shake when the charge lands in the sword (0 = none).")]
    public float chargeShake = 0.06f;
    [Tooltip("Camera shake on each wrong key while typing (0 = none).")]
    public float typoShake = 0.08f;
    [Tooltip("Shown when the sentence had no mistakes.")]
    public string perfectText = "PERFECT!";
    public Color perfectColor = new Color(1f, 1f, 0.85f, 1f);
    public float perfectSize = 4.5f;
    [Tooltip("Where PERFECT! pops up, relative to the player's centre (it rises from there).")]
    public Vector3 perfectOffset = new Vector3(0f, 2.2f, -0.3f);
    [Tooltip("A perfect sentence's burst is this many times bigger.")]
    public float perfectBurstMultiplier = 2f;

    [Header("Sword glow")]
    public Color glowColor = new Color(1f, 0.78f, 0.35f);
    public float glowIntensity = 3.5f;
    public float glowRange = 3f;
    [Tooltip("Embers drifting off the blade, per second.")]
    public float emberRate = 30f;
    public Color emberColor = new Color(1f, 0.9f, 0.55f, 1f);
    [Tooltip("Pulses per second.")]
    public float glowPulse = 1.5f;
    [Tooltip("Seconds to fade in and out.")]
    public float fadeTime = 0.25f;

    public bool IsAuraOn => auraOn;
    public bool IsPerfectShowing => perfectLabel != null && perfectLabel.gameObject.activeSelf;
    public float GlowIntensity => glow != null ? glow.intensity : 0f;
    public int AuraParticles => aura != null ? aura.particleCount : 0;

    ParticleSystem sparks;
    ParticleSystem aura;
    ParticleSystem embers;
    Light glow;
    float emberDebt;
    Texture2D dot;
    bool auraOn;
    float fade; // 0..1
    TextMeshPro perfectLabel;
    Camera cam;
    bool perfect;
    float perfectAt = float.NegativeInfinity;
    const float PerfectTime = 1.3f;

    void Awake()
    {
        if (states == null) states = FindObjectOfType<PlayerStateMachine>();
        if (display == null) display = FindObjectOfType<TypingDisplay>();
        if (swordOwner == null && states != null) swordOwner = states.GetComponent<PlayerAttack>();
        if (typing == null && states != null) typing = states.GetComponent<TypingController>();
        if (feedback == null) feedback = FindObjectOfType<ParryFeedback>();
        dot = SoftDot();
        perfectLabel = CreateLabel("SmitePerfect (runtime)", perfectSize, perfectColor);
        perfectLabel.text = perfectText;
        sparks = CreateSparks();
        aura = CreateAura();
        embers = CreateEmbers();
        glow = CreateGlow();
    }

    void OnEnable()
    {
        if (display != null)
        {
            display.LetterArrived += OnLetterArrived;
            display.LettersArrived += OnLettersArrived;
        }
        if (states != null) states.Changed += OnStateChanged;
        if (typing != null)
        {
            typing.SessionStarted += OnTypingStarted;
            typing.SentenceCompleted += OnSentenceCompleted;
            typing.WrongCharacter += OnWrongKey;
        }
    }

    void OnDisable()
    {
        if (typing != null)
        {
            typing.SessionStarted -= OnTypingStarted;
            typing.SentenceCompleted -= OnSentenceCompleted;
            typing.WrongCharacter -= OnWrongKey;
        }
        if (display != null)
        {
            display.LetterArrived -= OnLetterArrived;
            display.LettersArrived -= OnLettersArrived;
        }
        if (states != null) states.Changed -= OnStateChanged;
        SetAura(false);
    }

    void OnDestroy()
    {
        if (dot != null) Destroy(dot);
        if (perfectLabel != null) Destroy(perfectLabel.gameObject);
    }

    void OnTypingStarted(TypingSession session) => perfect = false;

    void OnWrongKey(char expected, char received)
    {
        if (feedback != null && typoShake > 0f) feedback.ShakeCamera(typoShake);
    }

    void OnSentenceCompleted() => perfect = typing.Session != null && typing.Session.Mistakes == 0;

    void OnLetterArrived(Vector3 at) => Burst(at, sparksPerLetter, 3f);

    // Charged: a burst, then the aura, as long as the Smite is still waiting for Q.
    void OnLettersArrived(Vector3 at)
    {
        Burst(at, Mathf.RoundToInt(chargedBurst * (perfect ? perfectBurstMultiplier : 1f)), perfect ? 8f : 6f);
        if (feedback != null && chargeShake > 0f) feedback.ShakeCamera(chargeShake);
        if (perfect) perfectAt = Time.unscaledTime;
        if (states == null || states.State == PlayerState.SmiteReady) SetAura(true);
    }

    void OnStateChanged(PlayerState from, PlayerState to)
    {
        if (to != PlayerState.SmiteReady) SetAura(false);
    }

    void SetAura(bool on)
    {
        auraOn = on;
        if (aura == null) return;
        var emission = aura.emission;
        emission.enabled = on;
    }

    void LateUpdate()
    {
        // Real time, so it behaves the same through hit-stop or slow motion.
        float dt = Time.unscaledDeltaTime;
        fade = Mathf.MoveTowards(fade, auraOn ? 1f : 0f, fadeTime > 0f ? dt / fadeTime : 1f);

        Vector3 player = states != null ? states.transform.position : transform.position;
        if (aura != null) aura.transform.position = player + auraOffset;

        bool haveBlade = false;
        Vector3 bladeTip = default, bladeHilt = default;
        if (swordOwner != null) haveBlade = swordOwner.TryGetBlade(out bladeTip, out bladeHilt);

        if (glow != null)
        {
            glow.enabled = fade > 0f;
            if (glow.enabled)
            {
                glow.transform.position = haveBlade ? Vector3.Lerp(bladeHilt, bladeTip, 0.5f) : player + Vector3.up;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * glowPulse * Mathf.PI * 2f);
                glow.intensity = glowIntensity * pulse * fade;
            }
        }

        // Embers from random points along the blade, while the aura is on.
        if (embers != null && auraOn && haveBlade)
        {
            emberDebt += emberRate * dt;
            var emit = new ParticleSystem.EmitParams { startColor = emberColor };
            for (; emberDebt >= 1f; emberDebt -= 1f)
            {
                emit.position = Vector3.Lerp(bladeHilt, bladeTip, Random.Range(0.15f, 1f));
                emit.velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.3f, 0.8f), 0f);
                embers.Emit(emit, 1);
            }
        }
        else emberDebt = 0f;

        UpdateLabels(player);
    }

    // PERFECT! pops in, rises and fades.
    void UpdateLabels(Vector3 player)
    {
        if (cam == null) cam = Camera.main;
        Quaternion facing = cam != null ? cam.transform.rotation : Quaternion.identity;
        float now = Time.unscaledTime;

        if (perfectLabel != null)
        {
            float t = now - perfectAt;
            bool show = t >= 0f && t < PerfectTime;
            if (perfectLabel.gameObject.activeSelf != show) perfectLabel.gameObject.SetActive(show);
            if (show)
            {
                float pop = Mathf.Clamp01(t / 0.25f);
                float rise = Mathf.SmoothStep(0f, 0.6f, t / PerfectTime);
                perfectLabel.transform.SetPositionAndRotation(player + perfectOffset + Vector3.up * rise, facing);
                perfectLabel.transform.localScale = Vector3.one * EaseOutBack(pop);
                perfectLabel.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PerfectTime - 0.4f, PerfectTime, t));
            }
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // Floating text in the typing display's font and style (bold, outlined, drawn on top).
    TextMeshPro CreateLabel(string name, float size, Color color)
    {
        var go = new GameObject(name);
        var label = go.AddComponent<TextMeshPro>();
        if (display != null && display.font != null) label.font = display.font;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.richText = false;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.rectTransform.sizeDelta = new Vector2(8f, 2f);
        Shader full = Shader.Find("TextMeshPro/Distance Field");
        if (full != null) label.fontMaterial.shader = full;
        label.outlineWidth = 0.3f;
        label.outlineColor = Color.black;
        Material m = label.fontMaterial;
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.3f);
        ShaderUtilities.UpdateShaderRatios(m);
        m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4000;
        go.SetActive(false);
        return label;
    }

    void Burst(Vector3 at, int count, float speed)
    {
        if (sparks == null || count <= 0) return;
        var main = sparks.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        sparks.Emit(new ParticleSystem.EmitParams { position = at, startColor = sparkColor, applyShapeToPosition = true }, count);
    }

    // A small round soft-edged dot, so motes look round instead of square.
    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SmiteDot (runtime)", wrapMode = TextureWrapMode.Clamp };
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

    // Additive glows on dark backgrounds but vanishes on bright ones, so it's used for
    // the brief sparks; the lasting aura and embers are alpha-blended so they read on
    // the white floor and the sky too. Sprites/Default (always in builds) if missing.
    Material GlowMaterial(string name) => ParticleMaterial(name, "Legacy Shaders/Particles/Additive");
    Material SolidMaterial(string name) => ParticleMaterial(name, "Legacy Shaders/Particles/Alpha Blended");

    Material ParticleMaterial(string name, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return new Material(shader) { name = name, mainTexture = dot };
    }

    ParticleSystem NewSystem(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        return ps;
    }

    ParticleSystem CreateSparks()
    {
        ParticleSystem ps = NewSystem("SmiteSparks (runtime)");
        var main = ps.main;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.gravityModifier = 0.6f;
        main.maxParticles = 400;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GlowMaterial("SmiteSparks (runtime)");
        ps.Play();
        return ps;
    }

    ParticleSystem CreateAura()
    {
        ParticleSystem ps = NewSystem("SmiteAura (runtime)");
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = auraColor;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = auraRate;
        emission.enabled = false;
        // A flat ring around the player's waist.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = auraRadius;
        shape.radiusThickness = 0.2f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        // Rise and swirl around the player.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(1.2f, 2f); // the ring is rotated, so local z is world up
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(auraSwirl);
        // Fade in and out over each mote's life.
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SolidMaterial("SmiteAura (runtime)");
        ps.Play();
        return ps;
    }

    ParticleSystem CreateEmbers()
    {
        ParticleSystem ps = NewSystem("SmiteEmbers (runtime)");
        var main = ps.main;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SolidMaterial("SmiteEmbers (runtime)");
        ps.Play();
        return ps;
    }

    Light CreateGlow()
    {
        var go = new GameObject("SmiteGlow (runtime)");
        go.transform.SetParent(transform, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = glowColor;
        l.range = glowRange;
        l.intensity = 0f;
        l.shadows = LightShadows.None;
        l.enabled = false;
        return l;
    }
}
