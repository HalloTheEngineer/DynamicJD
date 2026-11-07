# Default-curve research

The built-in `Default` and `Competitive` profiles were derived from public
BeatLeader replay headers. Lead data was collected in November 2026.

Method:

1. Sample BeatLeader players by rank (top 100, and pages corresponding to roughly top 800,
   top 4,000, top 16,000 and top 40,000).
2. Fetch each player's top scores with replay URLs.
3. Range-request the first 8 KB of each `.bsor` replay and parse the BSOR info block.
4. Keep only ranked difficulties with a BeatLeader type bit.
5. Compute the map-authored JD from BPM/NJS/offset and compare it with the replay's recorded
   `jumpDistance`. Keep plays whose JD differs by more than 1.5 JD or 25 ms RT.
6. Convert to JDFixer-compatible RT: `rt = jd * 500 / (2 * njs)`.
7. Group by type bit and fit/median the RT-vs-NJS points.

Observed medians (JDFixer-compatible RT):

| Tier | All modded plays |
|---|---:|
| top ~800 | ~204 ms |
| top ~4,000 | ~221 ms |
| top ~16,000 | ~279 ms |
| top ~40,000 | ~280 ms |

Type separation at the mid-rank sample:

| Type | Median RT |
|---|---:|
| Speed | ~214 ms |
| Linear | ~214 ms |
| Bomb Reset | ~221 ms |
| Balanced | ~227 ms |
| Tech | ~230 ms |
| Accuracy | ~290–360 ms |

The current build uses these observations as a default base RT curve plus a hardcoded per-type
factor model. The factor coefficients are stored in `Core/TypeModel.cs`. The player clone uses a
larger cross-validation study (`fit_study.py`) to select a flat, linear, or quadratic RT curve,
estimate a per-player type strength, and reject speed-modifier samples that distort the NJS/RT
relationship. The default curve remains a prior, not a claim that one curve is optimal for every
player.

`beatleader_replay_jd.py` is the data-collection sketch used for the analysis.
