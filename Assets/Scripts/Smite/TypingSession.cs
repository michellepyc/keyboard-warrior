using System;

/// What a typed character did.
public enum TypeResult { Correct, Wrong, Ignored }

/// How typed characters are matched against the sentence.
[Serializable]
public class TypingRules
{
    [UnityEngine.Tooltip("Uppercase and lowercase must match. Off: 't' and 'T' are the same.")]
    public bool caseSensitive;
    [UnityEngine.Tooltip("Curly quotes and long dashes in the sentence match the plain keyboard keys (' \" -).")]
    public bool plainQuotesAndDashes = true;
    [UnityEngine.Tooltip("Punctuation in the sentence is filled in automatically, so only letters, numbers and spaces are typed.")]
    public bool skipPunctuation;
}

/// The core of typing one sentence: tracks the next expected character, checks each
/// typed one against it, advances only when it's right, and reports completion.
/// A wrong character is a mistake and doesn't advance; what's already typed stays
/// typed. Plain C# with no Unity dependency, so it can be tested anywhere.
public class TypingSession
{
    public string Sentence { get; }
    public TypingRules Rules { get; }
    /// Index of the next character to type (Sentence.Length once complete).
    public int Index { get; private set; }
    public int Mistakes { get; private set; }
    public bool IsComplete => Index >= Sentence.Length;
    /// The part already typed.
    public string Done => Sentence.Substring(0, Index);
    /// The part still to type.
    public string Remaining => Sentence.Substring(Index);
    /// The next character to type, or '\0' once complete.
    public char Expected => IsComplete ? '\0' : Sentence[Index];
    /// 0..1, how much of the sentence is typed.
    public float Progress => Sentence.Length == 0 ? 1f : Index / (float)Sentence.Length;

    /// A character was right: (character, its index).
    public event Action<char, int> Correct;
    /// A character was wrong: (expected, received).
    public event Action<char, char> Wrong;
    /// The last character was typed.
    public event Action Completed;

    public TypingSession(string sentence, TypingRules rules = null)
    {
        Sentence = sentence ?? "";
        Rules = rules ?? new TypingRules();
        SkipPunctuation();
    }

    /// Feeds one typed character.
    public TypeResult Type(char received)
    {
        if (IsComplete || char.IsControl(received)) return TypeResult.Ignored;
        char expected = Sentence[Index];
        if (!Matches(expected, received))
        {
            Mistakes++;
            Wrong?.Invoke(expected, received);
            return TypeResult.Wrong;
        }
        Index++;
        Correct?.Invoke(expected, Index - 1);
        SkipPunctuation();
        if (IsComplete) Completed?.Invoke();
        return TypeResult.Correct;
    }

    /// Feeds several characters in order, stopping once the sentence is complete.
    public void Type(string received)
    {
        foreach (char c in received)
        {
            if (IsComplete) return;
            Type(c);
        }
    }

    public bool Matches(char expected, char received)
    {
        if (Rules.plainQuotesAndDashes)
        {
            expected = Plain(expected);
            received = Plain(received);
        }
        if (char.IsWhiteSpace(expected)) return char.IsWhiteSpace(received);
        return Rules.caseSensitive ? expected == received
            : char.ToLowerInvariant(expected) == char.ToLowerInvariant(received);
    }

    // Completion is reported by the keystroke that got there (Type), not here.
    void SkipPunctuation()
    {
        if (!Rules.skipPunctuation) return;
        while (!IsComplete && char.IsPunctuation(Sentence[Index])) Index++;
    }

    static char Plain(char c)
    {
        switch (c)
        {
            case '‘': case '’': case '‛': case '′': return '\'';
            case '“': case '”': case '‟': case '″': return '"';
            case '–': case '—': case '−': return '-';
            case ' ': return ' ';
            default: return c;
        }
    }
}
