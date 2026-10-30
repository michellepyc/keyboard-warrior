using TMPro;
using UnityEngine;

/// Shows the sentence being typed in a TextMeshPro text box that rides along with the
/// camera at a fixed spot on screen (or, with Follow Camera off, floats above the
/// player in the world, facing the camera).
/// Each character is animated on its own (TypingSentenceVisual holds their states):
/// untyped ones are dim and the next one to type is yellow; a correct one hops and
/// turns bright and stays that way; a
/// wrong key shakes the current character red and flashes the backdrop red, then it
/// settles back. This moves and recolours each character's own part of the text
/// mesh, so TextMeshPro's layout (wrapping, fitting) is untouched. Animations run on
/// real time, so the slow motion doesn't slow them.
/// When the sentence is finished, a wave pops across it as it flashes white and warms
/// to the charged colour (anticipation), then the letters peel off left to right and
/// arc into the sword (LetterArrived for each, LettersArrived after the last).
/// When typing fails (time runs out, cancelled, hit), the sentence jolts, the letters
/// not yet typed turn red and tumble away under gravity, and the typed ones dim and
/// sink. Neither ending holds up the player: input is already back by then.
/// The sentence always fits inside Box Size: TextMeshPro wraps it at spaces and
/// shrinks the font (between Min and Max Font Size) until every line fits. A word is
/// never split across lines: if TextMeshPro would have to break a long word to fit
/// it, the font is shrunk further until the word fits on one line. It also leaves
/// room for the animations: the font shrinks until even a fully raised hop or the
/// widest shake keeps every letter in the box.
/// Outline and an optional backdrop keep it readable against the scene.
public class TypingDisplay : MonoBehaviour
{
    public TypingController typing;

    [Header("Placement")]
    [Tooltip("Ride along with the camera at a fixed spot on screen, so the sentence is always in view (whatever the camera does) and steady while the camera shakes. Off: float above the player in the world.")]
    public bool followCamera = true;
    [Tooltip("Where on screen the box's centre sits (0..1 across and up).")]
    public Vector2 screenPosition = new Vector2(0.5f, 0.8f);
    [Tooltip("How far in front of the camera the box sits (metres); with Box Size this sets how big it looks.")]
    public float cameraDistance = 4.5f;
    [Tooltip("Not following the camera: what it floats near (the player if empty).")]
    public Transform anchor;
    [Tooltip("Not following the camera: offset from the anchor, in world space.")]
    public Vector3 offset = new Vector3(0f, 2.7f, -0.3f);
    [Tooltip("Not following the camera: the box slides this far (metres) away from the enemy you're locked on to, so the enemy, its attack warning and the lock-on bulb stay in view.")]
    public float awayFromTarget = 0.5f;

    [Header("Box (metres)")]
    [Tooltip("The sentence always stays inside this rectangle.")]
    public Vector2 boxSize = new Vector2(4f, 1.1f);
    public float maxFontSize = 6f;
    public float minFontSize = 1f;
    [Tooltip("How much the font shrinks per step when a long word won't fit on a line.")]
    public float fontStep = 0.1f;

    [Header("Text")]
    [Tooltip("Font asset (TextMeshPro's default if empty).")]
    public TMP_FontAsset font;
    public FontStyles fontStyle = FontStyles.Bold;
    [Tooltip("TextMeshPro outlines grow inward, into the letters; Face Dilate thickens the letters to make room.")]
    [Range(0f, 1f)] public float outlineWidth = 0.3f;
    public Color outlineColor = new Color(0f, 0f, 0f, 1f);
    [Tooltip("Thickens the letters (-1..1), so the outline sits around them instead of thinning them.")]
    [Range(-1f, 1f)] public float faceDilate = 0.3f;
    [Tooltip("Draw the text over the scene, so walls and characters never hide it.")]
    public bool drawOnTop = true;

