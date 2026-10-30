using System;
using UnityEngine;

public enum PlayerState { Normal, SmiteAvailable, Typing, SmiteReady, Smiting }

/// The player's high-level state, for the Smite mechanic. This is the player's only
/// state machine: moment-to-moment combat state (attacking, blocking, guard stun,
/// grounded, dead) stays in the components that own it. PlayerInputContext turns the
/// state into where keyboard input goes.
/// Only these transitions are allowed; any state can fall back to Normal:
///   Normal -> SmiteAvailable -> Typing -> SmiteReady -> Smiting -> Normal
/// Every transition is logged as "[PlayerState] A → B".
public class PlayerStateMachine : MonoBehaviour
{
    [Tooltip("Log every state change to the console.")]
    public bool logTransitions = true;
    [Tooltip("Show the current state in the bottom-left corner of the Game view.")]
    public bool showDebugLabel = true;

    public PlayerState State { get; private set; } = PlayerState.Normal;
    /// Seconds since the current state was entered.
    public float TimeInState => Time.time - enteredAt;
    /// Combat keys are ignored in this state (see PlayerInputContext).
    public bool GameplayInputLocked => PlayerInputContext.For(State) != InputContext.Combat;

    /// Fired on every state change: (from, to).
    public event Action<PlayerState, PlayerState> Changed;

    float enteredAt;
    GUIStyle labelStyle;

    /// Changes state if that transition is allowed. Returns whether it changed.
    public bool TrySetState(PlayerState next, string reason = null)
    {
        if (next == State) return false;
        if (!IsAllowed(State, next))
        {
            if (logTransitions) Debug.LogWarning($"[PlayerState] Ignored {State} → {next} (not allowed){Because(reason)}", this);
            return false;
        }
        PlayerState previous = State;
        State = next;
        enteredAt = Time.time;
        if (logTransitions) Debug.Log($"[PlayerState] {previous} → {next}{Because(reason)}", this);
        Changed?.Invoke(previous, next);
        return true;
    }

    /// Falls back to Normal from any state (a failed or cancelled Smite).
    public void ResetToNormal(string reason = null) => TrySetState(PlayerState.Normal, reason);

    /// Restarts TimeInState without changing state (e.g. another parry refreshing a window).
    public void RestartStateTimer() => enteredAt = Time.time;

    static bool IsAllowed(PlayerState from, PlayerState to)
    {
        if (to == PlayerState.Normal) return true;
        switch (from)
        {
            case PlayerState.Normal: return to == PlayerState.SmiteAvailable;
            case PlayerState.SmiteAvailable: return to == PlayerState.Typing;
            case PlayerState.Typing: return to == PlayerState.SmiteReady;
            case PlayerState.SmiteReady: return to == PlayerState.Smiting;
            default: return false;
        }
    }

    static string Because(string reason) => string.IsNullOrEmpty(reason) ? "" : $"  ({reason})";

    void OnGUI()
    {
        if (!showDebugLabel) return;
        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        string text = State == PlayerState.Normal ? "State: Normal" : $"State: {State}  ({TimeInState:0.0}s)";
        GUI.Label(new Rect(10f, Screen.height - 34f, 400f, 28f), text, labelStyle);
    }
}
