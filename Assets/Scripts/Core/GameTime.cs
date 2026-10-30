using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// The game's time controller: the one owner of Time.timeScale, so slow motion and
/// hit-stop don't undo each other. Nothing else should set Time.timeScale.
/// - Slow motion: any number of owners can ask for a speed; the slowest wins. Each
///   must clear its own request.
/// - Hit-stop: freezes the game (0x) while any owner holds one, then returns to the
///   slow-motion speed (not straight to 1x).
/// With no requests the game runs at 1x. A request whose owner has been destroyed
/// (e.g. its scene unloaded) is dropped, and every scene load clears those out, so
/// nothing can leave the game slowed or frozen for good.
/// While slowed, physics steps shrink by the same factor so physics stays smooth
/// in real time; the original step comes back at 1x.
public static class GameTime
{
    /// Keep physics updating at its usual real-time rate while slowed.
    public static bool scalePhysicsStep = true;

    static readonly Dictionary<object, float> slowRequests = new Dictionary<object, float>();
    static readonly HashSet<object> hitStops = new HashSet<object>();
    static readonly List<object> stale = new List<object>();
    static float baseFixedDeltaTime = 0.02f;

    /// The speed the game runs at when not frozen by hit-stop.
    public static float SlowMotion
    {
        get
        {
            float scale = 1f;
            foreach (float s in slowRequests.Values) scale = Mathf.Min(scale, s);
            return scale;
        }
    }

    public static bool IsHitStopped => hitStops.Count > 0;
    public static bool IsSlowed => slowRequests.Count > 0;

    /// Asks for the game to run at this speed (0..1) until ClearSlowMotion(owner).
    public static void SetSlowMotion(object owner, float scale)
    {
        slowRequests[owner] = Mathf.Clamp(scale, 0.01f, 1f);
        Apply();
    }

    public static void ClearSlowMotion(object owner)
    {
        if (slowRequests.Remove(owner)) Apply();
    }

    /// Freezes the game until EndHitStop(owner).
    public static void BeginHitStop(object owner)
    {
        hitStops.Add(owner);
        Apply();
    }

    public static void EndHitStop(object owner)
    {
        if (hitStops.Remove(owner)) Apply();
    }

    /// Who is slowing or freezing the game right now (for debugging).
    public static string Describe()
    {
        DropStale();
        var parts = new List<string>();
        foreach (var kv in slowRequests) parts.Add($"{Name(kv.Key)} {kv.Value:0.##}x");
        foreach (object o in hitStops) parts.Add($"{Name(o)} hit-stop");
        return $"timeScale {Time.timeScale:0.##}" + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : " (no requests)");
    }

    static string Name(object o) => o is Object u ? (u != null ? u.GetType().Name : "destroyed") : o.ToString();

    static void Apply()
    {
        DropStale();
        float slow = SlowMotion;
        Time.timeScale = hitStops.Count > 0 ? 0f : slow;
        // A hit-stop freeze doesn't step physics anyway, so only the slow-motion part matters.
        Time.fixedDeltaTime = scalePhysicsStep ? baseFixedDeltaTime * slow : baseFixedDeltaTime;
    }

    // Requests from destroyed objects (a Unity object compares equal to null once destroyed).
    static void DropStale()
    {
        stale.Clear();
        foreach (object o in slowRequests.Keys) if (o is Object u && u == null) stale.Add(o);
        foreach (object o in stale) slowRequests.Remove(o);
        stale.Clear();
        foreach (object o in hitStops) if (o is Object u && u == null) stale.Add(o);
        foreach (object o in stale) hitStops.Remove(o);
    }

    // Fresh state each time Play starts (also when domain reload is turned off).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        slowRequests.Clear();
        hitStops.Clear();
        baseFixedDeltaTime = Time.fixedDeltaTime;
        Time.timeScale = 1f;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= OnQuit;
        Application.quitting += OnQuit;
    }

    // Also when Play stops in the editor, so the next session doesn't start from a shrunk physics step.
    static void OnQuit()
    {
        slowRequests.Clear();
        hitStops.Clear();
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
    }

    // Whatever was slowing the old scene is gone now.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();
}
