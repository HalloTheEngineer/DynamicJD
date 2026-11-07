using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicJD.Core
{
    public sealed class TypeCoefficients
    {
        public float A { get; set; }
        public float B { get; set; }
        public float C { get; set; }

        public TypeCoefficients()
        {
        }

        public TypeCoefficients(float a, float b = 0f, float c = 0f)
        {
            A = a;
            B = b;
            C = c;
        }

        public float Factor(float njs)
        {
            float z = (njs - 20f) / 10f;
            float factor = 1f + A + B * z + C * z * z;
            if (factor < 0.55f) return 0.55f;
            if (factor > 1.85f) return 1.85f;
            return factor;
        }
    }

    /// <summary>
    /// Hardcoded mathematical type model. Coefficients are fitted from the same public BeatLeader
    /// replay study as the base presets. A model only shapes the user's editable base config.
    /// </summary>
    public sealed class TypeModel
    {
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyDictionary<MapArchetype, TypeCoefficients> Coefficients { get; }

        public TypeModel(string name, string description,
            IReadOnlyDictionary<MapArchetype, TypeCoefficients> coefficients)
        {
            Name = name;
            Description = description;
            Coefficients = coefficients;
        }

        public float Factor(MapArchetype archetype, float njs)
        {
            if (Coefficients.TryGetValue(archetype, out TypeCoefficients coefficients))
            {
                return coefficients.Factor(njs);
            }

            return 1f;
        }

        public float BlendedFactor(MapTypeInfo type, float njs, float strength)
        {
            if (type == null) return 1f;
            float raw = RawFactor(type, njs);
            strength = Clamp01(strength);
            return 1f + (raw - 1f) * strength;
        }

        public float RawFactor(MapTypeInfo type, float njs)
        {
            if (type == null || !type.IsKnown || type.Archetypes == null || type.Archetypes.Count == 0)
            {
                return Factor(MapArchetype.Balanced, njs);
            }

            return RawFactor(type.Archetypes, njs);
        }

        public float RawFactor(IReadOnlyList<MapArchetype> archetypes, float njs)
        {
            if (archetypes == null || archetypes.Count == 0)
            {
                return Factor(MapArchetype.Balanced, njs);
            }

            var distinct = archetypes.Distinct().ToList();
            if (distinct.Count == 0) return 1f;
            return distinct.Sum(a => Factor(a, njs)) / distinct.Count;
        }

        public float TargetReactionTime(BaseJdConfig baseConfig, MapTypeInfo type, float njs, float strength)
        {
            return baseConfig.EvaluateReactionTime(njs) * BlendedFactor(type, njs, strength);
        }

        public float TargetJumpDistance(BaseJdConfig baseConfig, MapTypeInfo type, float njs, float strength)
        {
            return JumpDistanceMath.CalculateJumpDistanceFromReactionTime(
                TargetReactionTime(baseConfig, type, njs, strength), njs);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static class TypeModels
    {
        public const string ResearchName = "Adaptive";
        public const string NeutralName = "Uniform";

        public static readonly TypeModel Research = new TypeModel(
            ResearchName,
            "Research prior for BeatLeader and inferred map types: shorter on speed/linear maps, longer on accuracy/tech maps. Inferred types use metric/tag heuristics and may be less accurate.",
            new Dictionary<MapArchetype, TypeCoefficients>
            {
                [MapArchetype.Speed] = new TypeCoefficients(-0.056f, -0.010f),
                [MapArchetype.Linear] = new TypeCoefficients(-0.056f, -0.010f),
                [MapArchetype.Tech] = new TypeCoefficients(0.050f, -0.010f),
                [MapArchetype.Balanced] = new TypeCoefficients(0f),
                [MapArchetype.Fitbeat] = new TypeCoefficients(0.018f),
                [MapArchetype.BombReset] = new TypeCoefficients(-0.008f),
                [MapArchetype.Accuracy] = new TypeCoefficients(0.320f, -0.160f, 0.120f),
            });

        public static readonly TypeModel Neutral = new TypeModel(
            NeutralName,
            "Same base JD for every map type.",
            MapArchetypes.All.ToDictionary(a => a, a => new TypeCoefficients()));

        public static readonly IReadOnlyList<TypeModel> All = new List<TypeModel> { Research, Neutral };

        public static TypeModel Get(string name)
        {
            name = NormalizeName(name);
            TypeModel model = All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            return model ?? Research;
        }

        public static string NormalizeName(string name)
        {
            if (string.Equals(name, "Research", StringComparison.OrdinalIgnoreCase)) return ResearchName;
            if (string.Equals(name, "Neutral", StringComparison.OrdinalIgnoreCase)) return NeutralName;
            return name;
        }
    }
}
