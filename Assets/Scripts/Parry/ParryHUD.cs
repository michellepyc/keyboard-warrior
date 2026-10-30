using UnityEngine;

/// Bare-bones IMGUI readout: HP (top left), banked Smite charges and the combo counter
/// (top right), and the last parry result with its timing (centre).
public class ParryHUD : MonoBehaviour
{
    public ParryController player;
    public PlayerHealth playerHealth;
    public float messageHold = 0.6f;
    public float messageFade = 0.3f;
    [Tooltip("Hits landed within this long of each other count as one combo.")]
    public float comboHitGap = 1.2f;
    [Tooltip("How long the combo counter stays up after the last hit.")]
    public float comboShowTime = 1f;

    int parries, blocks, hitsTaken, whiffs, strikes;
    int comboHits;
    float lastComboHitTime = float.NegativeInfinity;
    PlayerAttack attack;
    SmiteController smite;
    string headline = "";
    string detail = "";
    Color headlineColor = Color.white;
    float shownAt = float.NegativeInfinity;
    GUIStyle bigStyle, smallStyle, statsStyle, countStyle, comboStyle;

    void Awake()
    {
        if (player == null) player = FindObjectOfType<ParryController>();
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
        if (player != null) attack = player.GetComponent<PlayerAttack>();
        if (player != null) smite = player.GetComponent<SmiteController>();
    }

    void OnEnable()
    {
        player.AttackResolved += OnAttackResolved;
        player.Whiffed += OnWhiffed;
        player.LatePress += OnLatePress;
        if (playerHealth != null) playerHealth.Died += OnDied;
        CombatTarget.AnyDamaged += OnEnemyDamaged;
        if (attack != null) attack.HitLanded += OnHitLanded;
    }

    void OnDisable()
    {
        player.AttackResolved -= OnAttackResolved;
        player.Whiffed -= OnWhiffed;
        player.LatePress -= OnLatePress;
        if (playerHealth != null) playerHealth.Died -= OnDied;
        CombatTarget.AnyDamaged -= OnEnemyDamaged;
        if (attack != null) attack.HitLanded -= OnHitLanded;
    }

    void OnAttackResolved(ParryResult result, float sincePress)
    {
        switch (result)
        {
            case ParryResult.Parry:
                parries++;
                string input = player.LastParryWasAttack ? "swing" : "press";
                Show("PARRY", new Color(0.4f, 0.8f, 1f), $"{Ms(sincePress)} after {input}");
                break;
            case ParryResult.Blocked:
                blocks++;
                Show("GUARD STUN", new Color(0.7f, 0.75f, 0.85f), "Blocking leaves you open. Tap K or J just before impact to parry");
                break;
            default:
                hitsTaken++;
                float early = sincePress - player.parryWindow;
                Show("HIT", new Color(1f, 0.3f, 0.3f), early < 0.5f ? $"Early by {Ms(early)}" : "No parry");
                break;
        }
    }

    void OnWhiffed()
    {
        whiffs++;
        Show("whiff", new Color(0.7f, 0.7f, 0.7f), "");
    }

    void OnLatePress(float late)
    {
        detail = $"Late by {Ms(late)}";
        shownAt = Time.unscaledTime;
    }

    void OnDied()
    {
        parries = 0;
        Show("DOWN", new Color(1f, 0.3f, 0.3f), "Respawning...");
    }

    void OnEnemyDamaged(CombatTarget enemy, bool staggered) => strikes++;

    void OnHitLanded(IHittable target)
    {
        comboHits = Time.unscaledTime - lastComboHitTime <= comboHitGap ? comboHits + 1 : 1;
        lastComboHitTime = Time.unscaledTime;
    }

    void Show(string text, Color color, string info)
    {
        headline = text;
        headlineColor = color;
        detail = info;
        shownAt = Time.unscaledTime;
    }

    void OnGUI()
    {
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            statsStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            countStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            comboStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
        }

        string hp = playerHealth != null ? $"HP {playerHealth.Health}/{playerHealth.maxHealth}" : "";
        // Top left: just health.
        GUI.Label(new Rect(16, 12, 400, 30), hp, statsStyle);
        // Top right: parries banked as Smite charges (what Q can spend), not the lifetime total.
        string counter = smite != null ? $"Parries {smite.Charges}/{smite.maxCharges}" : $"Parries {parries}";
        GUI.Label(new Rect(Screen.width - 316, 12, 300, 44), counter, countStyle);

        float sinceCombo = Time.unscaledTime - lastComboHitTime;
        if (comboHits >= 2 && sinceCombo < comboShowTime)
        {
            GUI.color = new Color(1f, 0.85f, 0.4f, Mathf.Clamp01((comboShowTime - sinceCombo) / 0.3f));
            GUI.Label(new Rect(Screen.width - 316, 56, 300, 52), $"{comboHits} HITS", comboStyle);
            GUI.color = Color.white;
        }

        float age = Time.unscaledTime - shownAt;
        float alpha = Mathf.Clamp01(1f - (age - messageHold) / messageFade);
        if (alpha <= 0f) return;

        float y = Screen.height * 0.2f;
        GUI.color = new Color(headlineColor.r, headlineColor.g, headlineColor.b, alpha);
        GUI.Label(new Rect(0, y, Screen.width, 70), headline, bigStyle);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, y + 70, Screen.width, 32), detail, smallStyle);
        GUI.color = Color.white;
    }

    static string Ms(float seconds) => $"{Mathf.RoundToInt(seconds * 1000f)} ms";
}
