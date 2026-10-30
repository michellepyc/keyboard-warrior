# Adding an enemy

Any enemy can be parried, blocked and locked on to. It needs three pieces:
`CombatTarget`, `EnemyHitbox` and `HitboxWindow`. Its own AI (or none at all) only
has to play its attack animations.

## Setup
1. **Root:** add a **CombatTarget** to the enemy's root object. Give the root a collider
   (or colliders on its body parts) so the player's sword can hit it.
2. **Weapon:** on the weapon or fist that hits, which is usually a child of a hand bone:
   - Add a **Box, Sphere or Capsule collider** shaped around the striking part (blade, fist).
     It's made a trigger automatically.
   - Add an **EnemyHitbox**. Set its **Id** (`Sword`, `LeftFist`...), **Damage** and knockback.
   - Put one on each weapon, each with its own id.
3. **Attack states:** select each attack state in the enemy's Animator, click **Add
   Behaviour → HitboxWindow**, and set:
   - **Hitbox:** the id from step 2.
   - **Open At / Close At:** when the blade is swinging through, as a fraction of the
     animation (0 = start, 1 = end). Scrub the clip's preview to find it. Playback speed
     doesn't matter; it's a fraction of the animation, not seconds.
   - **Warn Lead:** seconds of warning before it opens. It shows the CombatTarget's Warn
     Marker, if you give it one.

   Several windows on one state make several strikes (e.g. a two-hit combo animation).

That's it. The player's attack-key parry (catch the strike on the sword or shield), K
parry and block, the 30 ms late-parry grace, damage, sparks, sounds and HUD all work with no
other setup. Lock-on, the combo step and blocking all turn toward the closest CombatTarget.

## Reacting (optional)
- **With no AI script:** add these triggers to the enemy's Animator and they're set
  automatically:
  - `Parried`, when one of its strikes is parried (it's also stunned for **Stun Time**).
    Transition from Any State to a stagger animation.
  - `Hurt`, when the player hits it.
- **From a script:**
  - `CombatTarget.StrikeResolved` tells you each strike's result: Parry, Blocked or Hit.
  - `EnemyHitbox.Resolved` also reports a miss (null).
  - You can call `EnemyHitbox.Open()` / `Close()` yourself instead of using HitboxWindow.
  - To handle the player's hits yourself, add a component that implements `IHittable`, and
    the CombatTarget passes hits to it. `EnemyAttacker` does this.

## Notes
- Turn on the debug view (F1) to see open hitboxes in red.
- Enemies don't have health yet: they react to hits but don't die.
- `EnemyAttacker` (the red block) uses the same system: its lunge opens the `StrikeHitbox` child.
