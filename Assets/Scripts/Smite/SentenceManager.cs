using UnityEngine;

/// Hands out the sentences for the Smite, one at a time, from a shuffled deck (see
/// SentenceDeck): no sentence repeats until all of them have been used, then the
/// deck reshuffles. The sentences come from a text file with one per line, by
/// default Assets/Data/Smite/Sentences.txt. Blank lines and lines starting with #
/// are skipped. Independent of the typing UI.
public class SentenceManager : MonoBehaviour
{
    [Tooltip("Text file with one sentence per line.")]
    public TextAsset sentenceFile;
    [Tooltip("Log each sentence handed out and each reshuffle.")]
    public bool logDraws = true;
    [Tooltip("Use a fixed shuffle order (for testing). 0 = different every time.")]
    public int seed;

    /// Sentences left before the next reshuffle.
    public int Remaining => deck != null ? deck.Remaining : 0;
    /// How many sentences were loaded.
    public int Count => deck != null ? deck.Count : 0;
    /// The deck itself (null until loaded).
    public SentenceDeck Deck => deck;

    SentenceDeck deck;

    void Awake() => Load();

    /// (Re)loads the file and starts a fresh shuffle.
    public void Load()
    {
        if (sentenceFile == null)
        {
            Debug.LogWarning("[Sentences] No sentence file assigned.", this);
            deck = new SentenceDeck(new string[0]);
            return;
        }
        Load(sentenceFile.text);
    }

    /// Loads sentences from text (one per line) instead of the file.
    public void Load(string text)
    {
        deck = new SentenceDeck(SentenceDeck.Parse(text), seed != 0 ? seed : (int?)null);
        deck.Reshuffled += () =>
        {
            if (logDraws) Debug.Log($"[Sentences] Shuffled {deck.Count} sentences (shuffle #{deck.Shuffles})", this);
        };
        if (deck.Count == 0) Debug.LogWarning($"[Sentences] {(sentenceFile != null ? sentenceFile.name : "The text")} has no sentences.", this);
        else if (logDraws) Debug.Log($"[Sentences] Loaded {deck.Count} sentences", this);
    }

    /// The next sentence. Null only if there are none.
    public string Next()
    {
        if (deck == null) Load();
        string s = deck.Next();
        if (logDraws && s != null) Debug.Log($"[Sentences] \"{s}\" ({deck.Remaining} left this pass)", this);
        return s;
    }
}
