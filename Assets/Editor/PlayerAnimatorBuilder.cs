using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds Player.controller from the clips in the player's Animations folder and
/// assigns it to the player model. Re-run it (Tools > Player Animations > Rebuild
/// Player Animator) after adding clips; optional ones like Jump_Fall_Loop or
/// Crouch_Walk are wired in when present.
///
/// Rebuilding replaces the controller, so make lasting changes here rather than
/// in the Animator window. PlayerAnimator drives the parameters at runtime.
///
/// Base layer: Locomotion (Idle/Run blend), Crouch (Idle/Walk blend),
///   JumpStart -> Fall -> Land, Hurt/Hurt2/... (one per Damage_Impact clip, picked at random), Death.
/// Actions layer (full body, faded in by PlayerAnimator): Block, Parry, BlockImpact, and a
///   sub-state machine per combo holding one state per hit (Standing1.., Running1.., Shielding1..).
/// States are laid out in rows so the Animator window stays readable.
public static class PlayerAnimatorBuilder
{
    const string ControllerPath = "Assets/Art/Characters/Player/Player.controller";
    const string AnimationFolder = "Assets/Art/Characters/Player/Animations";

    // Measured from the clips:
    // - Jump_Start crouches for ~55% before the feet leave the ground, but the
    //   game launches immediately, so start the clip at takeoff.
    // - Jump_Land's feet reach the floor about halfway through.
    // - Moving uses Locomotion_Walk (falling back to Locomotion_Run). It plays at its own
    //   pace here; PlayerAnimator.runAnimationSpeed scales it at runtime through RunAnimSpeed.
    // - Where each attack clip's blow lands (normalized time: the blade, or the shield
    //   for the bash, arriving in front), and its default playback speed. Each combo
    //   hit's hit frames open just before that moment, since at combo range the
    //   enemy is already inside the padded hitbox and the hit counts the instant they
    //   open; the swing lasts exactly as long as the clip. A hit's own Anim Speed
    //   (PlayerAttack) overrides the default speed.
    const float JumpTakeoff = 0.55f;
    const float LandTouchdown = 0.5f;
    const float ParrySpeed = 1.3f;
    static readonly Dictionary<string, (float peak, float speed)> SlashTiming = new Dictionary<string, (float, float)>
    {
        { "Attack_Lunge_Slash", (0.40f, 1.5f) },
        { "Attack_Jab", (0.25f, 1.6f) }, // Kevin Iglesias HumanM@Attack1H01_R: lands as the thrust shoots out
        { "Attack_Shield_Bash", (0.33f, 1.3f) }, // Kevin Iglesias HumanM@AttackShield01
        { "Attack_Rising_Slash", (0.50f, 1.3f) },
        // DoubleL One Hand Up attacks, sped up: at their authored speed each takes 2-3 s,
        // with the swing well over a second in. Low Cleave has the longest wind-up.
        { "Attack_Sweep_Slash", (0.21f, 2f) },     // Attack_A_1
        { "Attack_Turning_Cleave", (0.34f, 2f) },  // Attack_A_3
        { "Attack_Low_Cleave", (0.41f, 2.5f) },    // Attack_B_1
        { "Attack_Wide_Slash", (0.44f, 2.6f) },    // Attack_B_3
    };
    static readonly (float peak, float speed) DefaultSlashTiming = (0.4f, 1.5f);
    // Hit frames open this long (seconds) before the blow lands.
    const float ImpactLead = 0.03f;
    // Clips that strike with the shield as well as the sword (the sync sets those hits' hitbox).
    static readonly HashSet<string> ShieldStrikeClips = new HashSet<string> { "Attack_Shield_Bash" };

