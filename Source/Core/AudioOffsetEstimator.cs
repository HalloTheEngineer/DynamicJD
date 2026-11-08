using System;
using System.Collections.Generic;

namespace DynamicJD.Core
{
    public sealed class AudioOffsetSample
    {
        public float NoteTime { get; set; }
        public float DeviationMs { get; set; }
        public bool Accepted { get; set; }
    }

    public sealed class AudioOffsetResult
    {
        public IReadOnlyList<AudioOffsetSample> Samples { get; set; }
        public int TotalSamples { get; set; }
        public int AcceptedSamples { get; set; }
        public float MeanDeviationMs { get; set; }
        public float ConfidenceHalfWidthMs { get; set; }
        public float SuggestedDeltaMs { get; set; }
        public float LatencyAtStartMs { get; set; }
        public bool HasSuggestion { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Turns per-note timing deviations into a conservative audio-latency recommendation.
    /// Positive timeDeviation means the note was cut before its chart time, so the suggested
    /// latency delta is the negated mean deviation.
    /// </summary>
    public static class AudioOffsetEstimator
    {
        public const int DefaultMinSamples = 30;
        public const float DefaultMaxStepMs = 50f;
        public const float DeadbandMs = 5f;
        public const float MaxSampleDeviationMs = 300f;

        public static AudioOffsetResult Estimate(IReadOnlyList<AudioOffsetSample> samples,
            float latencyAtStartMs, int minSamples = DefaultMinSamples,
            float maxStepMs = DefaultMaxStepMs)
        {
            minSamples = Math.Max(2, minSamples);
            maxStepMs = Math.Max(1f, maxStepMs);

            var snapshot = new List<AudioOffsetSample>(samples?.Count ?? 0);
            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    AudioOffsetSample sample = samples[i];
                    snapshot.Add(new AudioOffsetSample
                    {
                        NoteTime = sample?.NoteTime ?? 0f,
                        DeviationMs = sample?.DeviationMs ?? 0f,
                    });
                }
            }

            var result = new AudioOffsetResult
            {
                Samples = snapshot,
                TotalSamples = snapshot.Count,
                LatencyAtStartMs = latencyAtStartMs,
            };

            if (snapshot.Count == 0)
            {
                result.Status = "No clean cuts were recorded.";
                return result;
            }

            var deviations = new List<float>(snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++)
            {
                AudioOffsetSample sample = snapshot[i];
                sample.Accepted = false;
                if (sample == null) continue;
                float value = sample.DeviationMs;
                if (float.IsNaN(value) || float.IsInfinity(value) ||
                    Math.Abs(value) > MaxSampleDeviationMs)
                {
                    continue;
                }
                deviations.Add(value);
            }

            if (deviations.Count < minSamples)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    AudioOffsetSample sample = snapshot[i];
                    if (sample == null) continue;
                    float value = sample.DeviationMs;
                    sample.Accepted = !float.IsNaN(value) && !float.IsInfinity(value) &&
                                      Math.Abs(value) <= MaxSampleDeviationMs;
                }
                result.AcceptedSamples = deviations.Count;
                result.Status = "Only " + deviations.Count + " usable cuts; need at least " +
                                minSamples + ".";
                return result;
            }

            float median = Median(deviations);
            var absoluteDeviations = new List<float>(deviations.Count);
            for (int i = 0; i < deviations.Count; i++)
            {
                absoluteDeviations.Add(Math.Abs(deviations[i] - median));
            }

            float mad = Median(absoluteDeviations);
            float robustSigma = mad * 1.4826f;
            float outlierThreshold = Math.Max(20f, robustSigma * 3f);

            var accepted = new List<float>(deviations.Count);
            for (int i = 0; i < snapshot.Count; i++)
            {
                AudioOffsetSample sample = snapshot[i];
                if (sample == null) continue;
                float value = sample.DeviationMs;
                if (float.IsNaN(value) || float.IsInfinity(value) ||
                    Math.Abs(value) > MaxSampleDeviationMs)
                {
                    continue;
                }
                bool isAccepted = Math.Abs(value - median) <= outlierThreshold;
                sample.Accepted = isAccepted;
                if (isAccepted) accepted.Add(value);
            }

            result.AcceptedSamples = accepted.Count;
            if (accepted.Count < minSamples)
            {
                result.Status = "Only " + accepted.Count + " consistent cuts; need at least " +
                                minSamples + ".";
                return result;
            }

            float mean = Mean(accepted);
            float standardDeviation = StandardDeviation(accepted, mean);
            float standardError = standardDeviation / (float)Math.Sqrt(accepted.Count);
            float confidenceHalfWidth = 1.96f * standardError;

            result.MeanDeviationMs = mean;
            result.ConfidenceHalfWidthMs = confidenceHalfWidth;

            bool confident = Math.Abs(mean) >= DeadbandMs &&
                             Math.Abs(mean) > confidenceHalfWidth;
            if (!confident)
            {
                result.Status = Math.Abs(mean) < DeadbandMs
                    ? "Average timing is already within +/-" + DeadbandMs.ToString("0") + " ms."
                    : "Timing spread is too high for a confident recommendation.";
                return result;
            }

            float rawDelta = -mean;
            bool capped = Math.Abs(rawDelta) > maxStepMs;
            float delta = Clamp(rawDelta, -maxStepMs, maxStepMs);

            result.HasSuggestion = true;
            result.SuggestedDeltaMs = delta;
            result.Status = "Cuts were " + (mean > 0f ? "early" : "late") + " by " +
                            Math.Abs(mean).ToString("0.0") + " ms on average." +
                            (capped ? " Adjustment capped at +/-" + maxStepMs.ToString("0") + " ms." : string.Empty);
            return result;
        }

        private static float Mean(List<float> values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return (float)(sum / values.Count);
        }

        private static float StandardDeviation(List<float> values, float mean)
        {
            if (values.Count < 2) return 0f;
            double sum = 0d;
            for (int i = 0; i < values.Count; i++)
            {
                double delta = values[i] - mean;
                sum += delta * delta;
            }
            return (float)Math.Sqrt(sum / (values.Count - 1));
        }

        private static float Median(List<float> values)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            int middle = sorted.Count / 2;
            if ((sorted.Count & 1) == 1) return sorted[middle];
            return (sorted[middle - 1] + sorted[middle]) * 0.5f;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
