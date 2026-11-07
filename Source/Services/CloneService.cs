using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicJD.Api;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Core.Cloning;

namespace DynamicJD.Services
{
    /// <summary>
    /// Reconstructs a player's effective JD profile from public BeatLeader replay headers.
    /// BeatLeader records the actual post-mod jump distance, which makes this possible. ScoreSaber is
    /// deliberately not used: its replay metadata records the pre-mod default JD.
    /// </summary>
    public sealed class CloneService
    {
        private const float JdTolerance = 1.5f;
        private const float RtToleranceMs = 25f;

        private readonly BeatLeaderClient _client;
        private readonly ConcurrentDictionary<long, BsorHeader> _headerCache =
            new ConcurrentDictionary<long, BsorHeader>();

        public CloneService(BeatLeaderClient client)
        {
            _client = client;
        }

        public async Task<CloneResult> AnalyzeAsync(string playerIdOrUrl, int maxScores,
            Action<CloneProgress> progress = null)
        {
            string playerId = NormalizePlayerId(playerIdOrUrl);
            var result = new CloneResult();
            if (string.IsNullOrEmpty(playerId))
            {
                result.Message = "Enter a BeatLeader player ID or profile URL.";
                return result;
            }

            void Report(string message, int completed = 0, int total = 0, int modified = 0, int errors = 0)
            {
                if (progress == null) return;
                try
                {
                    progress(new CloneProgress
                    {
                        Message = message,
                        Completed = completed,
                        Total = total,
                        Modified = modified,
                        Errors = errors,
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Warn("Clone progress callback failed: " + ex.Message);
                }
            }

            Report("Fetching scores…");
            List<PlayerScore> scores;
            try
            {
                scores = await _client.GetPlayerScoresAsync(playerId, maxScores).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Message = "BeatLeader request failed: " + ex.Message;
                Report(result.Message);
                return result;
            }

            result.ScoresSeen = scores.Count;
            int skippedSpeed = scores.Count(s =>
                !string.IsNullOrEmpty(s.ReplayUrl) && s.Njs > 0f && s.Bpm > 0f &&
                s.TypeMask != 0 && ScoreModifierRules.HasSpeedModifier(s.Modifiers));
            result.SkippedSpeedModifiers = skippedSpeed;
            Report("Loaded " + scores.Count + " scores" +
                   (skippedSpeed > 0 ? " · skipped " + skippedSpeed + " speed-modifier plays" : string.Empty), 0, 0);
            var candidates = scores
                .Where(s => !string.IsNullOrEmpty(s.ReplayUrl) && s.Njs > 0f && s.Bpm > 0f &&
                            s.TypeMask != 0 && !ScoreModifierRules.HasSpeedModifier(s.Modifiers))
                .Take(maxScores)
                .ToList();

            if (candidates.Count == 0)
            {
                result.Message = "No replay-backed ranked scores with map-type tags were found.";
                Report(result.Message);
                return result;
            }

            var warnings = new ConcurrentBag<string>();
            var names = new ConcurrentBag<string>();
            int replaysRead = 0;
            int processed = 0;
            int modifiedCount = 0;
            Report("Reading replay headers…", 0, candidates.Count);
            var tasks = candidates.Select(async score =>
            {
                try
                {
                    BsorHeader header = await GetHeaderAsync(score, warnings).ConfigureAwait(false);
                    if (header == null) return null;
                    if (!string.IsNullOrEmpty(header.Hash) &&
                        !string.Equals(header.Hash, score.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add("Replay hash mismatch for score " + score.ScoreId + ".");
                        return null;
                    }
                    if (!string.IsNullOrEmpty(header.PlayerName)) names.Add(header.PlayerName);

                    float original = JumpDistanceMath.CalculateJumpDistance(score.Bpm, score.Njs, score.MapOffset);
                    if (original <= 0f || header.JumpDistance <= 0f) return null;

                    float originalRt = JumpDistanceMath.CalculateReactionTime(original, score.Njs);
                    float replayRt = JumpDistanceMath.CalculateReactionTime(header.JumpDistance, score.Njs);
                    bool modded = Math.Abs(header.JumpDistance - original) > JdTolerance ||
                                  Math.Abs(replayRt - originalRt) > RtToleranceMs;

                    Interlocked.Increment(ref replaysRead);
                    if (!modded) return null;

                    Interlocked.Increment(ref modifiedCount);
                    return new CloneSample
                    {
                        Hash = score.Hash,
                        SongName = score.SongName,
                        Difficulty = score.Difficulty,
                        Njs = score.Njs,
                        Bpm = score.Bpm,
                        ReplayJumpDistance = header.JumpDistance,
                        OriginalJumpDistance = original,
                        ReactionTime = replayRt,
                        TypeMask = score.TypeMask,
                        Archetypes = MapArchetypes.FromMask(score.TypeMask).Distinct().ToList(),
                        Stars = score.Stars,
                        Nps = score.Nps,
                        Accuracy = score.Accuracy,
                    };
                }
                finally
                {
                    int done = Interlocked.Increment(ref processed);
                    if (done == 1 || done % 5 == 0 || done == candidates.Count)
                    {
                        Report("Reading replays " + done + "/" + candidates.Count, done, candidates.Count,
                            Math.Min(done, Volatile.Read(ref modifiedCount)), warnings.Count);
                    }
                }
            }).ToList();

            CloneSample[] analyzed = await Task.WhenAll(tasks).ConfigureAwait(false);
            var samples = analyzed.Where(s => s != null).ToList();
            string playerName = names.FirstOrDefault();
            result.ReplaysRead = replaysRead;
            foreach (string warning in warnings) result.Warnings.Add(warning);
            result.ModdedSamples = samples.Count;
            foreach (string warning in warnings) Plugin.Log?.Debug(warning);
            if (samples.Count < 4)
            {
                result.Message = samples.Count == 0
                    ? "No JD modifications detected. This player appears to play maps at their authored JD."
                    : "Only " + samples.Count + " modified samples found; need at least 4 to fit a profile.";
                Report(result.Message, candidates.Count, candidates.Count, samples.Count, warnings.Count);
                return result;
            }

            Report("Fitting profile…", candidates.Count, candidates.Count, samples.Count, warnings.Count);
            TypeModel fitterModel = PluginConfig.Instance == null
                ? TypeModels.Research
                : TypeModels.Get(PluginConfig.Instance.type_model);
            ProfileFitResult fit = ProfileFitter.Fit(samples, fitterModel);
            if (!fit.Success)
            {
                result.Message = fit.Message;
                Report(result.Message, candidates.Count, candidates.Count, samples.Count, warnings.Count);
                return result;
            }

            var profile = new JdProfile
            {
                Name = (string.IsNullOrEmpty(playerName) ? playerId : playerName) + " JD",
                Source = "BeatLeader clone",
                PlayerId = playerId,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                Notes = "Base JD config fitted from " + samples.Count +
                        " clean public replay headers. " + fit.Summary + ".",
                Base = fit.Base,
                SampleCount = fit.SampleCount,
                Confidence = fit.Confidence,
                RecommendedTypeStrength = fit.RecommendedTypeStrength,
            };
            profile.EnsureValid();

            var counts = new Dictionary<string, int>();
            foreach (MapArchetype archetype in MapArchetypes.All)
            {
                counts[archetype.ToString()] = samples.Count(s => s.Archetypes.Contains(archetype));
            }

            result.Profile = profile;
            result.SampleCounts = counts;
            result.RecommendedTypeStrength = fit.RecommendedTypeStrength;
            result.FitSummary = fit.Summary;
            result.Success = true;
            result.Message = "Fitted " + fit.Summary + " for " +
                             (string.IsNullOrEmpty(playerName) ? playerId : playerName) + ".";
            Report("Fit ready · " + fit.Summary, candidates.Count, candidates.Count, samples.Count, warnings.Count);

            return result;
        }

        private async Task<BsorHeader> GetHeaderAsync(PlayerScore score, ConcurrentBag<string> warnings)
        {
            if (_headerCache.TryGetValue(score.ScoreId, out BsorHeader cached))
            {
                return cached;
            }

            try
            {
                BsorHeader header = await _client.GetReplayHeaderAsync(score.ReplayUrl).ConfigureAwait(false);
                if (header == null)
                {
                    warnings.Add("Could not parse replay " + score.ScoreId + ".");
                    return null;
                }

                _headerCache[score.ScoreId] = header;
                return header;
            }
            catch (Exception ex)
            {
                warnings.Add("Replay " + score.ScoreId + " failed: " + ex.Message);
                return null;
            }
        }

        public static string NormalizePlayerId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = input.Trim();
            int query = input.IndexOf('?');
            if (query >= 0) input = input.Substring(0, query);

            if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                int u = input.IndexOf("/u/", StringComparison.OrdinalIgnoreCase);
                if (u >= 0)
                {
                    string rest = input.Substring(u + 3).Trim('/');
                    int slash = rest.IndexOf('/');
                    return slash >= 0 ? rest.Substring(0, slash) : rest;
                }

                string[] parts = input.TrimEnd('/').Split('/');
                return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
            }

            return input;
        }
    }
}