    [Header("Characters")]
    [Tooltip("Not typed yet.")]
    public Color untypedColor = new Color(0.62f, 0.64f, 0.7f, 1f);
    [Tooltip("Typed correctly (stays this colour).")]
    public Color correctColor = Color.white;
    [Tooltip("The letter to type next.")]
    public Color currentColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("A wrong key on this character, while it shakes.")]
    public Color errorColor = new Color(1f, 0.25f, 0.2f, 1f);
    public TypingSentenceVisual.Motion motion = new TypingSentenceVisual.Motion();
    [Tooltip("A wrong key also flashes the whole box this colour.")]
    public Color flashColor = new Color(0.75f, 0.05f, 0.05f, 0.7f);
    public float flashDuration = 0.25f;

    [Header("Completion")]
    [Tooltip("The finished sentence holds still and warms up this long (real seconds) before the letters fly.")]
    public float anticipationTime = 0.15f;
    [Tooltip("The finished sentence's colour, and the letters' as they fly.")]
    public Color chargedColor = new Color(1f, 0.84f, 0.4f, 1f);
    [Tooltip("How much each letter swells during the anticipation.")]
    public float anticipationScale = 1.12f;
    [Tooltip("How long each letter takes to fly into the sword.")]
    public float detachDuration = 0.4f;
    [Tooltip("Letters leave one after another, left to right, spread over at most this long.")]
    public float detachSpread = 0.3f;
    [Tooltip("How high (metres) the letters arc on the way.")]
    public float detachArc = 0.8f;
    [Tooltip("The letters fly into this player's sword (found automatically). Without one they fly to the anchor.")]
    public PlayerAttack swordOwner;
    [Tooltip("The finishing wave takes this long to cross the sentence (real seconds).")]
    public float rippleSpread = 0.12f;
    [Tooltip("How high (fraction of a line) each letter hops as the wave passes.")]
    public float rippleHop = 0.18f;

    [Header("Time running out")]
    [Tooltip("With this many seconds left, the letters still to type start blinking.")]
    public float blinkStartTime = 3f;
    [Tooltip("Blinks per second when it starts, and right before time runs out.")]
    public float blinkSlowHz = 2f;
    public float blinkFastHz = 10f;
    [Tooltip("The letters' colour in the dim half of each blink (they stay readable).")]
    public Color blinkColor = new Color(1f, 0.35f, 0.3f, 0.45f);

    [Header("Failure")]
    [Tooltip("The letters that weren't typed turn this colour and fall.")]
    public Color failColor = new Color(1f, 0.2f, 0.15f, 1f);
    [Tooltip("The jolt before they fall: how long, and how far (metres) the sentence shakes.")]
    public float failJoltTime = 0.12f;
    public float failShake = 0.08f;
    [Tooltip("Each falling letter lasts this long before it's gone.")]
    public float fallTime = 0.9f;
    [Tooltip("Letters start falling at random within this long, so they don't drop as one.")]
    public float fallStagger = 0.25f;
    [Tooltip("Downward pull on falling letters (metres per second squared, screen-down).")]
    public float fallGravity = 9f;
    [Tooltip("How hard each letter pops up and sideways as it starts to fall (metres per second).")]
    public float fallPop = 1.5f;
    [Tooltip("How fast falling letters spin (degrees per second, at most).")]
    public float fallSpin = 360f;

    [Header("Backdrop")]
    public bool showBackdrop = true;
    public Color backdropColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Backdrop margin around the box, in metres.")]
    public float backdropPadding = 0.15f;

    /// The TextMeshPro text (made at startup).
    public TextMeshPro Text { get; private set; }
    /// The characters' states (null when nothing is shown).
    public TypingSentenceVisual Visual { get; private set; }
    public bool IsShowing => root != null && root.activeSelf;
    /// Playing the finished-sentence animation.
    public bool IsCompleting => IsShowing && completing;
    /// Playing the failed-sentence animation.
    public bool IsFailing => IsShowing && failing;

    /// A letter reached the sword (world position).
    public event System.Action<Vector3> LetterArrived;
    /// The last letter reached the sword (world position).
    public event System.Action<Vector3> LettersArrived;

    GameObject root;
    Transform backdrop;
    Material backdropMaterial;
    Camera cam;
    PlayerTargeting targeting;
    float awayShift;
    float blinkPhase;
    TMP_MeshInfo[] restMesh;
    float flashAt = float.NegativeInfinity;
    bool completing;
    float completedAt;
    bool[] arrived;
    bool failing;
    float failedAt;
    bool[] typedWhenFailed;
    Vector3[] fallVelocity;
    float[] fallDelay, fallSpinRate;

    void Awake()
    {
        if (typing == null) typing = FindObjectOfType<TypingController>();
        if (anchor == null && typing != null) anchor = typing.transform;
        if (swordOwner == null && typing != null) swordOwner = typing.GetComponent<PlayerAttack>();
        if (anchor != null) targeting = anchor.GetComponent<PlayerTargeting>();
        Build();
        Hide();
    }

    void OnEnable()
    {
        if (typing == null) return;
        typing.SessionStarted += OnSessionStarted;
        typing.SentenceCompleted += OnSentenceCompleted;
        typing.SessionEnded += OnSessionEnded;
        typing.CorrectCharacter += OnCorrect;
        typing.WrongCharacter += OnWrong;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextRebuilt);
    }

    void OnDisable()
    {
        if (typing == null) return;
        typing.SessionStarted -= OnSessionStarted;
        typing.SentenceCompleted -= OnSentenceCompleted;
        typing.SessionEnded -= OnSessionEnded;
        typing.CorrectCharacter -= OnCorrect;
        typing.WrongCharacter -= OnWrong;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextRebuilt);
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (backdropMaterial != null) Destroy(backdropMaterial);
    }

    void OnSessionStarted(TypingSession session) => Show(session.Sentence);

    // Finished: play the charge-up. Timing out or cancelling hides it straight away.
    void OnSentenceCompleted()
    {
        if (!IsShowing) return;
        completing = true;
        completedAt = Time.unscaledTime;
        arrived = new bool[Text.textInfo.characterCount];
    }

    void OnSessionEnded()
    {
        if (completing) return;
        if (IsShowing && Visual != null) BeginFailure();
        else Hide();
    }

    void BeginFailure()
    {
        failing = true;
        failedAt = Time.unscaledTime;
        int n = Text.textInfo.characterCount;
        typedWhenFailed = new bool[n];
        fallVelocity = new Vector3[n];
        fallDelay = new float[n];
        fallSpinRate = new float[n];
        for (int i = 0; i < n; i++)
        {
            typedWhenFailed[i] = i < Visual.Count && Visual[i] == CharacterState.Correct;
            fallVelocity[i] = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.3f, 1f), 0f) * fallPop;
            fallDelay[i] = Random.Range(0f, fallStagger);
            fallSpinRate[i] = Random.Range(-fallSpin, fallSpin);
        }
    }

    void OnCorrect(char c, int index)
    {
        if (Visual != null) Visual.MarkCorrect(index, Time.unscaledTime);
    }

    void OnWrong(char expected, char received)
    {
        if (Visual == null || typing.Session == null) return;
        Visual.MarkError(typing.Session.Index, Time.unscaledTime);
        flashAt = Time.unscaledTime;
    }

    // TextMeshPro rebuilt the mesh: keep its fresh layout as the resting pose.
    void OnTextRebuilt(UnityEngine.Object changed)
    {
        if (changed == Text) restMesh = Text.textInfo.CopyMeshInfoVertexData();
    }

    void Build()
    {
        // Its own object, not under the player, so it doesn't turn or scale with them.
        root = new GameObject("TypingDisplay (runtime)");
        var textObject = new GameObject("Sentence");
        textObject.transform.SetParent(root.transform, false);
        Text = textObject.AddComponent<TextMeshPro>();
        if (font != null) Text.font = font;
        Text.alignment = TextAlignmentOptions.Center;
        Text.enableWordWrapping = true;
        Text.overflowMode = TextOverflowModes.Overflow;
        Text.richText = false;
        // The font's default material uses TextMeshPro's mobile shader, which ignores the
        // outline settings; the full one supports outline (and underlay) directly.
        Shader full = Shader.Find("TextMeshPro/Distance Field");
        if (full != null) Text.fontMaterial.shader = full;

        if (showBackdrop)
        {
            var quad = new GameObject("Backdrop");
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just behind the text
            quad.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            // UI/Default: transparent, coloured by the mesh, and its depth test can be
            // turned off like the text's.
            backdropMaterial = new Material(Shader.Find("UI/Default"));
            backdropMaterial.SetFloat("unity_GUIZTestMode", (float)(drawOnTop ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual));
            backdropMaterial.renderQueue = 3990; // under the text
            var r = quad.AddComponent<MeshRenderer>();
            r.sharedMaterial = backdropMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            backdrop = quad.transform;
        }
        ApplyStyle();
    }

    static Mesh UnitQuad()
    {
        var mesh = new Mesh { name = "TypingBackdrop" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// Colour, outline, draw-on-top and box size, from the settings (call after changing them).
    public void ApplyStyle()
    {
        Text.color = Color.white; // each character's colour is set per vertex
        Text.fontStyle = fontStyle;
        Text.rectTransform.sizeDelta = boxSize;
        // These make the text its own material copy (the font asset is untouched) and pad the mesh for the outline.
        Text.outlineWidth = outlineWidth;
        Text.outlineColor = outlineColor;
        Material m = Text.fontMaterial;
        m.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
        ShaderUtilities.UpdateShaderRatios(m);
        // Drawn last and through anything in front of it.
        m.SetFloat("unity_GUIZTestMode", (float)(drawOnTop ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual));
        m.renderQueue = 4000;
        if (backdrop != null)
        {
            backdrop.localScale = new Vector3(boxSize.x + backdropPadding * 2f, boxSize.y + backdropPadding * 2f, 1f);
            backdropMaterial.SetColor("_Color", backdropColor);
        }
    }

    /// Shows a sentence, fitted to the box.
    public void Show(string sentence)
    {
        root.SetActive(true);
        Text.text = sentence ?? "";
        Visual = new TypingSentenceVisual(Text.text.Length, motion);
        flashAt = float.NegativeInfinity;
        completing = false;
        failing = false;
        blinkPhase = 0f;
        Fit();
        restMesh = Text.textInfo.CopyMeshInfoVertexData();
        Follow(true);
        Animate();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        Visual = null;
        completing = false;
        failing = false;
    }

    /// TextMeshPro's auto-size shrinks the font until the lines fit the box. It can
    /// still break a word that's wider than a whole line, bold glyphs can poke a
    /// little past the edge, and the hop and shake move letters further out, so if any
    /// word ends up on two lines or any letter is (or would animate) outside the box,
    /// cap the size lower and lay it out again.
    public void Fit()
    {
        Text.rectTransform.sizeDelta = boxSize;
        Text.enableAutoSizing = true;
        Text.fontSizeMin = minFontSize;
        float cap = maxFontSize;
        for (int guard = 0; guard < 400; guard++)
        {
            Text.fontSizeMax = cap;
            Text.ForceMeshUpdate();
            if ((!SplitsAWord() && InsideBox() && InsideBoxWhenAnimating()) || cap <= minFontSize) break;
            cap = Mathf.Max(minFontSize, Text.fontSize - fontStep);
        }
        if (Text.isTextOverflowing || SplitsAWord() || !InsideBox() || !InsideBoxWhenAnimating())
            Debug.LogWarning($"[TypingDisplay] \"{Text.text}\" doesn't fit the {boxSize.x}x{boxSize.y} box even at font size {minFontSize}. Make the box bigger or Min Font Size smaller.", this);
    }

    /// Whether every visible letter lies within the box.
    public bool InsideBox()
    {
        TMP_TextInfo info = Text.textInfo;
        float hx = boxSize.x * 0.5f, hy = boxSize.y * 0.5f;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            if (c.bottomLeft.x < -hx || c.topRight.x > hx || c.descender < -hy || c.ascender > hy) return false;
        }
        return true;
    }

    /// Whether every letter stays in the box at the extremes of its animations: the top
    /// of the hop (raised and grown, about its own centre, as Animate does) and the
    /// widest point of the shake. Uses the resting layout, so call it after a layout.
    public bool InsideBoxWhenAnimating()
    {
        TMP_TextInfo info = Text.textInfo;
        float hx = boxSize.x * 0.5f, hy = boxSize.y * 0.5f;
        float grow = Mathf.Max(1f, motion.bounceScale);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            float lineHeight = info.lineInfo[c.lineNumber].lineHeight;
            Vector3 center = (c.vertex_BL.position + c.vertex_TR.position) * 0.5f;
            float up = Mathf.Max(0f, motion.bounceHeight) * lineHeight;
            float left = center.x + (c.bottomLeft.x - center.x) * grow;
            float right = center.x + (c.topRight.x - center.x) * grow;
            float top = center.y + (c.topRight.y - center.y) * grow + up;
            float bottom = center.y + (c.bottomLeft.y - center.y) * grow + Mathf.Min(0f, up);
            if (left < -hx || right > hx || top > hy || bottom < -hy) return false;
            float shake = Mathf.Abs(motion.shakeDistance) * lineHeight;
            if (c.bottomLeft.x - shake < -hx || c.topRight.x + shake > hx) return false;
        }
        return true;
    }

    /// Whether any word's letters are on more than one line.
    public bool SplitsAWord()
    {
        TMP_TextInfo info = Text.textInfo;
        for (int w = 0; w < info.wordCount; w++)
        {
            TMP_WordInfo word = info.wordInfo[w];
            int first = info.characterInfo[word.firstCharacterIndex].lineNumber;
            int last = info.characterInfo[word.lastCharacterIndex].lineNumber;
            if (first != last) return true;
        }
        return false;
    }

    void LateUpdate()
    {
        if (!IsShowing) return;
        Follow();
        if (completing) AnimateCompletion();
        else if (failing) AnimateFailure();
        else Animate();
    }

    /// Where the letters fly: the middle of the sword blade, or above the anchor.
    public Vector3 ChargeTarget()
    {
        if (swordOwner != null && swordOwner.TryGetBlade(out Vector3 tip, out Vector3 hilt)) return Vector3.Lerp(hilt, tip, 0.5f);
        return (anchor != null ? anchor.position : root.transform.position) + Vector3.up;
    }

    /// The finished sentence: a still, swelling, warming beat, then each letter (left to
    /// right, a little after the one before) arcs into the sword, shrinking and fading.
    void AnimateCompletion()
    {
        if (restMesh == null) return;
        float t = Time.unscaledTime - completedAt;
        TMP_TextInfo info = Text.textInfo;
        Vector3 targetWorld = ChargeTarget();
        Vector3 target = Text.transform.InverseTransformPoint(targetWorld);

        int visible = 0;
        for (int i = 0; i < info.characterCount; i++) if (info.characterInfo[i].isVisible) visible++;
        float gap = visible > 1 ? detachSpread / (visible - 1) : 0f;
        float ripple = visible > 1 ? Mathf.Min(rippleSpread, detachSpread) / (visible - 1) : 0f;
        float beat = anticipationTime > 0f ? Mathf.Clamp01(t / anticipationTime) : 1f;

        int rank = 0, done = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            int m = c.materialReferenceIndex, v = c.vertexIndex;
            if (m >= restMesh.Length || v + 3 >= restMesh[m].vertices.Length) { rank++; continue; }

            float start = anticipationTime + rank * gap;
            float u = detachDuration > 0f ? Mathf.Clamp01((t - start) / detachDuration) : (t >= start ? 1f : 0f);
            // The finishing wave: each letter flashes white, pops (with a little overshoot)
            // and hops as it passes, then settles warm and swollen.
            float lb = anticipationTime > 0f ? Mathf.Clamp01((t - rank * ripple) / anticipationTime) : 1f;
            float swell = 1f + (anticipationScale - 1f) * EaseOutBack(lb);
            Color charged = Color.Lerp(Color.white, chargedColor, lb);
            float hop = Mathf.Sin(Mathf.PI * lb) * rippleHop * info.lineInfo[c.lineNumber].lineHeight;
            rank++;

            Vector3[] rest = restMesh[m].vertices;
            Vector3 center = (rest[v] + rest[v + 2]) * 0.5f;
            Vector3 position = center + Vector3.up * hop;
            float scale = swell;
            float alpha = 1f;
            if (u > 0f)
            {
                // Speeds up into the sword along an arc.
                float e = u * u;
                Vector3 control = (center + target) * 0.5f + Vector3.up * detachArc;
                center += Vector3.up * hop;
                position = (1f - e) * (1f - e) * center + 2f * (1f - e) * e * control + e * e * target;
                scale = swell * Mathf.Lerp(1f, 0.25f, e);
                alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, u));
            }
            if (u >= 1f)
            {
                done++;
                if (i < arrived.Length && !arrived[i])
                {
                    arrived[i] = true;
                    LetterArrived?.Invoke(targetWorld);
                }
            }

            Vector3[] verts = info.meshInfo[m].vertices;
            for (int k = 0; k < 4; k++) verts[v + k] = position + (rest[v + k] - center) * scale;
            Color32 color = new Color(charged.r, charged.g, charged.b, charged.a * alpha);
            Color32[] colors = info.meshInfo[m].colors32;
            for (int k = 0; k < 4; k++) colors[v + k] = color;
        }
        Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);

        if (backdropMaterial != null)
        {
            // Warms a little with the beat, then fades away as the letters leave.
            float leaving = Mathf.Clamp01((t - anticipationTime) / Mathf.Max(0.01f, detachSpread + detachDuration));
            Color b = Color.Lerp(backdropColor, new Color(chargedColor.r * 0.35f, chargedColor.g * 0.25f, 0f, backdropColor.a), beat * 0.5f);
            b.a *= 1f - leaving;
            backdropMaterial.SetColor("_Color", b);
        }

        if (done >= visible)
        {
            LettersArrived?.Invoke(targetWorld);
            Hide();
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    /// The failed sentence: a short red jolt, then the letters that weren't typed tumble
    /// away (each with its own pop, spin and delay) while the typed ones dim and sink.
    void AnimateFailure()
    {
        if (restMesh == null || typedWhenFailed == null) return;
        float t = Time.unscaledTime - failedAt;
        TMP_TextInfo info = Text.textInfo;
        float jolt = failJoltTime > 0f ? Mathf.Clamp01(t / failJoltTime) : 1f;
        float shake = Mathf.Sin(t * 90f) * failShake * (1f - jolt);
        Color dim = new Color(untypedColor.r, untypedColor.g, untypedColor.b, correctColor.a);
        float end = failJoltTime + fallStagger + fallTime;

        int n = Mathf.Min(info.characterCount, typedWhenFailed.Length);
        for (int i = 0; i < n; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            int m = c.materialReferenceIndex, v = c.vertexIndex;
            if (m >= restMesh.Length || v + 3 >= restMesh[m].vertices.Length) continue;
            Vector3[] rest = restMesh[m].vertices;
            Vector3 center = (rest[v] + rest[v + 2]) * 0.5f;
            Vector3 position = center + Vector3.right * shake;
            float angle = 0f, alpha = 1f;
            Color color;

            if (typedWhenFailed[i])
            {
                // Typed: drains to grey and sinks away once the jolt is over.
                float s = Mathf.Clamp01((t - failJoltTime) / Mathf.Max(0.01f, fallTime * 0.6f));
                color = Color.Lerp(correctColor, dim, jolt);
                position += Vector3.down * (0.3f * s * s);
                alpha = 1f - s;
            }
            else
            {
                // Not typed: red, then falls.
                color = failColor;
                float ft = t - failJoltTime - fallDelay[i];
                if (ft > 0f)
                {
                    Vector3 vel = fallVelocity[i];
                    position = center + new Vector3(vel.x * ft, vel.y * ft - 0.5f * fallGravity * ft * ft, 0f);
                    angle = fallSpinRate[i] * ft;
                    alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fallTime * 0.55f, fallTime, ft));
                }
            }

            Quaternion turn = Quaternion.Euler(0f, 0f, angle);
            Vector3[] verts = info.meshInfo[m].vertices;
            for (int k = 0; k < 4; k++) verts[v + k] = position + turn * (rest[v + k] - center);
            Color32 c32 = new Color(color.r, color.g, color.b, color.a * alpha);
            Color32[] colors = info.meshInfo[m].colors32;
            for (int k = 0; k < 4; k++) colors[v + k] = c32;
        }
        Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);

        if (backdropMaterial != null)
        {
            // A red flash on the jolt, then the box fades with the letters.
            Color b = Color.Lerp(flashColor, backdropColor, jolt);
            b.a *= 1f - Mathf.Clamp01((t - failJoltTime) / Mathf.Max(0.01f, fallStagger + fallTime * 0.7f));
            backdropMaterial.SetColor("_Color", b);
        }

        if (t >= end) Hide();
    }

    /// Poses and colours every character from its state, starting from the resting
    /// layout each frame (rich text is off, so character i is the sentence's i-th).
    void Animate()
    {
        if (Visual == null || restMesh == null) return;
        float now = Time.unscaledTime;
        Visual.Tick(now);
        float flash = flashDuration > 0f ? Mathf.Clamp01(1f - (now - flashAt) / flashDuration) : 0f;

        TMP_TextInfo info = Text.textInfo;
        int count = Mathf.Min(info.characterCount, Visual.Count);
        int current = typing != null && typing.Session != null ? typing.Session.Index : -1;
        IsBlinkDimmed = BlinkDimmed();
        for (int i = 0; i < count; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            int m = c.materialReferenceIndex, v = c.vertexIndex;
            if (m >= restMesh.Length || v + 3 >= restMesh[m].vertices.Length) continue;

            Visual.Pose(i, now, out Vector2 pose, out float scale);
            float height = info.lineInfo[c.lineNumber].lineHeight;
            Vector3[] rest = restMesh[m].vertices;
            Vector3[] verts = info.meshInfo[m].vertices;
            Vector3 center = (rest[v] + rest[v + 2]) * 0.5f;
            Vector3 shift = new Vector3(pose.x, pose.y, 0f) * height;
            for (int k = 0; k < 4; k++) verts[v + k] = center + (rest[v + k] - center) * scale + shift;

            Color32 color = IsBlinkDimmed && Visual[i] == CharacterState.Untyped ? blinkColor
                : i == current && Visual[i] == CharacterState.Untyped ? currentColor
                : ColorOf(Visual[i], flash);
            Color32[] colors = info.meshInfo[m].colors32;
            for (int k = 0; k < 4; k++) colors[v + k] = color;
        }
        Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);

        if (backdropMaterial != null) backdropMaterial.SetColor("_Color", Color.Lerp(backdropColor, flashColor, flash));
    }

    /// Whether the letters still to type are in the dim half of a blink right now.
    public bool IsBlinkDimmed { get; private set; }

    // In the last seconds the untyped letters blink, faster as time runs out. The phase
    // accumulates (on real time), so the speed-up is smooth.
    bool BlinkDimmed()
    {
        if (typing == null || typing.Session == null || blinkStartTime <= 0f || typing.TimeLeft > blinkStartTime) return false;
        float hz = Mathf.Lerp(blinkFastHz, blinkSlowHz, typing.TimeLeft / blinkStartTime);
        blinkPhase += hz * Time.unscaledDeltaTime;
        return Mathf.Repeat(blinkPhase, 1f) >= 0.5f;
    }

    Color ColorOf(CharacterState state, float flash)
    {
        switch (state)
        {
            case CharacterState.Correct: return correctColor;
            case CharacterState.Error: return errorColor;
            // Without a backdrop the flash shows on the untyped text instead.
            default: return backdrop == null ? Color.Lerp(untypedColor, errorColor, flash * 0.6f) : untypedColor;
        }
    }

    // Placed by the anchor, shifted away from the locked-on enemy (sliding there, or
    // straight there when it first appears), and turned to face the camera (same
    // rotation as the camera, so the text stays upright and flat to the screen).
    void Follow(bool snap = false)
    {
        if (cam == null) cam = Camera.main;
        if (followCamera && cam != null)
        {
            // A child of the camera, so it moves with it (no lag behind camera moves, and
            // steady on screen through shakes). Placed from the camera's own field of
            // view and screen shape, so the spot on screen holds at any size.
            Transform t = root.transform;
            if (t.parent != cam.transform) t.SetParent(cam.transform, false);
            float halfH = cameraDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            t.localPosition = new Vector3((screenPosition.x - 0.5f) * 2f * halfW, (screenPosition.y - 0.5f) * 2f * halfH, cameraDistance);
            t.localRotation = Quaternion.identity;
            return;
        }
        if (root.transform.parent != null) root.transform.SetParent(null, true);

        float goal = 0f;
        CombatTarget target = targeting != null && awayFromTarget != 0f ? targeting.Closest() : null;
        if (target != null && anchor != null)
            goal = -Mathf.Sign(target.transform.position.x - anchor.position.x) * awayFromTarget;
        awayShift = snap ? goal : Mathf.MoveTowards(awayShift, goal, 6f * Time.unscaledDeltaTime);
        if (anchor != null) root.transform.position = anchor.position + offset + Vector3.right * awayShift;
        if (cam != null) root.transform.rotation = cam.transform.rotation;
    }
}
