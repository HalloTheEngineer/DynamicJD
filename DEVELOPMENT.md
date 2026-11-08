# Developing DynamicJD

## Repository layout

- `Source/` — plugin code, BSML UI, tests, and `DynamicJD.csproj`
- `Versioning/` — version-specific BSIPA manifests
- `manifest.json` — reference manifest; the build embeds `Versioning/manifest-legacy.json` or
  `Versioning/manifest-modern.json` based on `VersionFamily`
- `versions.json` — supported version metadata
- `tools/build-artifacts.sh` — local release build
- `research/` — model research scripts and data
- `README.md`, `LICENSE`, release metadata — repository root

## Requirements

- .NET SDK 9.0 or newer for the test project
- .NET Framework 4.8 reference assemblies
- A local Beat Saber installation for each target version
- `zip` and `strings` available on the build machine

The plugin project targets `net48`; the tests target `net9.0`.

## Build one version

```bash
dotnet build Source/DynamicJD.csproj -c Release \
  -p:GameVersion=1.40.8 \
  -p:VersionFamily=legacy \
  -p:GameDirectory=/path/to/1.40.8/
```

Use `VersionFamily=modern` and `GameVersion=1.42.0` for the 1.42 build. The correct manifest is
embedded automatically.

## Build both release artifacts

```bash
export DYNAMICJD_1408_DIR=/path/to/1.40.8
export DYNAMICJD_1420_DIR=/path/to/1.42.0
tools/build-artifacts.sh
```

The script runs the tests, builds both versions, checks the embedded `gameVersion`, and writes:

- `dist/DynamicJD-0.2.0-alpha.1-bs1.40.8.zip`
- `dist/DynamicJD-0.2.0-alpha.1-bs1.42.0.zip`

`dist/`, `bin/`, and `obj/` are ignored by Git.

## Release process

There is no GitHub Actions build. Build locally, smoke-test the intended version, then create the
GitHub release manually and attach the matching zip.

- Stable: `v0.1.0` -> `DynamicJD-0.1.0-bs1.40.8.zip`
- Audio alpha: `v0.2.0-alpha.1` -> `DynamicJD-0.2.0-alpha.1-bs1.40.8.zip`
- 1.42 audio alpha: `v0.2.0-alpha.1-bs1.42.0` -> `DynamicJD-0.2.0-alpha.1-bs1.42.0.zip`

Increment the alpha suffix for later alpha fixes.

## Runtime design

- `VariableMovementDataProvider.Init` is the primary hook. DynamicJD writes a beat offset, not NJS.
- `ManualUpdate` applies optional reaction-time compensation during NJS changes.
- `LevelScenesTransitionPatches` captures map identity and song speed before gameplay.
- `NoteMovementPatches` implements optional instant note rotation.
- `CompatibilityPatches` contains only the version-gated SongCore negative-NJS protection and the
  1.44 BeatLeader recorder bridge.
- `AudioOffsetPatches` starts a sample session at `AudioTimeSyncController.Start` and listens to
  `ScoreController.scoringForNoteStartedEvent` for clean local cuts.
- `AudioOffsetTracker` stores the latest run and `AudioOffsetService` applies a confirmed
  recommendation to `SettingsManager.settings.audio.latency`, saving through `SettingsIO`.
- Replay playback deliberately follows the normal gameplay path; there is no replay-specific
  SiraUtil/BeatLeader patching.

## Audio offset model

`GameNoteController.HandleCut` supplies:

```text
timeDeviation = noteData.time - songTime
positive = early cut, negative = late cut
```

The estimator filters outliers with a median/MAD rule, computes the mean accepted deviation and its
95% confidence interval, and recommends:

```text
suggestedDelta = -mean(timeDeviation)
newLatency = clamp(currentLatency + suggestedDelta, 0, 0.3)
```

A recommendation requires at least 30 accepted cuts, a 5 ms deadband, a confidence interval that
excludes zero, and a maximum step of 50 ms. Replays, practice mode, non-1.0x song speed, chain links,
and the first five seconds of a map are ignored. The **DynamicJD - Audio** tab renders all clean cuts into
one generated `Texture2D`; it is redrawn only when a run finishes, not per frame.

## JD model

Each profile stores a base reaction time at NJS 20, an RT slope, and optional quadratic curvature:

```text
baseRT(njs) = rt20 + slope * (njs - 20) + curvature * (njs - 20)^2
```

The map-type model blends factors per BeatLeader type bit:

```text
z = (njs - 20) / 10
factor_type(njs) = clamp(1 + a + b*z + c*z^2, 0.55, 1.85)
effectiveRT = baseRT(njs) * lerp(1, factor_type(njs), type_strength)
targetJD = effectiveRT * 2*njs / 500
```

Types used: Accuracy, Tech, Midspeed (Balanced), Speed, Fitbeat, Linear, and Bomb Reset. Missing
BeatLeader types are inferred from tags and metrics; inferred results are marked in the UI.

## Tests

```bash
dotnet test Source/Tests/DynamicJD.Tests/DynamicJD.Tests.csproj -c Release
```

The suite covers model math, resolver behavior, classifier rules, cloning, map context handling, and
the BSML source contract.

## 1.42 alpha constraints

- Stock SiraUtil `3.4.0+bs.1.42.0`
- Stock BeatLeader `0.10.666` (`1.42.0-1.44.1`)
- Linux/Proton is blocked in practice by the known BSIPA `4.3.7` access-violation issue
  (`nike4613/BeatSaber-IPA-Reloaded#116`, `ValveSoftware/Proton#6638`).
- Validate on Windows/SteamVR before calling 1.42 stable.
