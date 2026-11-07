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

- `dist/DynamicJD-0.1.0-bs1.40.8.zip`
- `dist/DynamicJD-0.1.0-bs1.42.0.zip`

`dist/`, `bin/`, and `obj/` are ignored by Git.

## Release process

There is no GitHub Actions build. Build locally, smoke-test the intended version, then create the
GitHub release manually and attach the matching zip.

- Stable: `v0.1.0` -> `DynamicJD-0.1.0-bs1.40.8.zip`
- Alpha: `v0.1.0-alpha.1` -> `DynamicJD-0.1.0-bs1.42.0.zip`

Increment the alpha suffix for later alpha fixes.

## Runtime design

- `VariableMovementDataProvider.Init` is the primary hook. DynamicJD writes a beat offset, not NJS.
- `ManualUpdate` applies optional reaction-time compensation during NJS changes.
- `LevelScenesTransitionPatches` captures map identity and song speed before gameplay.
- `NoteMovementPatches` implements optional instant note rotation.
- `CompatibilityPatches` contains only the version-gated SongCore negative-NJS protection and the
  1.44 BeatLeader recorder bridge.
- Replay playback deliberately follows the normal gameplay path; there is no replay-specific
  SiraUtil/BeatLeader patching.

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
