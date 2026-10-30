using System;
using UnityEngine;

/// Drives the Smite states on the PlayerStateMachine.
/// - Every successful parry (either key) banks a Smite charge, up to Max Charges.
///   Charges don't expire; dying loses them.
/// - With a charge banked the state is SmiteAvailable: Q spends one and starts Typing.
///   Finishing the sentence makes it SmiteReady; failing it (time runs out, Escape,
///   getting hit) loses that charge.
/// - SmiteReady makes the next attack the Smite: the attack key unleashes it (Smiting)
///   instead of a normal swing, and it waits for that attack.
/// - Smiting ends after Smite Duration (a stand-in for the Smite animation, and always
///   a hard limit). With charges left it's straight back to SmiteAvailable.
/// Q is a combat key read through the input context, so while typing it's just a letter.
[RequireComponent(typeof(PlayerStateMachine))]
public class SmiteController : MonoBehaviour
{
    [Tooltip("Spends a charge to start typing.")]
    public KeyCode smiteKey = KeyCode.Q;

    [Header("Charges")]
    [Tooltip("Most parries that can be banked as Smite charges.")]
    public int maxCharges = 3;
    [Tooltip("Dying loses every banked charge.")]
    public bool loseChargesOnDeath = true;

    [Header("Windows (seconds, 0 = no limit)")]
    [Tooltip("Only used without an enabled TypingController (which times each sentence itself): how long typing may take.")]
    public float typingTimeLimit = 10f;
    [Tooltip("Once the sentence is typed, how long the Smite waits for your next attack (0 = until you attack).")]
    public float readyWindow = 0f;
    [Tooltip("How long Smiting lasts. Stands in for the Smite animation until there is one.")]
    public float smiteDuration = 1f;

    [Header("Failure")]
    [Tooltip("Taking damage while typing fails the sentence (and loses its charge).")]
    public bool cancelOnDamage = true;
    [Tooltip("Taking damage while the Smite is ready loses it.")]
    public bool loseReadyOnDamage;

    [Header("Testing")]
    [Tooltip("Play starts with one charge banked, so Q can be pressed straight away.")]
    public bool startWithParry;

    public PlayerState State => states.State;
    /// Banked Smite charges (0..Max Charges).
    public int Charges { get; private set; }
    /// Fired when the number of banked charges changes.
    public event Action<int> ChargesChanged;

    PlayerStateMachine states;
    ParryController defense;
    PlayerHealth health;
    PlayerInputContext inputContext;
    TypingController typing;
    PlayerAttack attack;

    void Awake()
    {
        states = GetComponent<PlayerStateMachine>();
        defense = GetComponent<ParryController>();
        health = GetComponent<PlayerHealth>();
        inputContext = GetComponent<PlayerInputContext>();
        typing = GetComponent<TypingController>();
        attack = GetComponent<PlayerAttack>();
    }

    void Start()
    {
        if (startWithParry) AddCharge("testing: started with a parry");
    }

    void OnEnable()
    {
        if (defense != null) defense.AttackResolved += OnAttackResolved;
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }
    }

    void OnDisable()
    {
        if (defense != null) defense.AttackResolved -= OnAttackResolved;
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
        states.ResetToNormal("Smite disabled");
    }

    void OnAttackResolved(ParryResult result, float sincePress)
    {
        if (result != ParryResult.Parry) return;
        AddCharge($"successful parry ({(defense.LastParryWasAttack ? "sword/shield" : "block key")})");
    }

    /// Banks a charge (if there's room) and makes the Smite available.
    public void AddCharge(string reason)
    {
        if (Charges >= maxCharges)
        {
            if (states.logTransitions) Debug.Log($"[Smite] Charges full ({Charges}/{maxCharges}): {reason} not banked", this);
            return;
        }
        SetCharges(Charges + 1, reason);
        if (states.State == PlayerState.Normal) states.TrySetState(PlayerState.SmiteAvailable, reason);
    }

    void SetCharges(int value, string reason)
    {
        value = Mathf.Clamp(value, 0, Mathf.Max(0, maxCharges));
        if (value == Charges) return;
        if (states.logTransitions) Debug.Log($"[Smite] Charges {Charges} → {value}  ({reason})", this);
        Charges = value;
        ChargesChanged?.Invoke(Charges);
    }

    void OnDamaged(int amount)
    {
        if (states.State == PlayerState.Typing && cancelOnDamage) states.ResetToNormal("took damage");
        else if (states.State == PlayerState.SmiteReady && loseReadyOnDamage) states.ResetToNormal("took damage");
    }

    void OnDied()
    {
        if (loseChargesOnDeath) SetCharges(0, "died");
        states.ResetToNormal("died");
    }

    void Update()
    {
        // Back to Normal with charges left (after a Smite, or a failed sentence): available
        // again. Done here, not inside the state change, so every listener sees the
        // changes in order.
        if (states.State == PlayerState.Normal && Charges > 0) states.TrySetState(PlayerState.SmiteAvailable, $"{Charges} charge(s) banked");
        else if (states.State == PlayerState.SmiteAvailable && Charges <= 0) states.ResetToNormal("no charges");

        if (PlayerInputContext.KeyDown(inputContext, smiteKey)) PressSmite();

        float t = states.TimeInState;
        switch (states.State)
        {
            case PlayerState.Typing:
                if ((typing == null || !typing.isActiveAndEnabled) && Expired(t, typingTimeLimit)) states.ResetToNormal("typing timed out");
                break;
            case PlayerState.SmiteReady:
                if (Expired(t, readyWindow)) states.ResetToNormal("ready window expired");
                break;
            case PlayerState.Smiting:
                // Even once an animation ends it, this stays as a cap so input always comes back.
                if (t >= smiteDuration) FinishSmite();
                break;
        }
    }

    static bool Expired(float elapsed, float limit) => limit > 0f && elapsed >= limit;

    /// What pressing Q does: spend a charge and start typing.
    public void PressSmite()
    {
        if (states.State != PlayerState.SmiteAvailable || Charges <= 0) return;
        SetCharges(Charges - 1, $"{smiteKey} pressed");
        states.TrySetState(PlayerState.Typing, $"{smiteKey} pressed");
    }

    /// Called on the attack key: with the Smite ready, unleashes it instead of a normal
    /// attack and returns true (cutting any swing in progress short).
    public bool TryUnleash()
    {
        if (states.State != PlayerState.SmiteReady) return false;
        if (attack != null && attack.IsAttacking) attack.Interrupt();
        return states.TrySetState(PlayerState.Smiting, "attack pressed");
    }

    /// Called when the sentence is typed correctly (the typing UI will call this).
    public void CompleteSentence() => states.TrySetState(PlayerState.SmiteReady, "sentence completed");

    /// Called when the Smite ends (the Smite animation will call this).
    public void FinishSmite() => states.TrySetState(PlayerState.Normal, "Smite finished");

    /// Abandons the current Smite attempt (banked charges are kept).
    public void Cancel(string reason) => states.ResetToNormal(reason);
}
