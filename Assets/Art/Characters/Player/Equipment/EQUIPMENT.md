# Player equipment

Put the sword and shield models here:

```
Equipment/
  Sword/    sword FBX + its textures
  Shield/   shield FBX + its textures
```

FBX files dropped in these folders get Blender prop settings on first import:
axis conversion baked, file scale, no rig or animation, materials imported.

## Exporting from Blender
- Apply all transforms (Ctrl+A > All Transforms) before exporting.
- Put the origin where the hand holds it: the grip for the sword, the arm strap for the shield.
  The model is attached at that point.
- Export as FBX with only the weapon selected ("Selected Objects").

## Swapping out a placeholder
The placeholders are cubes on sockets on the player's skeleton:

| | Socket | Placeholder |
|---|---|---|
| Sword | `Player/ModelRoot/paladin_rigged2/.../mixamorig:RightHand/SwordSocket` | `Sword` |
| Shield | `Player/ModelRoot/paladin_rigged2/.../mixamorig:LeftForeArm/ShieldSocket` | `Shield` |

1. Drag the model from this folder onto the socket in the Hierarchy, so it becomes a child of it.
   Move and rotate it until it sits right in the hand or on the arm. Check it in Play mode while
   attacking and blocking.
2. Add a **Box Collider** to the model, tick **Is Trigger**, and size it to the blade or the shield face.
3. Point the scripts at the new colliders, on the **Player**:
   - **PlayerAttack > Sword Hitbox**: the sword's collider. This is what your slashes hit with.
   - **PlayerAttack > Shield Hitbox**: the shield's collider. Combo hit 3, the shield strike, hits with
     it as well as the sword.
   - **ParryController > Guard Colliders**: both colliders. Enemy strikes caught on them during a
     swing's hit frames are parried.
   - **ParryController > Shield**: the new shield. It's tinted while blocking and parrying.
4. Delete the placeholder cube, or turn it off.

Turn on the combat debug view (F1 in Play mode) to check the hitboxes line up with the new models.
