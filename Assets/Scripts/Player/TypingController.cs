using System;
using UnityEngine;

/// Runs the typing part of the Smite (phase 4: core logic, debug display only).
/// When the input context switches to Typing it takes the next sentence from the
/// SentenceManager and starts a TypingSession. Each typed character is checked
/// against the next expected one: right advances, wrong is a mistake and doesn't.
/// Every key is text here, including the combat keys. Finishing the sentence makes
/// the Smite ready; Escape cancels it. Backspace does nothing (nothing wrong is
/// ever entered, so there's nothing to delete).
/// Typing is timed: Base Time + Time Per Character x the characters to type, kept
/// between Minimum and Maximum Time. The timer runs on real time (unscaled), so the
/// slow motion during typing doesn't stretch it. Running out fails the attempt:
/// back to Normal, with combat input and normal speed restored.
[RequireComponent(typeof(PlayerInputContext))]
public class TypingController : MonoBehaviour
{
    [Tooltip("Cancels typing (and the Smite), back to Normal.")]
    public KeyCode cancelKey = KeyCode.Escape;
    [Tooltip("How typed characters are matched.")]
    public TypingRules rules = new TypingRules();
    [Tooltip("Used if there's no SentenceManager (or it has no sentences).")]
    public string fallbackSentence = "the knight raised his sword";

    [Header("Timer (real seconds: slow motion doesn't affect it)")]
    [Tooltip("Time every sentence gets, before its length is added.")]
    public float baseTime = 1.5f;
    [Tooltip("Extra time for each character to type (spaces count; skipped punctuation doesn't).")]
    public float timePerCharacter = 0.3f;
    public float minimumTime = 3f;
    public float maximumTime = 15f;

    [Header("Slow motion")]
    [Tooltip("Game speed while typing (1 = no slow motion). Typing, its timer and its animations all run on real time.")]
    [Range(0.05f, 1f)] public float slowMotion = 0.15f;

    [Tooltip("Holding a key doesn't type it again and again (the operating system's key repeat is ignored), so a held wrong key is one mistake, not many.")]
    public bool ignoreKeyRepeat = true;

    [Header("Debug")]
    [Tooltip("Log each correct and wrong character, and completion.")]
    public bool logCharacters = true;
    [Tooltip("Show the sentence, progress and timer as debug text in the bottom-left.")]
    public bool showDebugText;
    [Tooltip("Enter finishes the sentence straight away (for testing later phases).")]
    public bool enterSkipsSentence;

    /// The sentence being typed (null when not typing).
    public TypingSession Session { get; private set; }
    /// What's been typed correctly so far.
    public string Typed => Session != null ? Session.Done : "";
    /// Seconds this sentence was given.
    public float TimeLimit { get; private set; }
    /// Real seconds left (stops when the sentence is complete).
    public float TimeLeft { get; private set; }

    /// A new sentence started.
    public event Action<TypingSession> SessionStarted;
    /// A character was right: (character, its index).
    public event Action<char, int> CorrectCharacter;
    /// A character was wrong: (expected, received).
    public event Action<char, char> WrongCharacter;
    /// The whole sentence was typed.
    public event Action SentenceCompleted;
    /// The timer ran out before the sentence was typed.
    public event Action TimedOut;
    /// Typing ended for any reason: finished, timed out, cancelled or interrupted.
    public event Action SessionEnded;

    PlayerInputContext context;
    SmiteController smite;
    SentenceManager sentences;
    float wrongFlashUntil;
    GUIStyle style;

    void Awake()
    {
        context = GetComponent<PlayerInputContext>();
        smite = GetComponent<SmiteController>();
        sentences = GetComponent<SentenceManager>();
    }

    void OnEnable() => context.Changed += OnContextChanged;

    void OnDisable()
    {
        context.Changed -= OnContextChanged;
        bool wasTyping = Session != null;
        End();
        // Nothing would be left to finish the attempt, so don't leave the player stuck typing.
        if (wasTyping && smite != null) smite.Cancel("typing disabled");
    }

    void OnContextChanged(InputContext from, InputContext to)
    {
        if (to == InputContext.Typing) Begin(sentences != null ? sentences.Next() : null);
        else End();
    }

    /// Seconds a sentence gets: Base Time + Time Per Character x characters to type,
    /// clamped to Minimum..Maximum Time.
    public float TimeLimitFor(string sentence)
    {
        int count = 0;
        foreach (char c in sentence ?? "")
            if (!(rules.skipPunctuation && char.IsPunctuation(c))) count++;
        float max = Mathf.Max(minimumTime, maximumTime);
        return Mathf.Clamp(baseTime + count * timePerCharacter, minimumTime, max);
    }

