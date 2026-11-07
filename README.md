# DynamicJD

Type-aware jump-distance profiles for Beat Saber 1.40+.

DynamicJD changes the game's **beat offset**, not NJS. A user-editable base JD/RT config is shaped by a
hardcoded per-type mathematical model, then converted to the beat offset the game needs. Switching
models never edits the user's config.

## Install

Copy `DynamicJD.dll` (and `DynamicJD.pdb` if desired) into the game's `Plugins` folder. BSIPA reads
the embedded manifest. Requires BSIPA, BSML and SiraUtil.

## Two-layer model

### Editable JD config

Each profile stores:

* base reaction time at NJS 20 (`rt20`);
* RT slope in ms per NJS;
* optional quadratic RT curvature per squared NJS step;
* optional manual override JD.

The UI can edit the base as reaction time or jump distance. The evaluation is a smooth formula, so
any float NJS is handled exactly:

```text
baseRT(njs) = rt20 + slope * (njs - 20) + curvature * (njs - 20)^2
```

### Hardcoded type model

A type model supplies coefficients per BeatLeader bit type:

```text
z = (njs - 20) / 10
factor_type(njs) = clamp(1 + a + b*z + c*z^2, 0.55, 1.85)
effectiveRT = baseRT(njs) * lerp(1, factor_type(njs), type_strength)
targetJD = effectiveRT * 2*njs / 500
```

Only BeatLeader bit types are used: Accuracy, Tech, Midspeed (Balanced), Speed, Fitbeat, Linear and
Bomb Reset. Combined bits blend their factors. When BeatLeader does not publish a type bitmask, the
same bit types are inferred from its speed/style/feature tags first, then from tech/acc ratings,
peak sustained eBPM, linear percentage, NPS and bomb density. The type card shows when it is an
inferred result. Inferred types use metric/tag heuristics and still apply the selected type factor,
but may be less accurate than BeatLeader-provided `type` bitmasks.

Built-in models:

* `Adaptive` — research prior from public BeatLeader data; shortens speed/linear maps and lengthens accuracy/tech maps. Inferred types are heuristic but still shaped. The clone can tune its strength.
* `Uniform` — no type shaping.

The fixed base profile slots are:

* `Custom` — your editable setup, first in the list.
* `Suggested` — generated from your own public replay behavior. It resolves the local SteamID through Steamworks (falling back to the Beat Saber player ID/name), uses performance-weighted replay RT, a skill-tier prior, and a confidence score; `Refresh suggestion` regenerates it.
* `Default` — community ranked-player median.
* `Competitive` — top-player median.
* `Relaxed` — longer reaction time.
* `Per-Player` — applies the player selected in the Copy a player's setup section. Cloned players are kept in a saved list; the most recent legacy clone is migrated into that list.

The November 2026 research sample showed ~204ms RT around rank 800, ~221ms around rank 4,000 and
~279–280ms around rank 16,000+. The type model reproduces the observed separation: Speed/Linear
slightly below, Tech/Balanced slightly above, Accuracy substantially above.

## UI

The DynamicJD tab has:

* profile pagination: each page shows only the controls for the selected profile;
* map preview with detected type, original/target JD and RT;
* editable base config on the Custom page (RT or JD, slope, curvature, apply strength);
* hardcoded type model selector and type strength;
* a separate `JD Override` control below the song-selection UI, with toggle and 4–32 JD slider;
* an `Instant note rotation` toggle that removes note sway and snaps notes to their final rotation;
* an `ADVANCED` section with preserve-RT-at-song-speed, dependent NJS-event compensation, respect-map-offset, and JD clamp controls;
* replay support uses the same `VariableMovementDataProvider.Init` path as normal gameplay, matching JDFixer: `apply_in_replays` defaults to true and no BeatLeader/SiraUtil-specific replay code is used. Set it to false only to preserve the recorded replay exactly;
* a saved per-player list with a Player selector and remove action on the Per-Player page;
* BeatLeader player search and clone flow, available from the Per-Player page, with a live build feed and disabled actions while running;

## Clone player

1. Open the DynamicJD tab and select the `Per-Player` profile.
2. Use the Player selector to apply a saved player, or open `Copy a player's setup`.
3. Search by BeatLeader player name and select a result.
4. Analyze, then `Save to Per-Player`. The entry is added/updated and selected automatically.

