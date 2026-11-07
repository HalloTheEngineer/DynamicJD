using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicJD.Core.Cloning
{
    public sealed class FitResult
    {
        public float Slope { get; set; }
        public float InterceptAtNjs20 { get; set; }
        public int Count { get; set; }

        public float Evaluate(float njs)
        {
            return InterceptAtNjs20 + Slope * (njs - 20f);
        }
    }

    /// <summary>Robust Theil-Sen line fit of reaction time against NJS.</summary>
    public static class CurveFitter
    {
        public static readonly float[] Grid = { 8f, 12f, 16f, 20f, 24f, 28f, 32f };

        public static FitResult Fit(IReadOnlyList<CloneSample> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return new FitResult { InterceptAtNjs20 = 250f, Slope = 0f, Count = 0 };
            }

            var points = samples
                .Where(s => s.Njs > 0f && s.ReactionTime > 0f)
                .Select(s => (x: s.Njs, y: s.ReactionTime))
                .ToList();

            if (points.Count == 0)
            {
                return new FitResult { InterceptAtNjs20 = 250f, Slope = 0f, Count = 0 };
            }

            var slopes = new List<float>();
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    float dx = points[j].x - points[i].x;
                    if (Math.Abs(dx) < 0.5f) continue;
                    slopes.Add((points[j].y - points[i].y) / dx);
                }
            }

            float slope = slopes.Count > 0 ? Median(slopes) : 0f;
            slope = Clamp(slope, -5f, 5f);

            var intercepts = points.Select(p => p.y - slope * (p.x - 20f)).ToList();
            float intercept = Median(intercepts);
            intercept = Clamp(intercept, 100f, 900f);

            return new FitResult
            {
                Slope = slope,
                InterceptAtNjs20 = intercept,
                Count = points.Count,
            };
        }

        public static JdCurve ToCurve(FitResult fit)
        {
            return new JdCurve(Grid.Select(njs =>
                new CurveAnchor(njs, JumpDistanceMath.CalculateJumpDistanceFromReactionTime(fit.Evaluate(njs), njs))));
        }

        public static float Median(List<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) * 0.5f : values[mid];
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