    [MenuItem("Tools/Player Animations/Rebuild Player Animator")]
    public static void Build()
    {
        Dictionary<string, AnimationClip> clips = LoadClips();
        AnimationClip Clip(params string[] names)
        {
            foreach (string n in names)
                if (clips.TryGetValue(n, out AnimationClip c)) return c;
            return null;
        }

        AnimationClip idle = Clip("Locomotion_Idle");
        if (idle == null)
        {
            Debug.LogError("[PlayerAnimatorBuilder] Need at least Animations/Locomotion/Idle.fbx.");
            return;
        }

        PlayerMovement movement = Object.FindObjectOfType<PlayerMovement>();
        float runSpeed = movement != null ? movement.moveSpeed : 6f;
        float crouchSpeed = runSpeed * (movement != null ? movement.crouchSpeedMultiplier : 0.4f);
        PlayerAttack attack = Object.FindObjectOfType<PlayerAttack>();
        PlayerAttack.ComboSet[] combos = attack != null && attack.combos.Length > 0 ? attack.combos : PlayerAttack.DefaultCombos();
        if (attack != null) SyncComboTimings(attack, Clip);

        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "RunAnimSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f,
        });
        controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Crouching", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Blocking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("ComboStep", AnimatorControllerParameterType.Int);
        controller.AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "AttackSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f,
        });
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("HurtIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter("Parry", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("BlockImpact", AnimatorControllerParameterType.Trigger);

        var missing = new List<string>();
        BuildBaseLayer(controller, controller.layers[0].stateMachine, Clip, runSpeed, crouchSpeed, missing);
        BuildActionsLayer(controller, Clip, combos, missing);
        AssetDatabase.SaveAssets();

        AssignToPlayer(controller);

        string note = missing.Count > 0 ? $" Not found (skipped): {string.Join(", ", missing)}." : "";
        Debug.Log($"[PlayerAnimatorBuilder] Built {ControllerPath} from {clips.Count} clip(s).{note}");
    }

    delegate AnimationClip ClipLookup(params string[] names);

    static void BuildBaseLayer(AnimatorController controller, AnimatorStateMachine sm, ClipLookup clip,
        float runSpeed, float crouchSpeed, List<string> missing)
    {
        AnimatorState locomotion = sm.AddState("Locomotion");
        locomotion.motion = SpeedBlend(controller, "Locomotion", clip("Locomotion_Idle"), Require(clip, missing, "Locomotion_Walk", "Locomotion_Run"), runSpeed);
        locomotion.speedParameter = "RunAnimSpeed";
        locomotion.speedParameterActive = true;
        sm.defaultState = locomotion;

        AnimationClip crouchIdle = Require(clip, missing, "Crouch_Idle");
        if (crouchIdle != null)
        {
            AnimatorState crouch = sm.AddState("Crouch");
            crouch.motion = SpeedBlend(controller, "Crouch", crouchIdle, Require(clip, missing, "Crouch_Walk"), crouchSpeed);
            Link(locomotion, crouch).AddCondition(AnimatorConditionMode.If, 0, "Crouching");
            Link(crouch, locomotion).AddCondition(AnimatorConditionMode.IfNot, 0, "Crouching");
        }

        // Air: JumpStart on takeoff, Fall when dropping (also when walking off a ledge), Land on touchdown.
        AnimationClip startClip = Require(clip, missing, "Jump_Start");
        AnimationClip fallClip = Require(clip, missing, "Jump_Fall_Loop", "Jump_Fall");
        AnimationClip landClip = Require(clip, missing, "Jump_Land");

        AnimatorState jumpStart = null, fall = null, land = null;
        if (startClip != null) jumpStart = AddState(sm, "JumpStart", startClip);
        if (fallClip != null) fall = AddState(sm, "Fall", fallClip);
        if (landClip != null) land = AddState(sm, "Land", landClip);

        if (jumpStart != null)
        {
            AnimatorStateTransition t = Link(locomotion, jumpStart, 0.05f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            t.AddCondition(AnimatorConditionMode.Greater, 0.5f, "VerticalSpeed");
            t.offset = JumpTakeoff;
        }
        if (fall != null)
        {
            AnimatorStateTransition t = Link(locomotion, fall, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            t.AddCondition(AnimatorConditionMode.Less, -2f, "VerticalSpeed");
            if (jumpStart != null) LinkAtEnd(jumpStart, fall, 0.9f, 0.15f);
        }

        AnimatorState touchdownTarget = land != null ? land : locomotion;
        foreach (AnimatorState air in new[] { jumpStart, fall })
        {
            if (air == null) continue;
            AnimatorStateTransition t = Link(air, touchdownTarget, 0.05f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            if (land != null) t.offset = LandTouchdown;
        }
        if (land != null)
        {
            LinkAtEnd(land, locomotion, 0.8f, 0.15f);
            if (jumpStart != null)
            {
                AnimatorStateTransition t = Link(land, jumpStart, 0.05f);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                t.AddCondition(AnimatorConditionMode.Greater, 0.5f, "VerticalSpeed");
                t.offset = JumpTakeoff;
            }
        }

        // Death first so it wins over Hurt on the killing blow.
        AnimationClip deathClip = Require(clip, missing, "Damage_Death");
        if (deathClip != null)
        {
            AnimatorState death = AddState(sm, "Death", deathClip);
            LinkAny(sm, death, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Dead");
            Link(death, locomotion, 0f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        }

        // One state per hit-reaction clip (Damage_Impact, Damage_Impact_2, ...), tagged
        // "Hurt". PlayerAnimator picks one at random through HurtIndex.
        var hurtClips = new List<AnimationClip>();
        AnimationClip firstHurt = Require(clip, missing, "Damage_Impact");
        if (firstHurt != null) hurtClips.Add(firstHurt);
        for (AnimationClip next = clip("Damage_Impact_2"); next != null && firstHurt != null; next = clip($"Damage_Impact_{hurtClips.Count + 1}"))
            hurtClips.Add(next);
        for (int i = 0; i < hurtClips.Count; i++)
        {
            AnimatorState hurt = AddState(sm, i == 0 ? "Hurt" : $"Hurt{i + 1}", hurtClips[i]);
            hurt.tag = "Hurt";
            AnimatorStateTransition enter = LinkAny(sm, hurt, 0.05f);
            enter.AddCondition(AnimatorConditionMode.If, 0, "Hurt");
            enter.AddCondition(AnimatorConditionMode.Equals, i, "HurtIndex");
            // A second hit during the same reaction restarts it.
            enter.canTransitionToSelf = true;
            LinkAtEnd(hurt, locomotion, 0.85f, 0.15f);
        }

        // Layout: movement on the top row (jumping to the right), reactions below.
        sm.entryPosition = new Vector3(0f, 0f);
        sm.anyStatePosition = new Vector3(0f, 260f);
        sm.exitPosition = new Vector3(1100f, 0f);
        Place(sm, "Locomotion", 250f, 0f);
        Place(sm, "Crouch", 250f, 110f);
        Place(sm, "JumpStart", 550f, -110f);
        Place(sm, "Fall", 850f, -110f);
        Place(sm, "Land", 850f, 0f);
        Place(sm, "Death", 550f, 260f);
        for (int i = 0; i < hurtClips.Count; i++) Place(sm, i == 0 ? "Hurt" : $"Hurt{i + 1}", 250f, 260f + 70f * i);
    }

    static void BuildActionsLayer(AnimatorController controller, ClipLookup clip, PlayerAttack.ComboSet[] combos, List<string> missing)
    {
        var sm = new AnimatorStateMachine { name = "Actions", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(sm, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Actions",
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f, // PlayerAnimator fades this in while an action plays
            stateMachine = sm,
        });

        AnimatorState empty = sm.AddState("Empty");
        sm.defaultState = empty;

        AnimatorState block = null;
        AnimationClip blockClip = Require(clip, missing, "Block_Idle");
        if (blockClip != null)
        {
            block = AddAction(sm, "Block", blockClip);
            Link(empty, block).AddCondition(AnimatorConditionMode.If, 0, "Blocking");
            Link(block, empty).AddCondition(AnimatorConditionMode.IfNot, 0, "Blocking");
        }

        // Any-state order is priority order: reactions beat starting a swing.
        AnimationClip parryClip = Require(clip, missing, "Parry_Shield");
        if (parryClip != null)
        {
            AnimatorState parry = AddAction(sm, "Parry", parryClip);
            parry.speed = ParrySpeed;
            LinkAny(sm, parry, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "Parry");
            ReturnFromAction(parry, block, empty);
        }

        AnimationClip impactClip = Require(clip, missing, "Block_Impact");
        if (impactClip != null)
        {
            AnimatorState impact = AddAction(sm, "BlockImpact", impactClip);
            LinkAny(sm, impact, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "BlockImpact");
            ReturnFromAction(impact, block, empty);
        }

        // Layout: idle/guard states on the top row, each combo in its own group below.
        sm.entryPosition = new Vector3(0f, 0f);
        sm.anyStatePosition = new Vector3(0f, 260f);
        sm.exitPosition = new Vector3(1100f, 0f);
        Place(sm, "Empty", 250f, 0f);
        Place(sm, "Block", 550f, 0f);
        Place(sm, "Parry", 550f, 110f);
        Place(sm, "BlockImpact", 850f, 110f);

        // One state per hit of each combo, picked by ComboIndex and ComboStep, grouped
        // in a sub-state machine per combo. Each plays from its first frame; the
        // gameplay swing lasts as long as the clip.
        for (int c = 0; c < combos.Length; c++)
        {
            AnimatorStateMachine group = sm.AddStateMachine(combos[c].name, new Vector3(250f + 300f * c, 260f));
            group.entryPosition = new Vector3(0f, 0f);
            group.anyStatePosition = new Vector3(0f, 110f);
            group.parentStateMachinePosition = new Vector3(600f, 0f);
            group.exitPosition = new Vector3(600f, 110f);
            for (int i = 0; i < combos[c].hits.Length; i++)
            {
                AnimationClip slashClip = StepClip(clip, combos[c], i, missing);
                if (slashClip == null) continue;
                AnimatorState attack = AddAction(group, $"{combos[c].name}{i + 1}", slashClip);
                Place(group, attack.name, 300f, 80f * i);
                attack.speed = StepSpeed(combos[c].hits[i], slashClip);
                // PlayerAttack speeds up a swing's tail once the next hit is queued.
                attack.speedParameter = "AttackSpeed";
                attack.speedParameterActive = true;
                AnimatorStateTransition enter = LinkAny(sm, attack, i == 0 ? 0.05f : 0.08f);
                enter.AddCondition(AnimatorConditionMode.If, 0, "Attacking");
                enter.AddCondition(AnimatorConditionMode.Equals, c, "ComboIndex");
                enter.AddCondition(AnimatorConditionMode.Equals, i + 1, "ComboStep");

                // Leave once the gameplay swing (and so the clip) ends.
                if (block != null)
                {
                    AnimatorStateTransition toBlock = Link(attack, block, 0.15f);
                    toBlock.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");
                    toBlock.AddCondition(AnimatorConditionMode.If, 0, "Blocking");
                }
                Link(attack, empty, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");
            }
        }
    }

    /// Moves a state in the Animator window (layout only).
    static void Place(AnimatorStateMachine sm, string stateName, float x, float y)
    {
        ChildAnimatorState[] states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state.name != stateName) continue;
            states[i].position = new Vector3(x, y, 0f);
            sm.states = states;
            return;
        }
    }

    /// Hit i of a combo uses <prefix>_<i+1> (hit 1 may also be just <prefix>). A hit
    /// without its own clip reuses the combo's first one.
    static AnimationClip HitClipFor(ClipLookup clip, string prefix, int i, List<string> missing = null)
    {
        AnimationClip c = i == 0 ? clip($"{prefix}_1", prefix) : clip($"{prefix}_{i + 1}");
        if (c != null) return c;
        missing?.Add($"{prefix}_{i + 1}");
        return i == 0 ? null : clip($"{prefix}_1", prefix);
    }

    static (float peak, float speed) TimingFor(AnimationClip clip) =>
        SlashTiming.TryGetValue(clip.name, out var t) ? t : DefaultSlashTiming;

    /// Fits each combo hit to its clip: active frames centred on the swing's peak,
    /// and startup + active + recovery = the clip's length at its playback speed.
    /// The clip a combo hit plays: its own Clip if set, otherwise the combo's prefix rule.
    static AnimationClip StepClip(ClipLookup clip, PlayerAttack.ComboSet set, int i, List<string> missing = null)
    {
        string own = set.hits[i].clip;
        if (!string.IsNullOrEmpty(own))
        {
            AnimationClip c = clip(own);
            if (c != null) return c;
            missing?.Add(own);
        }
        return HitClipFor(clip, set.clipPrefix, i, missing);
    }

    static float StepSpeed(PlayerAttack.AttackStep step, AnimationClip clip) =>
        step.animSpeed > 0f ? step.animSpeed : TimingFor(clip).speed;

    /// Reads which clip and speed each combo hit uses from the Animator (as edited by
    /// hand), stores them on the hits, and fits their timings, without rebuilding.
    [MenuItem("Tools/Player Animations/Sync Combo Timings From Animator")]
    public static void SyncFromAnimator()
    {
        PlayerAttack attack = Object.FindObjectOfType<PlayerAttack>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (attack == null || controller == null || controller.layers.Length < 2)
        {
            Debug.LogError("[PlayerAnimatorBuilder] Need the player in the open scene and an Actions layer in " + ControllerPath);
            return;
        }
        Undo.RecordObject(attack, "Sync combo timings from the Animator");
        foreach (AnimatorStateTransition t in controller.layers[1].stateMachine.anyStateTransitions)
        {
            int combo = -1, step = -1;
            foreach (AnimatorCondition c in t.conditions)
            {
                if (c.parameter == "ComboIndex") combo = Mathf.RoundToInt(c.threshold);
                if (c.parameter == "ComboStep") step = Mathf.RoundToInt(c.threshold) - 1;
            }
            if (combo < 0 || combo >= attack.combos.Length || step < 0 || step >= attack.combos[combo].hits.Length) continue;
            if (!(t.destinationState?.motion is AnimationClip clip)) continue;
            PlayerAttack.AttackStep hit = attack.combos[combo].hits[step];
            hit.clip = clip.name;
            hit.animSpeed = t.destinationState.speed;
            hit.hitbox = ShieldStrikeClips.Contains(clip.name) ? PlayerAttack.HitboxSource.SwordAndShield : PlayerAttack.HitboxSource.Sword;
        }
        Dictionary<string, AnimationClip> clips = LoadClips();
        SyncComboTimings(attack, names =>
        {
            foreach (string n in names)
                if (clips.TryGetValue(n, out AnimationClip c)) return c;
            return null;
        });
    }

    static void SyncComboTimings(PlayerAttack attack, ClipLookup clip)
    {
        Undo.RecordObject(attack, "Sync combo timings to slash clips");
        var report = new List<string>();
        foreach (PlayerAttack.ComboSet set in attack.combos)
        for (int i = 0; i < set.hits.Length; i++)
        {
            PlayerAttack.AttackStep step = set.hits[i];
            AnimationClip slashClip = StepClip(clip, set, i);
            if (slashClip == null) continue;
            float speed = StepSpeed(step, slashClip);
            float clipTime = slashClip.length / speed;
            float impact = TimingFor(slashClip).peak * clipTime;
            step.startup = Mathf.Max(0f, impact - ImpactLead);
            step.recovery = Mathf.Max(0f, clipTime - step.startup - step.active);
            report.Add($"{set.name} {i + 1} ({slashClip.name} x{speed:0.##}, {step.hitbox}): lands {impact:F2} s, swing {clipTime:F2} s");
        }
        EditorUtility.SetDirty(attack);
        EditorSceneManager.MarkSceneDirty(attack.gameObject.scene);
        Debug.Log("[PlayerAnimatorBuilder] Combo timings set from the clips: " + string.Join("; ", report));
    }

    static void AssignToPlayer(AnimatorController controller)
    {
        PlayerVisuals visuals = Object.FindObjectOfType<PlayerVisuals>();
        Animator animator = visuals != null && visuals.modelRoot != null
            ? visuals.modelRoot.GetComponentInChildren<Animator>() : null;
        if (animator == null)
        {
            Debug.LogWarning("[PlayerAnimatorBuilder] No Animator under the player's ModelRoot in the open scene; assign the controller by hand.");
            return;
        }
        Undo.RecordObject(animator, "Assign Player Animator");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
    }

    static Dictionary<string, AnimationClip> LoadClips()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder }))
        {
            foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                if (o is AnimationClip c && c.length > 0.05f) clips[c.name] = c;
            }
        }
        return clips;
    }

    static AnimationClip Require(ClipLookup clip, List<string> missing, params string[] names)
    {
        AnimationClip c = clip(names);
        if (c == null) missing.Add(names[0]);
        return c;
    }

    static BlendTree SpeedBlend(AnimatorController controller, string name, AnimationClip still, AnimationClip moving,
        float movingSpeed, float movingTimeScale = 1f)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(still, 0f);
        if (moving != null)
        {
            tree.AddChild(moving, movingSpeed);
            ChildMotion[] children = tree.children;
            children[1].timeScale = movingTimeScale;
            tree.children = children;
        }
        return tree;
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip)
    {
        AnimatorState state = sm.AddState(name);
        state.motion = clip;
        return state;
    }

    /// Action states are tagged so PlayerAnimator knows to keep the layer faded in.
    static AnimatorState AddAction(AnimatorStateMachine sm, string name, AnimationClip clip)
    {
        AnimatorState state = AddState(sm, name, clip);
        state.tag = "Action";
        return state;
    }

    static void ReturnFromAction(AnimatorState action, AnimatorState block, AnimatorState empty)
    {
        if (block != null) LinkAtEnd(action, block, 0.9f, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Blocking");
        LinkAtEnd(action, empty, 0.9f, 0.15f);
    }

    static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration = 0.1f)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        return t;
    }

    static AnimatorStateTransition LinkAtEnd(AnimatorState from, AnimatorState to, float exitTime, float duration)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
        return t;
    }

    static AnimatorStateTransition LinkAny(AnimatorStateMachine sm, AnimatorState to, float duration)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(to);
        t.canTransitionToSelf = false;
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        return t;
    }
}
