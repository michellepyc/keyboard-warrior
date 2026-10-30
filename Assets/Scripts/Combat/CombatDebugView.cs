using UnityEngine;

/// Draws the combat boxes in the Game view (and Scene view), on top of everything:
/// - Player attack hitboxes (the padded sword and/or shield): dim yellow while swinging, bright
///   yellow during the hit frames, green briefly after it lands a hit.
/// - Sword/shield parry colliders (only with Show Guard Colliders): cyan, bright while a
///   strike on them would be parried.
/// - Enemy strike hitboxes (EnemyHitbox): red while open.
/// Toggle with toggleKey.
public class CombatDebugView : MonoBehaviour
{
    public bool show = true;
    public KeyCode toggleKey = KeyCode.F1;
    [Tooltip("Also draw the sword/shield parry colliders (cyan).")]
    public bool showGuardColliders;
    [Tooltip("How long the attack hitbox stays green after landing a hit.")]
    public float landedFlashTime = 0.25f;

    public PlayerAttack attack;
    public ParryController defense;

    static readonly Color SwingColor = new Color(1f, 0.9f, 0.2f, 0.12f);
    static readonly Color ActiveColor = new Color(1f, 0.9f, 0.2f, 0.45f);
    static readonly Color LandedColor = new Color(0.2f, 1f, 0.3f, 0.55f);
    static readonly Color GuardColor = new Color(0.2f, 0.9f, 1f, 0.15f);
    static readonly Color GuardActiveColor = new Color(0.2f, 0.9f, 1f, 0.5f);
    static readonly Color StrikeColor = new Color(1f, 0.15f, 0.1f, 0.4f);

    Material material;
    readonly System.Collections.Generic.List<BoxCollider> hitboxes = new System.Collections.Generic.List<BoxCollider>();
    float landedUntil = float.NegativeInfinity;

    void Awake()
    {
        if (attack == null) attack = FindObjectOfType<PlayerAttack>();
        if (defense == null && attack != null) defense = attack.GetComponent<ParryController>();
    }

    void OnEnable()
    {
        if (attack != null) attack.HitLanded += OnHitLanded;
    }

    void OnDisable()
    {
        if (attack != null) attack.HitLanded -= OnHitLanded;
    }

    void OnHitLanded(IHittable target) => landedUntil = Time.unscaledTime + landedFlashTime;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) show = !show;
    }

    void OnRenderObject()
    {
        if (!show || !Application.isPlaying) return;
        if (!EnsureMaterial()) return;
        material.SetPass(0);

        if (attack != null)
        {
            Color c = Time.unscaledTime < landedUntil ? LandedColor
                : attack.IsHitActive ? ActiveColor
                : attack.IsAttacking ? SwingColor
                : Color.clear;
            if (c.a > 0f)
            {
                attack.CurrentHitboxes(hitboxes);
                foreach (BoxCollider box in hitboxes)
                    if (attack.GetHitbox(box, out Vector3 center, out Quaternion rotation, out Vector3 size))
                        DrawBox(Matrix4x4.TRS(center, rotation, size), c);
            }
        }

        if (showGuardColliders && defense != null && defense.GuardColliders != null)
        {
            Color c = defense.CanGuardParry ? GuardActiveColor : GuardColor;
            foreach (Collider col in defense.GuardColliders)
            {
                if (col is BoxCollider box && box.enabled && box.gameObject.activeInHierarchy)
                {
                    // Include the padding the parry check adds around them.
                    Transform t = box.transform;
                    Vector3 s = t.lossyScale;
                    Vector3 size = new Vector3(Mathf.Abs(s.x * box.size.x), Mathf.Abs(s.y * box.size.y), Mathf.Abs(s.z * box.size.z))
                        + Vector3.one * (defense.guardPadding * 2f);
                    DrawBox(Matrix4x4.TRS(t.TransformPoint(box.center), t.rotation, size), c);
                }
            }
        }

        foreach (EnemyHitbox strike in EnemyHitbox.Active)
        {
            strike.GetWorldBox(out Vector3 center, out Quaternion rotation, out Vector3 size);
            DrawBox(Matrix4x4.TRS(center, rotation, size), StrikeColor);
        }
    }

    bool EnsureMaterial()
    {
        if (material != null) return true;
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null) return false;
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 0);
        // Always on top, so boxes inside the character still show.
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        return true;
    }

    static readonly Vector3[] Corners =
    {
        new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
        new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
        new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
        new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
    };
    static readonly int[] Faces = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 5, 4, 3, 2, 6, 7, 0, 3, 7, 4, 1, 2, 6, 5 };
    static readonly int[] Edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

    /// A unit cube transformed by m: translucent faces plus a solid outline.
    static void DrawBox(Matrix4x4 m, Color color)
    {
        GL.PushMatrix();
        GL.MultMatrix(m);
        GL.Begin(GL.QUADS);
        GL.Color(color);
        foreach (int i in Faces) GL.Vertex(Corners[i]);
        GL.End();
        GL.Begin(GL.LINES);
        GL.Color(new Color(color.r, color.g, color.b, 1f));
        foreach (int i in Edges) GL.Vertex(Corners[i]);
        GL.End();
        GL.PopMatrix();
    }
}
