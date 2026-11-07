using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicJD.Core
{
    public sealed class JdProfile
    {
        public string Name { get; set; } = "Default";
        public ProfileRole? Role { get; set; }
        public string Source { get; set; } = "built-in";
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public float Confidence { get; set; }
        public string LastAnalyzedAt { get; set; } = string.Empty;
        public float RecommendedTypeStrength { get; set; } = -1f;

        /// <summary>The editable user configuration. Type shaping is applied on top of this.</summary>
        public BaseJdConfig Base { get; set; }

        // Retained only so profiles written by the first DynamicJD build can be migrated.
        public Dictionary<string, JdCurve> Curves { get; set; }
        public JdCurve Global { get; set; }

        public void EnsureValid()
        {
            if (Base == null)
            {
                Base = new BaseJdConfig();
                TryMigrateLegacyCurve(Base);
                Curves = null;
                Global = null;
            }

            Base.EnsureValid();
        }

        public JdProfile Clone(string newName)
        {
            EnsureValid();
            return new JdProfile
            {
                Name = newName,
                Role = Role,
                Source = Source,
                PlayerId = PlayerId,
                PlayerName = PlayerName,
                CreatedAt = CreatedAt,
                Notes = Notes,
                SampleCount = SampleCount,
                Confidence = Confidence,
                LastAnalyzedAt = LastAnalyzedAt,
                RecommendedTypeStrength = RecommendedTypeStrength,
                Base = Base.Clone(),
            };
        }

        private void TryMigrateLegacyCurve(BaseJdConfig target)
        {
            JdCurve legacy = Global;
            if ((legacy == null || legacy.Anchors == null || legacy.Anchors.Count == 0) &&
                Curves != null && Curves.TryGetValue(MapArchetype.Balanced.ToString(), out JdCurve balanced))
            {
                legacy = balanced;
            }

            if (legacy == null || legacy.Anchors == null || legacy.Anchors.Count == 0)
            {
                return;
            }

            float rt12 = JumpDistanceMath.CalculateReactionTime(legacy.Evaluate(12f), 12f);
            float rt20 = JumpDistanceMath.CalculateReactionTime(legacy.Evaluate(20f), 20f);
            float rt28 = JumpDistanceMath.CalculateReactionTime(legacy.Evaluate(28f), 28f);
            if (rt12 <= 0f || rt20 <= 0f || rt28 <= 0f) return;

            float slope = (rt28 - rt12) / 16f;
            target.SlopeMsPerNjs = slope;
            target.Rt20 = Math.Max(20f, rt20);
        }
    }

    public sealed class MapOverride
    {
        public float TargetJumpDistance { get; set; }
        public float Strength { get; set; } = -1f;
        public string Archetype { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;

        public bool HasTarget => TargetJumpDistance > 0.01f;
        public bool HasArchetype => !string.IsNullOrEmpty(Archetype) && Enum.TryParse(Archetype, out MapArchetype _);
    }

    public sealed class ResolvedJd
    {
        public float Bpm { get; set; }
        public float Njs { get; set; }
        public float MapOffset { get; set; }
        public float OriginalJumpDistance { get; set; }
        public float TargetJumpDistance { get; set; }
        public float BeatOffset { get; set; }
        public float ReactionTime { get; set; }
        public float BaseReactionTime { get; set; }
        public float TypeFactor { get; set; } = 1f;
        public float MinimumJumpDistance { get; set; }
        public float MaximumJumpDistance { get; set; }
        public bool ManualOverride { get; set; }
        public string Source { get; set; } = "map";
        public MapTypeInfo Type { get; set; }
        public MapArchetype Archetype { get; set; }
    }
}
