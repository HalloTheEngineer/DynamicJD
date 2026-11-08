using System.Collections.Generic;
using DynamicJD.Core;
using Xunit;

namespace DynamicJD.Tests
{
    public class AudioOffsetTests
    {
        [Fact]
        public void LateCuts_RecommendMoreLatency()
        {
            AudioOffsetResult result = Estimate(-50f, 40);

            Assert.True(result.HasSuggestion);
            Assert.Equal(40, result.AcceptedSamples);
            Assert.Equal(50f, result.SuggestedDeltaMs, 2);
        }

        [Fact]
        public void EarlyCuts_RecommendLessLatency()
        {
            AudioOffsetResult result = Estimate(40f, 40);

            Assert.True(result.HasSuggestion);
            Assert.Equal(-40f, result.SuggestedDeltaMs, 2);
        }

        [Fact]
        public void InsufficientSamples_DoNotRecommend()
        {
            AudioOffsetResult result = Estimate(-50f, 20);

            Assert.False(result.HasSuggestion);
            Assert.Equal(20, result.AcceptedSamples);
        }

        [Fact]
        public void Outliers_AreExcludedFromTheRecommendation()
        {
            var samples = NewSamples(-30f, 40);
            samples.AddRange(NewSamples(250f, 6));

            AudioOffsetResult result = AudioOffsetEstimator.Estimate(samples, 0f);

            Assert.True(result.HasSuggestion);
            Assert.Equal(40, result.AcceptedSamples);
            Assert.Equal(30f, result.SuggestedDeltaMs, 1);
        }

        [Fact]
        public void HighVariance_DoesNotRecommend()
        {
            var samples = new List<AudioOffsetSample>();
            for (int i = 0; i < 40; i++)
            {
                samples.Add(new AudioOffsetSample
                {
                    NoteTime = i,
                    DeviationMs = (i & 1) == 0 ? -150f : 150f,
                });
            }

            AudioOffsetResult result = AudioOffsetEstimator.Estimate(samples, 0f);

            Assert.False(result.HasSuggestion);
        }

        [Fact]
        public void Deadband_DoesNotRecommendTinyCorrection()
        {
            AudioOffsetResult result = Estimate(-4f, 40);

            Assert.False(result.HasSuggestion);
        }

        [Fact]
        public void LargeCorrection_IsCapped()
        {
            AudioOffsetResult result = Estimate(-80f, 40);

            Assert.True(result.HasSuggestion);
            Assert.Equal(50f, result.SuggestedDeltaMs, 2);
            Assert.Contains("capped", result.Status);
        }

        [Fact]
        public void Result_KeepsItsOwnSampleSnapshot()
        {
            var samples = NewSamples(-50f, 40);
            AudioOffsetResult result = AudioOffsetEstimator.Estimate(samples, 0f);
            samples.Clear();

            Assert.Equal(40, result.Samples.Count);
            Assert.True(result.HasSuggestion);
        }

        private static AudioOffsetResult Estimate(float deviationMs, int count)
        {
            return AudioOffsetEstimator.Estimate(NewSamples(deviationMs, count), 0f);
        }

        private static List<AudioOffsetSample> NewSamples(float deviationMs, int count)
        {
            var samples = new List<AudioOffsetSample>(count);
            for (int i = 0; i < count; i++)
            {
                samples.Add(new AudioOffsetSample
                {
                    NoteTime = i * 0.5f,
                    DeviationMs = deviationMs,
                });
            }
            return samples;
        }
    }
}
