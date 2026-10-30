using UnityEngine;

/// A bar along the bottom of the screen showing how much time is left to type the
/// Smite sentence. It drains from the right; gold turning orange, then red in the
/// last seconds, when it pulses in step with the blinking letters. It slides in when
/// typing starts and, when typing ends, flashes gold (finished) or red (failed) and
/// fades out. Runs on real time, so the typing slow motion doesn't slow it.
public class TypingTimeBar : MonoBehaviour
{
    public TypingController typing;
    [Tooltip("For the blink timing (found automatically).")]
    public TypingDisplay display;

    [Header("Layout (pixels)")]
    public float height = 10f;
    [Tooltip("Gap from the bottom edge of the screen.")]
    public float bottomMargin = 14f;
    [Tooltip("Gap from the left and right edges.")]
    public float sideMargin = 16f;

    [Header("Colours")]
    public Color fullColor = new Color(1f, 0.82f, 0.3f, 1f);
    public Color midColor = new Color(1f, 0.55f, 0.15f, 1f);
    public Color lowColor = new Color(1f, 0.2f, 0.15f, 1f);
    public Color trackColor = new Color(0f, 0f, 0f, 0.5f);
    public Color successColor = new Color(1f, 0.92f, 0.55f, 1f);
    public Color failColor = new Color(1f, 0.15f, 0.1f, 1f);

    [Header("Timing (real seconds)")]
    public float slideInTime = 0.15f;
    [Tooltip("How long the bar flashes and fades after typing ends.")]
    public float endTime = 0.4f;

    /// How full the bar is right now (0..1); for tests.
    public float Fill { get; private set; }
    /// Whether it's drawn at all right now.
    public bool IsVisible => appear > 0f;

    Texture2D white;
    float appear;            // 0..1 slide-in
    float startedAt, endedAt = float.NegativeInfinity;
    bool typingNow, ended, succeeded;

    void Awake()
    {
        if (typing == null) typing = FindObjectOfType<TypingController>();
        if (display == null) display = FindObjectOfType<TypingDisplay>();
        white = new Texture2D(1, 1) { name = "TypingTimeBar (runtime)" };
        white.SetPixel(0, 0, Color.white);
        white.Apply();
    }

    void OnDestroy()
    {
        if (white != null) Destroy(white);
    }

    void OnEnable()
    {
        if (typing == null) return;
        typing.SessionStarted += OnStarted;
        typing.SentenceCompleted += OnCompleted;
        typing.SessionEnded += OnEnded;
    }

    void OnDisable()
    {
        if (typing == null) return;
        typing.SessionStarted -= OnStarted;
        typing.SentenceCompleted -= OnCompleted;
        typing.SessionEnded -= OnEnded;
    }

    void OnStarted(TypingSession session)
    {
        typingNow = true;
        ended = succeeded = false;
        startedAt = Time.unscaledTime;
    }

    void OnCompleted() => succeeded = true;

    void OnEnded()
    {
        if (!typingNow) return;
        typingNow = false;
        ended = true;
        endedAt = Time.unscaledTime;
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (typingNow)
        {
            appear = slideInTime > 0f ? Mathf.Clamp01((now - startedAt) / slideInTime) : 1f;
            if (typing.Session != null && typing.TimeLimit > 0f) Fill = Mathf.Clamp01(typing.TimeLeft / typing.TimeLimit);
        }
        else if (ended)
        {
            appear = endTime > 0f ? Mathf.Clamp01(1f - (now - endedAt) / endTime) : 0f;
            if (appear <= 0f) ended = false;
        }
        else appear = 0f;
    }

    void OnGUI()
    {
        if (appear <= 0f || white == null) return;
        float w = Screen.width - sideMargin * 2f;
        // Slides up from below the screen edge as it appears.
        float y = Screen.height - bottomMargin - height + (1f - EaseOut(appear)) * (height + bottomMargin);

        Color bar;
        float alpha = 1f;
        if (ended)
        {
            // A quick flash in the result colour, then the fade (appear) takes it away.
            bar = succeeded ? successColor : failColor;
            alpha = appear;
        }
        else
        {
            float blinkFrom = display != null ? display.blinkStartTime : 3f;
            float left = typing != null ? typing.TimeLeft : 0f;
            bar = Fill > 0.5f ? Color.Lerp(midColor, fullColor, (Fill - 0.5f) * 2f) : Color.Lerp(lowColor, midColor, Fill * 2f);
            if (left <= blinkFrom)
            {
                bar = lowColor;
                // Pulse with the letters' blink.
                if (display != null && display.IsBlinkDimmed) bar = Color.Lerp(lowColor, Color.white, 0.45f);
            }
        }

        Color old = GUI.color;
        GUI.color = new Color(trackColor.r, trackColor.g, trackColor.b, trackColor.a * alpha);
        GUI.DrawTexture(new Rect(sideMargin, y, w, height), white);
        float fill = ended ? (succeeded ? 1f : Fill) : Fill;
        GUI.color = new Color(bar.r, bar.g, bar.b, bar.a * alpha);
        GUI.DrawTexture(new Rect(sideMargin, y, w * fill, height), white);
        GUI.color = old;
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
}
