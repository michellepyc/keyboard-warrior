# Player Animations

## Naming rules

- **Rename every Mixamo download right away.** Mixamo names the file after the
  animation and every clip inside it `mixamo.com`.
- **Put each file in the folder for the action it belongs to.** The folder is
  automatically added to the clip name, so keep file names short:
  `Block/Crouch_Impact.fbx` becomes the clip **`Block_Crouch_Impact`**.
- **Blocked vs. unblocked comes from the folder:**
  - `Block/Crouch_Impact` is a crouching hit you **blocked**.
  - `Damage/Crouch_Impact` is a crouching hit that **got through**.
- **Name transitions `From_To_To`**, e.g. `Crouch/Stand_To_Crouch`. Anything with
  `_To_` in the name plays once.
- **Looping is automatic.** A name containing `Idle`, `Walk`, `Run` or `Loop`
  loops (unless it's a transition). Everything else plays once.
- **After renaming or moving files**, run **Tools → Player Animations →
  Re-apply Clip Names and Looping**. This also undoes any clip trimming you did
  in the Inspector.

## Mixamo download settings

- **Character:** your paladin.
- **In Place:** check it on every movement animation (walk, run, crouch walk).
  The game moves the character in code, so the animation shouldn't move it.
- **Format:** FBX for Unity (.fbx).
- **Skin:** Without Skin.
- **Frames per second:** 30. **Keyframe reduction:** none.

## Checklist

**Now** = needed by the game as it plays today.
**Later** = polish. Unity blends between clips automatically, so most
transition clips are optional.

### Locomotion/
- [ ] `Idle`: **Now**. Standing with sword and shield. Loops.
- [x] `Run`: Loops, in place. Kevin Iglesias `HumanM@Run01_Forward` (rotation offset 180),
      played at PlayerAnimator > Run Animation Speed 1.15. The old Mixamo run is in
      `Player/Unused/Run_Mixamo.fbx` (it used Run Animation Speed 1.35).
- [ ] `Walk`: **Later**. Only if you add a walk speed.

### Jump/
- [ ] `Start`: **Now**. The takeoff.
- [ ] `Fall_Loop`: **Now**. Held in the air. Loops.
- [ ] `Land`: **Now**. The landing.

### Crouch/
- [ ] `Idle`: **Now**. Loops.
- [ ] `Walk`: **Now**. Crouch-walking. Use In Place. Loops.
- [ ] `Stand_To_Crouch`: **Later**.
- [ ] `Crouch_To_Stand`: **Later**.

### Attack/
- [x] `Slash`: **Now**. Combo hit 1 (J). Keep it short; the hit lands about 0.06 s in.
- [x] `Slash_2`: Combo hit 2 (press J again near the end of hit 1). Kevin Iglesias `HumanM@Attack1H01_R` (the old Mixamo one is in `Player/Unused/`).
  Kevin Iglesias clips face the opposite way to the Mixamo ones on this model, so they need
  Root Transform Rotation > Offset = 180 in the clip's import settings, or the character turns around.
- [x] `Slash_3`: Combo hit 3, a shield strike. Kevin Iglesias `HumanM@AttackShield01`.
- [x] `Slash_4`: Combo hit 4, the finisher. Hits harder.
  These four are the **Shielding** combo (attack while, or just after, blocking).
- [x] `A_1`, `A_2`, `A_3`: the **Running** combo (attack while moving). DoubleL One Hand Up Attack_A (InPlace).
      `A_4` is its slow, heavy finisher: Kevin Iglesias `HumanM@AttackPolearm01` (a lunging thrust).
- [x] `B_1`, `B_2`, `B_3`: the **Standing** combo (attack standing still). DoubleL One Hand Up Attack_B (InPlace).
      `B_4` is its slow, heavy finisher: Kevin Iglesias `HumanM@Attack2H01` (a two-handed overhead).
  DoubleL and Kevin Iglesias clips need Root Transform Rotation > Offset = 180 on this model.
  More hits: add the next clip (`Slash_5`, `A_4`...) and a hit to that combo in PlayerAttack > Combos, then rebuild the animator.
  A new combo: add a ComboSet with its clip prefix (e.g. `Attack_C` for `C_1`, `C_2`...).
  A new clip's swing peak defaults to 40% of the clip; add its measured peak to `SlashTiming` in PlayerAnimatorBuilder.
- [ ] `Crouch_Slash`: **Later**.
- [ ] `Air_Slash`: **Later**.

### Block/ (hits you blocked)
- [ ] `Idle`: **Now**. Holding K with the shield up. Loops.
- [ ] `Impact`: **Now**. Standing, hit lands on the shield.
- [ ] `Idle_To_Block`: **Later**. Raising the shield.
- [ ] `Block_To_Idle`: **Later**. Lowering it.
- [ ] `Crouch_Idle`: **Later**. Crouching block. Loops.
- [ ] `Crouch_Impact`: **Later**. Crouching, hit blocked.

### Parry/
- [ ] `Shield`: **Now**. A quick deflect with the shield (tapping K). A fast
      `Block/Impact` works as a stand-in.
- [ ] `Sword`: **Later**. A deflect with the sword (parrying with J).

### Damage/ (hits that got through)
- [x] `Impact`: Standing, took a hit.
- [x] `Impact_2`: Another standing hit reaction; each hit picks one at random. Kevin Iglesias
      `HumanM@CombatDamage01` (rotation offset 180). Add `Impact_3`... for more variety.
- [x] `Death`: Plays when HP reaches 0. Kevin Iglesias `HumanM@Death01` (rotation offset 180; the old Mixamo one is in `Player/Unused/`).
- [ ] `Crouch_Impact`: **Later**. Crouching, took a hit.
- [ ] `Get_Up`: **Later**. For the respawn.