BeatLeader replay headers record the actual post-mod jump distance, which makes this possible. Score lists are fetched in pages of 100 because the public API rejects larger page sizes. Replay failures are logged individually and summarized in the UI as `Errors: N`. Speed-changing modifiers (`FS`, `SF`, `SS`, `FN`) are excluded because they change the NJS/RT relationship. The clone fits a type-adjusted flat, linear, or quadratic profile using cross-validation and sets the global type influence once when saved. ScoreSaber is intentionally not used because its replay metadata records the pre-mod default JD.

## Override precedence

1. Saved per-map override.
2. Global manual override.
3. Editable base config shaped by the type model.

## Conflicts

Do not run this alongside JDFixer, NjsFixer or LevelTweaks; they contend for the same jump-distance
hook. DynamicJD logs a warning when it detects them, but does not disable itself.

## Build

Single-version build:

```bash
dotnet build DynamicJD.csproj -c Release \
  -p:GameVersion=1.40.8 \
  -p:VersionFamily=legacy \
  -p:GameDirectory=/path/to/1.40.8/
```

The embedded manifest is selected automatically from `Versioning/manifest-legacy.json` or
`Versioning/manifest-modern.json` based on `VersionFamily`.

Build both release artifacts and run the tests:

```bash
export DYNAMICJD_1408_DIR=/path/to/1.40.8
export DYNAMICJD_1420_DIR=/path/to/1.42.0
tools/build-artifacts.sh
```

This writes `dist/DynamicJD-0.1.0-bs1.40.8.zip` and
`dist/DynamicJD-0.1.0-bs1.42.0.zip`. References come from each game directory's
`Beat Saber_Data/Managed` and `Plugins` folders.

## Design notes

* Game math is a clean transcription of `CoreMathUtils.CalculateHalfJumpDurationInBeats` and the
  movement provider's offset path.
* The pure core (`Core/`) has no Unity or IPA dependencies and is tested on Linux.
* Map types are cached in `UserData/DynamicJD/map_types.json`; profiles are in `profiles.json`;
  saved player setups are in `per_players.json`; per-map overrides are in `overrides.json`.
* Instant note rotation is visual, but rotation also affects the note hitbox orientation. Replay
  playback applies DynamicJD behavior by default; disable `Apply to replays` for faithful replay viewing.

## Credits

Game behavior understood from the 1.44.1 decompilation and from the public JDFixer project. This is
a fresh implementation; no JDFixer source files are copied.

## Supported builds

DynamicJD is built from one source tree into exact-version artifacts. The manifest records the
exact lower bound for each release; do not claim compatibility for a version that has not passed the
clean-instance smoke test.

| Release tag | Manifest game version | Status | Scope |
|---|---|---|---|
| `v0.1.0` | `1.40.8` | stable | normal gameplay + BeatLeader/ScoreSaber replay |
| `v0.1.0-alpha.1` | `1.42.0` | alpha | normal gameplay + replay with stock BeatLeader/SiraUtil |

`1.44.1` is intentionally deferred until a stock SiraUtil release supports it. The release contains
only DynamicJD; SiraUtil, BeatLeader, ScoreSaber and SongCore remain stock dependencies.

### 1.42 alpha notes

The 1.42 alpha uses stock SiraUtil `3.4.0+bs.1.42.0`, BeatLeader `0.10.666`
(`1.42.0-1.44.1`) and the versions shipped in the BeatLeader zip. On Linux/Proton, BSIPA `4.3.7`
has a known upstream access-violation issue (`nike4613/BeatSaber-IPA-Reloaded#116` and
`ValveSoftware/Proton#6638`); validate on Windows before calling 1.42 FPFC stable. The alpha does
not include SiraUtil/BeatLeader unpatching or replay-specific compatibility hacks.

## Local release process

There is no GitHub build workflow and no private-reference archive. Release artifacts are built
locally against installed game directories and uploaded manually.

1. Run `tools/build-artifacts.sh` with `DYNAMICJD_1408_DIR` and `DYNAMICJD_1420_DIR` set.
2. Run the clean-instance smoke test for the intended version.
3. Create the GitHub release manually and attach the matching zip:
   - stable `v0.1.0`: `DynamicJD-0.1.0-bs1.40.8.zip`
   - alpha `v0.1.0-alpha.1`: `DynamicJD-0.1.0-bs1.42.0.zip`
4. Do not commit `dist/`.

The 1.44.1 target remains deferred.
