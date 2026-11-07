using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicJD.Api;
using DynamicJD.Core;
using DynamicJD.Core.Cloning;

namespace DynamicJD.Services
{
    /// <summary>
    /// Builds the Suggested profile from the player's own public BeatLeader plays. The estimate is
    /// deliberately conservative: performance-weighted observed RT is blended with a skill-tier prior,
    /// and sparse data lowers confidence instead of producing an aggressive result.
    /// </summary>
    public sealed class SuggestedProfileService
    {
        private readonly BeatLeaderClient _client;

        public SuggestedProfileService(BeatLeaderClient client)
        {
            _client = client;
        }

        public async Task<JdProfile> AnalyzeAsync(string playerId, string playerName, int maxScores)
        {
            playerId = (playerId ?? string.Empty).Trim();
            playerName = (playerName ?? string.Empty).Trim();
            if (string.Equals(playerName, "<NO NAME!>", StringComparison.OrdinalIgnoreCase)) playerName = string.Empty;
            if (string.IsNullOrEmpty(playerId) && string.IsNullOrEmpty(playerName))
            {
                throw new InvalidOperationException("No BeatLeader player ID or player name was available.");
            }

            List<PlayerScore> scores = null;
            bool scoreRequestSucceeded = false;
            string lastError = null;

            if (!string.IsNullOrEmpty(playerId))
            {
                try
                {
                    scores = await _client.GetPlayerScoresAsync(playerId, maxScores).ConfigureAwait(false);
                    scoreRequestSucceeded = true;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    Plugin.Log?.Warn("Suggested profile scores failed for " + playerId + ": " + ex.Message);
                }
            }

            if ((scores == null || scores.Count == 0) && !string.IsNullOrEmpty(playerName))
            {
                try
                {
                    List<PlayerSearchResult> matches = await _client.SearchPlayersAsync(playerName, 8).ConfigureAwait(false);
                    PlayerSearchResult match = matches.FirstOrDefault(m =>
                        string.Equals(m.Name, playerName, StringComparison.OrdinalIgnoreCase)) ?? matches.FirstOrDefault();
                    if (match != null)
                    {
                        playerId = match.Id;
                        playerName = match.Name;
                        scores = await _client.GetPlayerScoresAsync(playerId, maxScores).ConfigureAwait(false);
                        scoreRequestSucceeded = true;
                    }
                    else
                    {
                        lastError ??= "No BeatLeader player found for '" + playerName + "'.";
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    Plugin.Log?.Warn("Suggested profile name lookup failed for " + playerName + ": " + ex.Message);
                }
            }

            if (scores == null && !scoreRequestSucceeded)
            {
                throw new InvalidOperationException("Could not load BeatLeader scores: " + (lastError ?? "unknown error"));
            }

            scores ??= new List<PlayerScore>();

            PlayerSummary summary = null;
            try
            {
                summary = await _client.GetPlayerAsync(playerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Suggested profile summary failed for " + playerId + ": " + ex.Message);
            }
            var samples = new List<Sample>();

            var tasks = scores
                .Where(s => !string.IsNullOrEmpty(s.ReplayUrl) && s.Njs > 0f &&
                            !ScoreModifierRules.HasSpeedModifier(s.Modifiers))
                .Select(async score =>
                {
                    try
                    {
                        BsorHeader header = await _client.GetReplayHeaderAsync(score.ReplayUrl).ConfigureAwait(false);
                        if (header == null || header.JumpDistance <= 0f) return (Sample)null;

                        float rt = JumpDistanceMath.CalculateReactionTime(header.JumpDistance, score.Njs);
                        if (rt <= 30f || rt > 2000f) return null;

                        float accuracy = Math.Max(0f, Math.Min(1f, score.Accuracy));
                        float accWeight = Clamp01((accuracy - 0.70f) / 0.28f);
                        float missWeight = 1f / (1f + score.MissedNotes + score.BadCuts * 0.5f);
                        float fcWeight = score.FullCombo ? 1f : 0f;
                        float weight = 0.55f * accWeight + 0.30f * missWeight + 0.15f * fcWeight;
                        if (weight <= 0.02f) return null;

                        return new Sample
                        {
                            Njs = score.Njs,
                            Rt = rt,
                            Weight = weight,
                            Accuracy = accuracy,
                            Misses = score.MissedNotes + score.BadCuts,
                        };
                    }
                    catch
                    {
                        return null;
                    }
                });

            Sample[] analyzed = await Task.WhenAll(tasks).ConfigureAwait(false);
            samples.AddRange(analyzed.Where(s => s != null));

            return BuildProfile(playerId, summary, samples, playerName);
        }

        private static JdProfile BuildProfile(string playerId, PlayerSummary summary, List<Sample> samples, string fallbackName)
        {
            float priorRt20 = PriorRt20(summary?.Rank ?? 0);
            const float priorSlope = -1.8f;

            float rt20 = priorRt20;
            float slope = priorSlope;
            float confidence = 0f;

            if (samples.Count >= 4)
            {
                float totalWeight = samples.Sum(s => s.Weight);
                if (totalWeight > 0f)
                {
                    float meanX = samples.Sum(s => s.Njs * s.Weight) / totalWeight;
                    float meanY = samples.Sum(s => s.Rt * s.Weight) / totalWeight;
                    float covariance = samples.Sum(s => s.Weight * (s.Njs - meanX) * (s.Rt - meanY));
                    float variance = samples.Sum(s => s.Weight * (s.Njs - meanX) * (s.Njs - meanX));

                    float observedSlope = variance > 0.001f ? covariance / variance : priorSlope;
                    observedSlope = Clamp(observedSlope, -5f, 2f);
                    float observedRt20 = meanY - observedSlope * (meanX - 20f);

                    confidence = Clamp01(samples.Count / 30f) * 0.8f + 0.2f;
                    rt20 = observedRt20 * confidence + priorRt20 * (1f - confidence);
                    slope = observedSlope * confidence + priorSlope * (1f - confidence);
                }
            }
            else
            {
                confidence = samples.Count / 4f * 0.2f;
            }

            float averageMisses = samples.Count > 0 ? (float)samples.Average(s => s.Misses) : 0f;
            if (averageMisses >= 3f) rt20 += 10f;
            else if (averageMisses <= 0.2f && samples.Count >= 10) rt20 -= 5f;

            rt20 = Clamp(rt20, 170f, 350f);
            slope = Clamp(slope, -5f, 2f);

            string playerName = summary?.Name ?? (string.IsNullOrEmpty(fallbackName) ? playerId : fallbackName);
            return new JdProfile
            {
                Name = DefaultProfiles.SuggestedName,
                Role = ProfileRole.Suggested,
                Source = "suggested",
                PlayerId = playerId,
                PlayerName = playerName,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                LastAnalyzedAt = DateTime.UtcNow.ToString("o"),
                SampleCount = samples.Count,
                Confidence = confidence,
                Notes = samples.Count == 0
                    ? "Not enough public replay data yet. Play a few maps with BeatLeader replays enabled."
                    : "Based on " + samples.Count + " of " + playerName + "'s public plays. " +
                      "Confidence: " + ConfidenceLabel(confidence) + ".",
                Base = new BaseJdConfig { Rt20 = rt20, SlopeMsPerNjs = slope },
            };
        }

        private static float PriorRt20(int rank)
        {
            if (rank <= 0) return 220f;
            if (rank <= 1000) return 203f;
            if (rank <= 5000) return 220f;
            if (rank <= 20000) return 250f;
            return 280f;
        }

        private static string ConfidenceLabel(float confidence)
        {
            if (confidence >= 0.7f) return "high";
            if (confidence >= 0.4f) return "medium";
            return "low";
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private sealed class Sample
        {
            public float Njs;
            public float Rt;
            public float Weight;
            public float Accuracy;
            public float Misses;
        }
    }
}
