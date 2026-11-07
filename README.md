# DynamicJD

DynamicJD keeps every Beat Saber map at a jump distance that feels right to you.

It reads the map's note jump speed (NJS), song speed, and detected map type, then adjusts the
map's jump distance automatically. It does not change NJS or the map file.

## Quick start

1. Install **BSIPA**, **BeatSaberMarkupLanguage**, and **SiraUtil** with your normal mod manager.
2. Copy `DynamicJD.dll` into `Beat Saber/Plugins/`.
3. Start Beat Saber.
4. Open the **DynamicJD** gameplay-setup tab.
5. Pick a profile and adjust the JD/RT sliders if you want.
6. Play a map. DynamicJD applies the setting when the map starts.

Replays use the same behavior as normal gameplay by default. You can turn that off under
**Apply to replays**.

## Profiles

The **Base JD profile** controls the starting point for your jump distance.

| Profile | What it is for |
|---|---|
| **Custom** | Your own editable setup. This is the profile to tune. |
| **Suggested** | Built from your own public BeatLeader replays. Press **Refresh suggestion** to regenerate it. |
| **Default** | A middle-ground profile for most players. |
| **Competitive** | Based on faster top-level reaction times. |
| **Relaxed** | Gives you more time to read the map. |
| **Per-Player** | Copies the setup of a player you choose. |

## Tuning your JD

Open the **DynamicJD** tab and select **Custom**.

- **Tune by** lets you work in **Reaction time** or **Jump distance**.
- **Profile influence** blends your setup with the map's original jump distance.
- **Type adaptation** changes how strongly map types affect the result.
- **Type influence** controls the global strength of map-type shaping.
- **Preserve RT at song speed** keeps your reaction time stable on Faster/Slower Song maps.
- **NJS event compensation** keeps reaction time stable when a map changes NJS mid-song.
- **Respect map offset** uses the map author's offset instead of the calculated one.
- **Minimum JD** and **Maximum JD** clamp the final result.

You can also use the floating **JD Override** control below the song list. It replaces the normal
profile result with one fixed JD for every map while enabled.

## Map types

DynamicJD can shape your JD by map style:

- **Adaptive** uses BeatLeader map types when available and falls back to heuristic detection.
- **Uniform** ignores map type.

Inferred types are shown in the map preview. They are useful, but less precise than a type supplied
by BeatLeader.

## Copy a player's setup

1. Open the **DynamicJD** tab and select **Per-Player**.
2. Choose **Copy a player's setup**.
3. Search for a BeatLeader player.
4. Run the analysis and choose **Save to Per-Player**.

The copied setup is saved locally and selected automatically. Speed-changing modifiers are excluded
from the analysis because they change the relationship between NJS and reaction time.

## Replays

DynamicJD applies to BeatLeader and ScoreSaber replay playback by default, using the same
`VariableMovementDataProvider.Init` path as normal play.

- Turn off **Apply to replays** if you want the replay to use its recorded JD exactly.
- Replay note motion can look different from the original recording when DynamicJD changes the JD.
- DynamicJD does not patch SiraUtil, BeatLeader, ScoreSaber, or FPFC internals.

## Version support

| Release | Status | Notes |
|---|---|---|
| `v0.1.0` | Stable | Beat Saber **1.40.8**; BeatLeader and ScoreSaber replay supported |
| `v0.1.0-alpha.1` | Alpha | Beat Saber **1.42.0**; use stock SiraUtil `3.4.0+bs.1.42.0` and BeatLeader `0.10.666` |
| `1.44.1` | Not yet released | Waiting for compatible stock SiraUtil support |

The **1.42** alpha is for testing. On Linux/Proton, BSIPA `4.3.7` has a known upstream access-violation
problem, so validate 1.42 on Windows/SteamVR before treating it as stable.

## Conflicts

Do not run DynamicJD together with another jump-distance controller:

- **JDFixer**
- **NjsFixer**
- **LevelTweaks**

They compete for the same game hook. DynamicJD logs a warning when it sees them, but it does not
disable itself.

If **Instant note rotation** is enabled, note-rotation or note-hitbox mods may also interfere.

## Troubleshooting

- Open `Logs/_latest.log` and search for `DynamicJD` after a problem.
- Make sure the required versions of BSIPA, BSML, and SiraUtil are installed.
- For the 1.42 alpha, confirm that SiraUtil is `3.4.0+bs.1.42.0` and that you are not using an
  older SiraUtil.
- If JD does not change, check that the plugin is **Enabled**, a profile is selected, and no manual
  JD override is active.

## For developers

Source code lives under [`Source/`](Source/). Build instructions, release steps, and implementation
notes are in [`DEVELOPMENT.md`](DEVELOPMENT.md).

## License

MIT. See [`LICENSE`](LICENSE).