    // Typing is over (finished, failed or cancelled): no session, normal speed.
    void End()
    {
        bool wasTyping = Session != null;
        Session = null;
        GameTime.ClearSlowMotion(this);
        if (wasTyping) SessionEnded?.Invoke();
    }

    /// Starts typing this sentence (the fallback if it's empty).
    public void Begin(string sentence)
    {
        if (string.IsNullOrEmpty(sentence)) sentence = fallbackSentence;
        Session = new TypingSession(sentence, rules);
        Session.Correct += OnCorrect;
        Session.Wrong += OnWrong;
        Session.Completed += OnCompleted;
        TimeLimit = TimeLeft = TimeLimitFor(sentence);
        GameTime.SetSlowMotion(this, slowMotion);
        if (logCharacters) Debug.Log($"[Typing] Sentence: \"{sentence}\" ({TimeLimit:0.0}s)", this);
        SessionStarted?.Invoke(Session);
        // Nothing to type (all punctuation, with Skip Punctuation on).
        if (Session.IsComplete) OnCompleted();
    }

    void Update()
    {
        if (!context.TypingEnabled || Session == null) return;

        if (RawInput.Down(cancelKey))
        {
            if (smite != null) smite.Cancel("typing cancelled");
            return;
        }

        string typed = RawInput.Chars();
        // Auto-repeated characters arrive on frames where no key was freshly pressed.
        if (ignoreKeyRepeat && typed.Length > 0 && !RawInput.AnyKeyDown()) typed = "";
        foreach (char c in typed)
        {
            if (Session == null || Session.IsComplete) return;
            if (c == '\n' || c == '\r')
            {
                if (enterSkipsSentence) OnCompleted();
                return;
            }
            Session.Type(c);
        }

        // After this frame's keys, so a sentence finished on the last frame still counts.
        if (Session == null || Session.IsComplete) return;
        TimeLeft = Mathf.Max(0f, TimeLeft - Time.unscaledDeltaTime);
        if (TimeLeft <= 0f) OnTimedOut();
    }

    void OnTimedOut()
    {
        if (logCharacters) Debug.Log($"[Typing] Time's up: typed {Session.Index}/{Session.Sentence.Length} ({Session.Mistakes} mistake{(Session.Mistakes == 1 ? "" : "s")})", this);
        TimedOut?.Invoke();
        End();
        if (smite != null) smite.Cancel("typing timed out");
    }

    void OnCorrect(char c, int index)
    {
        if (logCharacters) Debug.Log($"[Typing] Correct: {Show(c)}", this);
        CorrectCharacter?.Invoke(c, index);
    }

    void OnWrong(char expected, char received)
    {
        wrongFlashUntil = Time.unscaledTime + 0.25f;
        if (logCharacters) Debug.Log($"[Typing] Wrong: expected {Show(expected)}, received {Show(received)}", this);
        WrongCharacter?.Invoke(expected, received);
    }

    void OnCompleted()
    {
        if (logCharacters) Debug.Log($"[Typing] Sentence complete ({Session.Mistakes} mistake{(Session.Mistakes == 1 ? "" : "s")})", this);
        SentenceCompleted?.Invoke();
        if (smite != null) smite.CompleteSentence();
    }

    static string Show(char c) => c == ' ' ? "space" : c.ToString();

    void OnGUI()
    {
        if (!showDebugText || Session == null || context.Current != InputContext.Typing) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = Color.white } };
        // Typed part green, the next character underlined (red just after a mistake), the rest white.
        string next = Session.IsComplete ? "" : Session.Remaining.Substring(0, 1);
        string rest = Session.IsComplete ? "" : Session.Remaining.Substring(1);
        string nextColor = Time.unscaledTime < wrongFlashUntil ? "#ff5050" : "#ffe680";
        string shownNext = next == " " ? "_" : next;
        string timeColor = TimeLeft < 2f ? "#ff5050" : "#ffffff";
        string text = $"<color={timeColor}>{TimeLeft:0.0}s</color>   <color=#7dff8a>{Esc(Session.Done)}</color><color={nextColor}>{Esc(shownNext)}</color>{Esc(rest)}   <size=13>mistakes {Session.Mistakes}</size>";
        GUI.Label(new Rect(10f, Screen.height - 62f, 900f, 30f), text, style);
    }

    static string Esc(string s) => s.Replace("<", "‹").Replace(">", "›");
}
