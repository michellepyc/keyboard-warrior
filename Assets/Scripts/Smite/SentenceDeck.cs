using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// A shuffled deck of sentences: each draw takes the next card, and no sentence
/// comes up again until every one has been drawn. Then the deck is refilled and
/// reshuffled. A reshuffle never puts the last sentence drawn on top, so the same
/// one doesn't come up twice in a row across the boundary (with 2+ sentences).
/// Plain C# with no Unity dependency, so it can be used and tested anywhere.
public class SentenceDeck
{
    readonly List<string> all = new List<string>();
    readonly List<string> pile = new List<string>();
    readonly Random random;
    string lastDrawn;

    /// Every sentence in the deck.
    public IReadOnlyList<string> All => all;
    /// How many sentences there are.
    public int Count => all.Count;
    /// Sentences left before the next reshuffle.
    public int Remaining => pile.Count;
    /// How many times the deck has been shuffled (including the first).
    public int Shuffles { get; private set; }

    /// Fired after each shuffle (the first one too).
    public event Action Reshuffled;

    /// seed: the same seed gives the same order every time (null = random).
    public SentenceDeck(IEnumerable<string> sentences, int? seed = null)
    {
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        foreach (string s in sentences)
            if (!string.IsNullOrEmpty(s)) all.Add(s);
    }

    /// One sentence per line. Blank lines and lines starting with # are skipped;
    /// each line is trimmed and runs of spaces or tabs become one space.
    public static List<string> Parse(string text)
    {
        var sentences = new List<string>();
        if (string.IsNullOrEmpty(text)) return sentences;
        foreach (string raw in text.Split('\n'))
        {
            string line = Regex.Replace(raw.Trim(), @"\s+", " ");
            if (line.Length == 0 || line.StartsWith("#")) continue;
            sentences.Add(line);
        }
        return sentences;
    }

    /// The next sentence, reshuffling first if the deck is used up. Null if it's empty.
    public string Next()
    {
        if (all.Count == 0) return null;
        if (pile.Count == 0) Refill();
        string s = pile[pile.Count - 1];
        pile.RemoveAt(pile.Count - 1);
        lastDrawn = s;
        return s;
    }

    /// Puts every sentence back and shuffles, discarding the rest of the current pass.
    public void Reset()
    {
        pile.Clear();
        Refill();
    }

    void Refill()
    {
        pile.Clear();
        pile.AddRange(all);
        // Fisher-Yates.
        for (int i = pile.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (pile[i], pile[j]) = (pile[j], pile[i]);
        }
        // Draws come off the end; keep the one just drawn from coming straight back.
        int top = pile.Count - 1;
        if (pile.Count > 1 && pile[top] == lastDrawn)
        {
            int swap = random.Next(top);
            (pile[top], pile[swap]) = (pile[swap], pile[top]);
        }
        Shuffles++;
        Reshuffled?.Invoke();
    }
}
