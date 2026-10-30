using System;
using System.Collections.Generic;
using UnityEngine;

public enum InputContext { Combat, Typing, None }

/// Where the player's keyboard goes right now, like an input action map (the project
/// uses the old Input Manager, which has none). It follows the PlayerStateMachine:
///   Combat (Normal, SmiteAvailable, SmiteReady): move, jump, crouch, attack, block, Smite.
///   Typing (Typing): characters go to the TypingController; combat keys read as up.
///   None (Smiting): nothing.
/// Gameplay scripts read their keys through Key / KeyDown / KeyUp instead of Input.
/// Keys still held when Combat comes back (a letter held as the sentence finished)
/// are ignored until released, so nothing fires by itself. The context is worked out
/// from the state every time, and anything missing or disabled means Combat, so input
/// can't stay switched off.
[DefaultExecutionOrder(-50)]
public class PlayerInputContext : MonoBehaviour
{
    [Tooltip("Log every context change to the console.")]
    public bool logChanges = true;

    public InputContext Current => states != null && isActiveAndEnabled ? For(states.State) : InputContext.Combat;
    /// Typing input is live (not on the frame typing started, so the key that opened it isn't typed).
    public bool TypingEnabled => Current == InputContext.Typing && Time.frameCount != switchedFrame;

    /// Fired when the context changes: (from, to).
    public event Action<InputContext, InputContext> Changed;

    public static InputContext For(PlayerState state) =>
        state == PlayerState.Typing ? InputContext.Typing
        : state == PlayerState.Smiting ? InputContext.None
        : InputContext.Combat;

    PlayerStateMachine states;
    readonly HashSet<KeyCode> seen = new HashSet<KeyCode>();
    readonly HashSet<KeyCode> heldOver = new HashSet<KeyCode>();
    int switchedFrame = -1;

    void Awake() => states = GetComponent<PlayerStateMachine>();

    void OnEnable()
    {
        if (states != null) states.Changed += OnStateChanged;
    }

    void OnDisable()
    {
        if (states != null) states.Changed -= OnStateChanged;
        heldOver.Clear();
    }

    // Runs before the gameplay scripts, so a released key is usable again the same frame.
    void Update() => heldOver.RemoveWhere(k => !RawInput.Held(k));

    void OnStateChanged(PlayerState from, PlayerState to)
    {
        InputContext a = For(from), b = For(to);
        if (a == b) return;
        switchedFrame = Time.frameCount;
        heldOver.Clear();
        if (b == InputContext.Combat)
            foreach (KeyCode k in seen)
                if (RawInput.Held(k)) heldOver.Add(k);
        if (logChanges)
        {
            string ignoring = heldOver.Count > 0 ? $"  (ignoring held {string.Join(", ", heldOver)} until released)" : "";
            Debug.Log($"[Input] {a} → {b}{ignoring}", this);
        }
        Changed?.Invoke(a, b);
    }

    bool Live(KeyCode k)
    {
        seen.Add(k);
        return Current == InputContext.Combat && !heldOver.Contains(k);
    }

    /// Combat key held.
    public bool GetKey(KeyCode k) => Live(k) && RawInput.Held(k);
    /// Combat key pressed this frame.
    public bool GetKeyDown(KeyCode k) => Live(k) && RawInput.Down(k);
    /// Combat key released this frame.
    public bool GetKeyUp(KeyCode k) => Live(k) && RawInput.Up(k);

    // For scripts that may run without a context (it then behaves as Combat).
    public static bool Key(PlayerInputContext context, KeyCode k) => context != null ? context.GetKey(k) : RawInput.Held(k);
    public static bool KeyDown(PlayerInputContext context, KeyCode k) => context != null ? context.GetKeyDown(k) : RawInput.Down(k);
    public static bool KeyUp(PlayerInputContext context, KeyCode k) => context != null ? context.GetKeyUp(k) : RawInput.Up(k);
}

/// The raw keyboard, read by PlayerInputContext and TypingController. Tests (or a
/// replay) can swap these out to press keys and type text; Reset puts the real
/// keyboard back.
public static class RawInput
{
    public static Func<KeyCode, bool> Held = Input.GetKey;
    public static Func<KeyCode, bool> Down = Input.GetKeyDown;
    public static Func<KeyCode, bool> Up = Input.GetKeyUp;
    /// Characters typed this frame (backspace is '\b', enter is '\n' or '\r').
    /// Includes the operating system's auto-repeat while a key is held.
    public static Func<string> Chars = () => Input.inputString;
    /// Whether any key was freshly pressed this frame (auto-repeat doesn't count).
    public static Func<bool> AnyKeyDown = () => Input.anyKeyDown;

    public static void Reset()
    {
        Held = Input.GetKey;
        Down = Input.GetKeyDown;
        Up = Input.GetKeyUp;
        Chars = () => Input.inputString;
        AnyKeyDown = () => Input.anyKeyDown;
    }
}
