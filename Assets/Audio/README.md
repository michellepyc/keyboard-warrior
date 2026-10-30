# Audio for Keyboard Warrior

Drop sound files into the folders below and they play in the game automatically.
There's no setup in Unity: when a file is added, removed or renamed, the game's sound
list (`CombatSoundBank`) updates itself.

## Folders

| Folder | Plays when... | Notes |
|---|---|---|
| `SFX/Swing/` | the player starts any sword slash (every combo hit) | Whoosh/swish, 0.2–0.5 s. Several variations. |
| `SFX/Hit/` | a slash or shield bash connects with an enemy | Impact/armour thud, under 0.4 s. Several variations. |
| `SFX/FinisherHit/` | the 4th combo hit (finisher) connects | Bigger and heavier than Hit. Empty folder means a Hit sound plays. |
| `SFX/Parry/` | a perfectly timed parry with the block key (K) | Bright metallic ring, under 0.8 s. |
| `SFX/AttackParry/` | a parry by catching the strike on the sword or shield (attack key, J) | The big "clash": heavier than Parry. Empty folder means a Parry sound plays. |
| `SFX/Block/` | the player blocks a hit (holding K, not a parry) | Dull shield thud, under 0.5 s. |
| `SFX/Hurt/` | the player takes damage | Grunt and/or impact, under 0.6 s. Several variations. |
| `SFX/Whiff/` | a block-key tap that catches nothing | Short and subtle (it fires often), under 0.3 s. |
| `SFX/TypeCorrect/` | each correct letter while typing the Smite sentence | A soft tick or chime, under 0.15 s. Plays a lot, fast: keep it quiet and dry. The game raises its pitch as the sentence fills, so record it at one pitch. |
| `SFX/TypeError/` | a wrong key while typing | A low, muted thud or buzz, under 0.25 s. Clear but not harsh. |
| `SFX/SentenceComplete/` | the last letter of the sentence is typed | A short rising swell or whoosh, about 0.5 s, leading into SmiteCharged. |
| `SFX/SmiteCharged/` | the letters reach the sword and the Smite is ready | A bright ring or holy "charge" hit, under 1 s. |
| `SFX/SmiteReadyLoop/` | while the Smite is ready, waiting for Q | A quiet hum or shimmer that **loops seamlessly**. The first file by name plays; it fades out when the Smite is used. |
| `SFX/TypeFail/` | typing the Smite sentence fails (time ran out, cancelled or hit) | A deflating "fizzle", a dull crumble, or letters clattering down, under 1 s. Disappointing but not punishing. |
| `SFX/SmitePerfect/` | the sentence was typed with no mistakes (plays with SmiteCharged) | A short sparkle or bright chime on top of the charge, under 1 s. |
| `Music/` | the level is playing | Loops forever. The first file by name plays. |

Each folder also has a `README.txt` with the same notes.

## How sounds are played
- **Variations:** put several files in a folder (for example `Hit_01.wav`, `Hit_02.wav`,
  `Hit_03.wav`). Each time, one is picked at random, so repeated hits don't sound identical.
- **Pitch:** every effect is played up to ±5% higher or lower in pitch for extra variety.
  There's no need to bake pitch variations into the files.
- **Overlap:** effects can overlap (a swing and a hit at the same time, for example). Keep tails short.
- **Hit-stop:** the game freezes for a split second on impacts. Sounds keep playing through it, so
  put the impact at the very start of the file, with no silence before it.
- **Volume:** set each file's loudness in the file itself; there are no per-sound volume
  controls in game. The overall SFX and music volumes are in Unity.

## File format
- **Effects:** `.wav`, 44.1 or 48 kHz, 16- or 24-bit. Mono is fine (the game is 2D audio).
  Trim the silence at both ends.
- **Music:** `.wav` or `.ogg`, stereo. It must **loop seamlessly**, since the end goes straight back to the start.
- **Loudness:** normalise peaks to about −1 dBFS. Keep effects roughly consistent with each other,
  and the music quieter than the effects.
- **Names:** anything readable, with no spaces needed (`Swing_Light_01.wav`). The file name order
  only matters for music (the first track plays).

## Adding more
To add a new kind of sound (footsteps, jumps, enemy attacks...), ask for a new folder to be
hooked up. Folders not listed above aren't played yet.

## For the programmer
- Import settings are applied on a file's first import: effects decompress on load (instant
  playback), music streams.
- The bank is `Assets/Audio/CombatSoundBank.asset`, rebuilt by `Editor/AudioFolderImporter.cs`.
  Run **Tools > Audio > Rebuild Sound Bank** if it ever gets out of sync.
- In the scene, `ParrySystems > CombatAudio` plays the effects (it also has the SFX volume and
  pitch variation settings), and the `Music` object's `MusicPlayer` plays the music (volume,
  fade-in, track number).
