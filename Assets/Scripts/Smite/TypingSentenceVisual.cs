using System;
using UnityEngine;

public enum CharacterState { Untyped, Correct, Error }

/// The sentence on screen as a row of characters, each with its own visual state
/// and animation, independent of how it's drawn (TypingDisplay draws it):
///   Untyped: waiting to be typed.
///   Correct: typed; it bounces once and then stays bright. Never goes back.
///   Error: the wrong key was pressed on it; it shakes, then returns to Untyped.
/// Times are whatever clock the caller passes (TypingDisplay uses real time, so slow
/// motion doesn't slow the animations).
public class TypingSentenceVisual
{
    [Serializable]
    public class Motion
    {
        [Tooltip("How high a correct character hops, as a fraction of its height.")]
        public float bounceHeight = 0.35f;
        [Tooltip("How big it grows at the top of the hop (1 = no change).")]
        public float bounceScale = 1.25f;
        public float bounceDuration = 0.22f;
        [Tooltip("How far a wrong character shakes side to side, as a fraction of its height.")]
        public float shakeDistance = 0.12f;
        public float shakeDuration = 0.3f;
        [Tooltip("Shakes per second.")]
        public float shakeFrequency = 30f;
    }

    struct Character
    {
        public CharacterState state;
        public float since;
    }

    readonly Character[] characters;
    readonly Motion motion;

    public int Count => characters.Length;
    public CharacterState this[int i] => characters[i].state;

    public TypingSentenceVisual(int count, Motion motion)
    {
        characters = new Character[Mathf.Max(0, count)];
        this.motion = motion ?? new Motion();
    }

    /// Typed correctly: bounces, then stays Correct.
    public void MarkCorrect(int i, float now)
    {
        if (!Valid(i) || characters[i].state == CharacterState.Correct) return;
        characters[i] = new Character { state = CharacterState.Correct, since = now };
    }

    /// Wrong key on this character: shakes (again, if it already was). Correct ones are left alone.
    public void MarkError(int i, float now)
    {
        if (!Valid(i) || characters[i].state == CharacterState.Correct) return;
        characters[i] = new Character { state = CharacterState.Error, since = now };
    }

    /// Ends errors whose shake is over (back to Untyped).
    public void Tick(float now)
    {
        for (int i = 0; i < characters.Length; i++)
            if (characters[i].state == CharacterState.Error && now - characters[i].since >= motion.shakeDuration)
                characters[i] = new Character { state = CharacterState.Untyped, since = now };
    }

    /// Where character i is drawn right now: offset (in multiples of its height) and scale.
    public void Pose(int i, float now, out Vector2 offset, out float scale)
    {
        offset = Vector2.zero;
        scale = 1f;
        if (!Valid(i)) return;
        float t = now - characters[i].since;
        switch (characters[i].state)
        {
            case CharacterState.Correct:
                if (t < motion.bounceDuration)
                {
                    float hop = Mathf.Sin(Mathf.PI * t / motion.bounceDuration);
                    offset.y = hop * motion.bounceHeight;
                    scale = 1f + (motion.bounceScale - 1f) * hop;
                }
                break;
            case CharacterState.Error:
                if (t < motion.shakeDuration)
                {
                    float fade = 1f - t / motion.shakeDuration;
                    offset.x = Mathf.Sin(t * motion.shakeFrequency * Mathf.PI * 2f) * motion.shakeDistance * fade;
                }
                break;
        }
    }

    /// Whether character i is still animating.
    public bool IsAnimating(int i, float now)
    {
        if (!Valid(i)) return false;
        float t = now - characters[i].since;
        return characters[i].state == CharacterState.Correct ? t < motion.bounceDuration
            : characters[i].state == CharacterState.Error && t < motion.shakeDuration;
    }

    bool Valid(int i) => i >= 0 && i < characters.Length;
}
