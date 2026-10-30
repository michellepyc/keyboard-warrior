using UnityEngine;

/// Owns what the player looks like: the placeholder capsule until a model is
/// placed under ModelRoot, then the model's renderers. Other scripts go through
/// this for blinking and state tints so they work either way.
public class PlayerVisuals : MonoBehaviour
{
    [Tooltip("Put the character model in here. Feet at this object's origin, facing its +Z.")]
    public Transform modelRoot;
    [Tooltip("Capsule shown until a model is added. Hidden at runtime once one is.")]
    public Renderer placeholder;

    [Header("Staging")]
    [Tooltip("Turns the model this far toward the camera from pure side-on, like stage blocking. 0 faces fully left/right. Visual only; gameplay always faces straight left/right.")]
    [Range(0f, 90f)] public float cheatAngle = 0f;
    [Tooltip("How fast the model turns around when changing direction, in degrees per second. 0 snaps.")]
    public float turnSpeed = 1080f;
    [Tooltip("While blocking, turns the model this far so its shield (left) side leads. 0 faces fully left/right, 90 is fully shield-first.")]
    [Range(0f, 90f)] public float blockTurnAngle = 0f;

    public bool HasModel { get; private set; }
    public Animator Animator { get; private set; }

    PlayerMovement movement;
    ParryController defense;
    PlayerClock clock;
    Renderer[] renderers;
    float yaw = float.NaN;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        defense = GetComponent<ParryController>();
        clock = GetComponent<PlayerClock>();
        HasModel = modelRoot != null && modelRoot.childCount > 0;
        if (placeholder != null) placeholder.gameObject.SetActive(!HasModel);

        if (HasModel)
        {
            renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            Animator = modelRoot.GetComponentInChildren<Animator>();
            // Movement is code-driven; clips shouldn't move the character.
            if (Animator != null) Animator.applyRootMotion = false;
        }
        else
        {
            renderers = placeholder != null ? new[] { placeholder } : new Renderer[0];
        }
    }

    void LateUpdate()
    {
        if (modelRoot == null || movement == null) return;

        // World yaw 90 faces +X and 270 faces -X; the camera sits on -Z (yaw 180),
        // so any cheat angle tilts each side toward 180. Stepping the raw angle
        // (not the shortest rotation) keeps it between 90 and 270, so turning
        // around always swings through the character's front, never its back.
        float targetYaw = movement.Facing > 0 ? 90f + cheatAngle : 270f - cheatAngle;
        // Blocking leads with the shield arm. Facing right that turns the chest toward
        // the camera; facing left it turns the back toward it, since the shield arm is
        // on the camera side then. Either way the angle stays in 90..360, so the
        // turnaround path above still swings through the front.
        if (defense != null && defense.IsBlocking) targetYaw += blockTurnAngle;
        yaw = float.IsNaN(yaw) || turnSpeed <= 0f
            ? targetYaw
            : Mathf.MoveTowards(yaw, targetYaw, turnSpeed * PlayerClock.DtOf(clock));
        modelRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public void SetVisible(bool visible)
    {
        foreach (Renderer r in renderers) r.enabled = visible;
    }

    /// State colors are for the placeholder only; a real model keeps its materials.
    public void SetTint(Color color)
    {
        if (!HasModel && placeholder != null) placeholder.material.color = color;
    }
}
