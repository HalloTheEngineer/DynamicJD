using System;
using System.Collections.Generic;
using System.Linq;
using DynamicJD.Core;

namespace DynamicJD.Core.Cloning
{
    public enum ProfileCurveKind
    {
        Flat,
        Linear,
        Quadratic,
    }

    public sealed class ProfileFitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public BaseJdConfig Base { get; set; }
        public ProfileCurveKind Curve { get; set; }
        public float RecommendedTypeStrength { get; set; }
        public int SampleCount { get; set; }
        public float CvMaeMs { get; set; }
        public float CvRmseMs { get; set; }
        public float CvR2 { get; set; }
        public float NjsMin { get; set; }
        public float NjsMax { get; set; }
        public float NjsIqr { get; set; }
        public float SlopeCiLow { get; set; }
        public float SlopeCiHigh { get; set; }
        public float Confidence { get; set; }
    }

    /// <summary>
    /// Selects a flat, linear, or quadratic reaction-time curve and a per-player type strength using
    /// cross-validation. Replay RT is normalised by the type factor before fitting, preventing type
    /// composition from being absorbed into the base slope.
    /// </summary>
    public static class ProfileFitter
    {
        private const int FoldCount = 5;
        private const float TypeStrengthStep = 0.05f;
        private const float LinearGainThreshold = 0.02f;
        private const float QuadraticGainThreshold = 0.03f;
        private const float TypeGainThreshold = 0.02f;
        private const float EpsilonMs = 0.05f;

        public static ProfileFitResult Fit(IReadOnlyList<CloneSample> samples, TypeModel typeModel = null)
        {
            typeModel = typeModel ?? TypeModels.Research;
            var valid = samples == null
                ? new List<CloneSample>()
                : samples.Where(s => s != null && s.Njs > 0f && s.ReactionTime > 0f).ToList();

            var result = new ProfileFitResult();
            if (valid.Count < 4)
            {
                result.Message = "Not enough clean replay samples to fit a profile.";
                return result;
            }

            float[] x = valid.Select(s => s.Njs).ToArray();
            float[] y = valid.Select(s => s.ReactionTime).ToArray();
            float[] rawFactors = valid.Select(s =>
            {
                IReadOnlyList<MapArchetype> archetypes = s.Archetypes;
                if (archetypes == null || archetypes.Count == 0)
                {
                    archetypes = MapArchetypes.FromMask(s.TypeMask).ToList();
                }
                return typeModel.RawFactor(archetypes, s.Njs);
            }).ToArray();

            int folds = Math.Min(FoldCount, valid.Count);
            if (folds < 2)
            {
                result.Message = "Not enough samples for cross-validation.";
                return result;
            }

            var strengths = new List<float>();
            for (float strength = 0f; strength <= 1.0001f; strength += TypeStrengthStep)
            {
                strengths.Add(Math.Min(1f, strength));
            }

            var candidates = new List<Candidate>();
            foreach (float strength in strengths)
            {
                float[] factors = Factor(rawFactors, strength);
                foreach (ProfileCurveKind curve in new[] { ProfileCurveKind.Flat, ProfileCurveKind.Linear, ProfileCurveKind.Quadratic })
                {
                    if (curve == ProfileCurveKind.Quadratic && valid.Count < 20) continue;
                    double[] errors = CrossValidate(x, y, factors, curve, folds);
                    if (errors == null) continue;
                    candidates.Add(new Candidate
                    {
                        Curve = curve,
                        Strength = strength,
                        Mae = (float)errors[0],
                        Rmse = (float)errors[1],
                        R2 = (float)errors[2],
                    });
                }
            }

            if (candidates.Count == 0)
            {
                result.Message = "Profile fitting failed.";
                return result;
            }

            Candidate flat = BestForCurve(candidates, ProfileCurveKind.Flat);
            Candidate linear = BestForCurve(candidates, ProfileCurveKind.Linear);
            Candidate quadratic = BestForCurve(candidates, ProfileCurveKind.Quadratic);
            Candidate chosen = flat;

            if (linear != null &&
                linear.Mae <= flat.Mae * (1f - LinearGainThreshold) &&
                linear.Mae < flat.Mae - EpsilonMs)
            {
                chosen = linear;
            }

            if (quadratic != null &&
                quadratic.Mae <= chosen.Mae * (1f - QuadraticGainThreshold) &&
                quadratic.Mae < chosen.Mae - EpsilonMs)
            {
                chosen = quadratic;
            }

            Candidate zeroType = candidates.FirstOrDefault(c => c.Curve == chosen.Curve && c.Strength <= 0.0001f);
            if (zeroType != null &&
                (chosen.Mae >= zeroType.Mae * (1f - TypeGainThreshold) ||
                 chosen.Mae >= zeroType.Mae - EpsilonMs))
            {
                chosen = zeroType;
            }

            float[] finalFactors = Factor(rawFactors, chosen.Strength);
            double[] coefficients = FitCurve(x, Normalize(y, finalFactors), chosen.Curve);
            if (coefficients == null)
            {
                result.Message = "Profile fit was singular.";
                return result;
            }

            float njsIqr = Percentile(x, 0.75f) - Percentile(x, 0.25f);
            var slopeCi = SlopeConfidence(x, y, finalFactors);
            if (chosen.Curve == ProfileCurveKind.Linear && slopeCi.Low <= 0f && slopeCi.High >= 0f)
            {
                chosen = flat;
                coefficients = FitCurve(x, Normalize(y, Factor(rawFactors, chosen.Strength)), chosen.Curve);
                slopeCi = new Interval(0f, 0f);
            }

            var baseConfig = new BaseJdConfig
            {
                Rt20 = (float)coefficients[0],
                SlopeMsPerNjs = chosen.Curve == ProfileCurveKind.Flat ? 0f : (float)coefficients[1],
                CurvatureMsPerNjs2 = chosen.Curve == ProfileCurveKind.Quadratic ? (float)coefficients[2] : 0f,
            };
            baseConfig.EnsureValid();

            float spread = StandardDeviation(y);
            float confidence = Confidence(valid.Count, njsIqr, chosen.Mae, spread, chosen.Curve, slopeCi);

            result.Success = true;
            result.Base = baseConfig;
            result.Curve = chosen.Curve;
            result.RecommendedTypeStrength = chosen.Strength;
            result.SampleCount = valid.Count;
            result.CvMaeMs = chosen.Mae;
            result.CvRmseMs = chosen.Rmse;
            result.CvR2 = chosen.R2;
            result.NjsMin = x.Min();
            result.NjsMax = x.Max();
            result.NjsIqr = njsIqr;
            result.SlopeCiLow = slopeCi.Low;
            result.SlopeCiHigh = slopeCi.High;
            result.Confidence = confidence;
            result.Summary = CurveName(chosen.Curve) +
                             " · type " + Math.Round(chosen.Strength * 100f) + "%" +
                             " · CV ±" + Math.Round(chosen.Mae) + " ms" +
                             " · NJS " + x.Min().ToString("0.#") + "-" + x.Max().ToString("0.#");
            result.Message = "Fitted " + CurveName(chosen.Curve).ToLowerInvariant() + " profile from " +
                             valid.Count + " clean plays.";
            return result;
        }

        private sealed class Candidate
        {
            public ProfileCurveKind Curve;
            public float Strength;
            public float Mae;
            public float Rmse;
            public float R2;
        }

        private struct Interval
        {
            public float Low;
            public float High;

            public Interval(float low, float high)
            {
                Low = low;
                High = high;
            }
        }

        private static Candidate BestForCurve(IEnumerable<Candidate> candidates, ProfileCurveKind curve)
        {
            return candidates
                .Where(c => c.Curve == curve)
                .OrderBy(c => c.Mae)
                .FirstOrDefault();
        }

        private static float[] Normalize(float[] values, float[] factors)
        {
            var normalized = new float[values.Length];
            for (int i = 0; i < values.Length; i++) normalized[i] = values[i] / factors[i];
            return normalized;
        }

        private static float[] Factor(float[] rawFactors, float strength)
        {
            var factors = new float[rawFactors.Length];
            for (int i = 0; i < factors.Length; i++)
            {
                factors[i] = 1f + (rawFactors[i] - 1f) * strength;
            }
            return factors;
        }

        private static double[] CrossValidate(float[] x, float[] y, float[] factors, ProfileCurveKind curve, int folds)
        {
            var order = Enumerable.Range(0, x.Length).ToArray();
            var random = new Random(9173 + x.Length);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temp = order[i];
                order[i] = order[j];
                order[j] = temp;
            }

            var predicted = new double[x.Length];
            var actual = new double[x.Length];
            var completed = new bool[x.Length];
            for (int fold = 0; fold < folds; fold++)
            {
                var train = new List<int>();
                var test = new List<int>();
                for (int i = 0; i < order.Length; i++)
                {
                    if (i % folds == fold) test.Add(order[i]);
                    else train.Add(order[i]);
                }
                if (train.Count < 3 || test.Count == 0) continue;

                float[] tx = train.Select(i => x[i]).ToArray();
                float[] ty = train.Select(i => y[i] / factors[i]).ToArray();
                double[] coefficients = FitCurve(tx, ty, curve);
                if (coefficients == null) continue;

                foreach (int index in test)
                {
                    predicted[index] = Evaluate(coefficients, x[index]) * factors[index];
                    actual[index] = y[index];
                    completed[index] = predicted[index] > 0d;
                }
            }

            double sumAbs = 0d;
            double sumSq = 0d;
            double sum = 0d;
            int count = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                if (!completed[i]) continue;
                sum += actual[i];
                count++;
            }
            if (count == 0) return null;
            double mean = sum / count;
            double total = 0d;
            for (int i = 0; i < actual.Length; i++)
            {
                if (!completed[i]) continue;
                double error = predicted[i] - actual[i];
                sumAbs += Math.Abs(error);
                sumSq += error * error;
                total += (actual[i] - mean) * (actual[i] - mean);
            }
            return new[]
            {
                sumAbs / count,
                Math.Sqrt(sumSq / count),
                total > 0d ? 1d - sumSq / total : 0d,
            };
        }

        private static double[] FitCurve(float[] x, float[] y, ProfileCurveKind curve)
        {
            switch (curve)
            {
                case ProfileCurveKind.Flat:
                    return new[] { (double)Median(y) };
                case ProfileCurveKind.Quadratic:
                    return FitQuadraticRobust(x, y);
                default:
                    return FitLinearTheilSen(x, y);
            }
        }

        private static double[] FitLinearTheilSen(float[] x, float[] y)
        {
            var slopes = new List<float>();
            for (int i = 0; i < x.Length; i++)
            {
                for (int j = i + 1; j < x.Length; j++)
                {
                    float dx = x[j] - x[i];
                    if (Math.Abs(dx) < 0.25f) continue;
                    slopes.Add((y[j] - y[i]) / dx);
                }
            }
            float slope = slopes.Count > 0 ? Median(slopes) : 0f;
            var intercepts = new List<float>(x.Length);
            for (int i = 0; i < x.Length; i++)
            {
                intercepts.Add(y[i] - slope * (x[i] - 20f));
            }
            return new[] { (double)Median(intercepts), (double)slope };
        }

        private static double[] FitQuadraticRobust(float[] x, float[] y)
        {
            double[,] design = Design(x, 2);
            double[] weights = Enumerable.Repeat(1d, x.Length).ToArray();
            double[] coefficients = null;
            for (int iteration = 0; iteration < 7; iteration++)
            {
                coefficients = WeightedSolve(design, y, weights);
                if (coefficients == null) return null;
                double[] residuals = new double[x.Length];
                for (int i = 0; i < x.Length; i++)
                {
                    double predicted = coefficients[0] + coefficients[1] * (x[i] - 20d) +
                                       coefficients[2] * (x[i] - 20d) * (x[i] - 20d);
                    residuals[i] = y[i] - predicted;
                }
                double scale = 1.4826d * Median(residuals.Select(r => Math.Abs(r)).ToArray());
                if (scale < 0.15d) break;
                for (int i = 0; i < residuals.Length; i++)
                {
                    double ratio = Math.Abs(residuals[i]) / (1.345d * scale);
                    weights[i] = ratio <= 1d ? 1d : 1d / ratio;
                }
            }
            return coefficients;
        }

        private static double[,] Design(float[] x, int degree)
        {
            var design = new double[x.Length, degree + 1];
            for (int i = 0; i < x.Length; i++)
            {
                double offset = x[i] - 20d;
                double value = 1d;
                for (int d = 0; d <= degree; d++)
                {
                    design[i, d] = value;
                    value *= offset;
                }
            }
            return design;
        }

        private static double[] WeightedSolve(double[,] design, float[] y, double[] weights)
        {
            int columns = design.GetLength(1);
            var matrix = new double[columns, columns];
            var vector = new double[columns];
            for (int row = 0; row < design.GetLength(0); row++)
            {
                double weight = weights[row];
                if (weight <= 0d) continue;
                for (int a = 0; a < columns; a++)
                {
                    vector[a] += design[row, a] * y[row] * weight;
                    for (int b = 0; b < columns; b++)
                    {
                        matrix[a, b] += design[row, a] * design[row, b] * weight;
                    }
                }
            }
            return Solve(matrix, vector);
        }

        private static double[] Solve(double[,] matrix, double[] vector)
        {
            int n = vector.Length;
            for (int column = 0; column < n; column++)
            {
                int pivot = column;
                for (int row = column + 1; row < n; row++)
                {
                    if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
                }
                if (Math.Abs(matrix[pivot, column]) < 1e-8d) return null;
                if (pivot != column)
                {
                    for (int k = 0; k < n; k++)
                    {
                        double temp = matrix[column, k];
                        matrix[column, k] = matrix[pivot, k];
                        matrix[pivot, k] = temp;
                    }
                    double tempVector = vector[column];
                    vector[column] = vector[pivot];
                    vector[pivot] = tempVector;
                }

                double divisor = matrix[column, column];
                for (int k = column; k < n; k++) matrix[column, k] /= divisor;
                vector[column] /= divisor;
                for (int row = 0; row < n; row++)
                {
                    if (row == column) continue;
                    double factor = matrix[row, column];
                    if (Math.Abs(factor) < 1e-12d) continue;
                    for (int k = column; k < n; k++) matrix[row, k] -= factor * matrix[column, k];
                    vector[row] -= factor * vector[column];
                }
            }
            return vector;
        }

        private static double Evaluate(double[] coefficients, float x)
        {
            double offset = x - 20d;
            double result = 0d;
            double value = 1d;
            foreach (double coefficient in coefficients)
            {
                result += coefficient * value;
                value *= offset;
            }
            return result;
        }

        private static Interval SlopeConfidence(float[] x, float[] y, float[] factors)
        {
            int n = x.Length;
            if (n < 4) return new Interval(0f, 0f);
            var design = Design(x, 1);
            var weights = Enumerable.Repeat(1d, n).ToArray();
            double[] coefficients = WeightedSolve(design, y.Select((value, index) => (float)(value / factors[index])).ToArray(), weights);
            if (coefficients == null) return new Interval(0f, 0f);

            double meanX = x.Average();
            double sxx = x.Sum(value => (value - meanX) * (value - meanX));
            if (sxx <= 0d) return new Interval(0f, 0f);
            double sse = 0d;
            for (int i = 0; i < n; i++)
            {
                double predicted = coefficients[0] + coefficients[1] * (x[i] - 20d);
                double actual = y[i] / factors[i];
                sse += (actual - predicted) * (actual - predicted);
            }
            double standardError = Math.Sqrt(sse / Math.Max(1, n - 2) / sxx);
            double t = n > 30 ? 1.96d : 2.1d;
            return new Interval(
                (float)(coefficients[1] - t * standardError),
                (float)(coefficients[1] + t * standardError));
        }

        private static float Confidence(int count, float njsIqr, float mae, float spread,
            ProfileCurveKind curve, Interval slope)
        {
            float sampleTerm = Clamp01((count - 10f) / 90f);
            float errorTerm = spread > 1f ? Clamp01(1f - mae / spread) : 0.5f;
            float rangeTerm = Clamp01((njsIqr - 0.5f) / 2.5f);
            float shapeTerm = 0.7f;
            if (curve != ProfileCurveKind.Flat)
            {
                float width = stripeWidth(slope);
                float slopeMagnitude = Math.Max(0.5f, Math.Abs(slope.Low + slope.High) * 0.5f);
                shapeTerm = Clamp01(1f - width / (slopeMagnitude * 2f + 1f));
            }
            return Clamp01(0.35f * sampleTerm + 0.35f * errorTerm + 0.20f * rangeTerm + 0.10f * shapeTerm);
        }

        private static float stripeWidth(Interval interval)
        {
            return Math.Abs(interval.High - interval.Low);
        }

        private static string CurveName(ProfileCurveKind curve)
        {
            switch (curve)
            {
                case ProfileCurveKind.Flat: return "Flat";
                case ProfileCurveKind.Quadratic: return "Quadratic";
                default: return "Linear";
            }
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] ordered = values.OrderBy(v => v).ToArray();
            if (ordered.Length == 0) return 0f;
            int middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) * 0.5f
                : ordered[middle];
        }

        private static double Median(IEnumerable<double> values)
        {
            double[] ordered = values.OrderBy(v => v).ToArray();
            if (ordered.Length == 0) return 0d;
            int middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) * 0.5d
                : ordered[middle];
        }

        private static float Percentile(float[] values, float percentile)
        {
            float[] ordered = values.OrderBy(v => v).ToArray();
            if (ordered.Length == 0) return 0f;
            float position = percentile * (ordered.Length - 1);
            int lower = (int)position;
            int upper = Math.Min(ordered.Length - 1, lower + 1);
            float weight = position - lower;
            return ordered[lower] * (1f - weight) + ordered[upper] * weight;
        }

        private static float StandardDeviation(float[] values)
        {
            if (values.Length < 2) return 0f;
            float mean = values.Average();
            double sum = values.Sum(value => (value - mean) * (double)(value - mean));
            return (float)Math.Sqrt(sum / (values.Length - 1));
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
