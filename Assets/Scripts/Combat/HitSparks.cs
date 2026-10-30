using UnityEngine;

/// Spark bursts where hits connect: your hits on enemies (bigger on the finisher),
/// parries, and blocks. Built in code, so no particle assets are needed. Sparks
/// run on unscaled time so they still fly during hit-stop.
public class HitSparks : MonoBehaviour
{
    public PlayerAttack attack;
    public ParryController defense;

    [Header("Colors")]
    public Color hitColor = new Color(1f, 0.85f, 0.55f);
    public Color finisherColor = new Color(1f, 0.6f, 0.25f);
    public Color parryColor = new Color(0.55f, 0.95f, 1f);
    [Tooltip("A parry caught on the sword or shield (attack key).")]
    public Color attackParryColor = new Color(1f, 0.93f, 0.7f);
    public Color blockColor = new Color(0.75f, 0.78f, 0.85f);

    [Header("Particle counts")]
    public int hitCount = 14;
    public int finisherCount = 30;
    public int parryCount = 40;
    public int attackParryCount = 60;
    public int blockCount = 10;

    ParticleSystem sparks;

    void Awake()
    {
        if (attack == null) attack = FindObjectOfType<PlayerAttack>();
        if (defense == null && attack != null) defense = attack.GetComponent<ParryController>();
        sparks = CreateSparks();
    }

    void OnEnable()
    {
        if (attack != null) attack.HitLanded += OnHitLanded;
        if (defense != null) defense.AttackResolved += OnAttackResolved;
    }

    void OnDisable()
    {
        if (attack != null) attack.HitLanded -= OnHitLanded;
        if (defense != null) defense.AttackResolved -= OnAttackResolved;
    }

    void OnHitLanded(IHittable target)
    {
        if (attack.IsFinisher) Burst(attack.LastHitPoint, finisherColor, finisherCount, 9f);
        else Burst(attack.LastHitPoint, hitColor, hitCount, 6f);
    }

    void OnAttackResolved(ParryResult result, float sinceInput)
    {
        if (result == ParryResult.Parry && defense.LastParryWasAttack) Burst(defense.LastContactPoint, attackParryColor, attackParryCount, 13f);
        else if (result == ParryResult.Parry) Burst(defense.LastContactPoint, parryColor, parryCount, 10f);
        else if (result == ParryResult.Blocked) Burst(defense.LastContactPoint, blockColor, blockCount, 4f);
    }

    void Burst(Vector3 position, Color color, int count, float speed)
    {
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            startColor = color,
            applyShapeToPosition = true,
        };
        // Emit in a few batches with different speeds so the burst has some spread.
        for (int i = 0; i < 3; i++)
        {
            var main = sparks.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * (0.4f + 0.3f * i), speed * (0.7f + 0.3f * i));
            sparks.Emit(emit, Mathf.Max(1, count / 3));
        }
    }

    ParticleSystem CreateSparks()
    {
        var go = new GameObject("HitSparks (runtime)");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.gravityModifier = 1.5f;
        main.maxParticles = 500;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        // Shrink and fade out over each spark's life.
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1f;
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        renderer.sharedMaterial = new Material(shader) { name = "HitSparks (runtime)" };

        ps.Play();
        return ps;
    }
}
