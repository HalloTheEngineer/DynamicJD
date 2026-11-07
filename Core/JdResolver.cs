using System;

namespace DynamicJD.Core
{
    public sealed class ResolveContext
    {
        public bool Enabled { get; set; } = true;
        public float Bpm { get; set; }
        public float Njs { get; set; }
        public float MapOffset { get; set; }
        public float OriginalJumpDistance { get; set; }
        public MapTypeInfo Type { get; set; }
        public JdProfile Profile { get; set; }
        public TypeModel TypeModel { get; set; }
        public float TypeModelStrength { get; set; } = 1f;
        public float ApplyStrength { get; set; } = 1f;
        public MapOverride Override { get; set; }
        public bool ManualOverride { get; set; }
        public float ManualJumpDistance { get; set; }
        public float MinJumpDistance { get; set; } = 2f;
        public float MaxJumpDistance { get; set; } = 60f;
        public bool PreserveReactionTime { get; set; }
        public float SongSpeedMultiplier { get; set; } = 1f;
        public float NjsGainMultiplier { get; set; }
    }

    public static class JdResolver
    {
        public static ResolvedJd Resolve(ResolveContext ctx)
        {
            float bpm = ctx.Bpm;
            float njs = JumpDistanceMath.SanitizeNjs(ctx.Njs);
            float original = ctx.OriginalJumpDistance > 0f
                ? ctx.OriginalJumpDistance
                : JumpDistanceMath.CalculateJumpDistance(bpm, njs, ctx.MapOffset);
            float min = JumpDistanceMath.MinimumJumpDistance(bpm, njs);
            float max = Math.Max(min, ctx.MaxJumpDistance > 0f ? ctx.MaxJumpDistance : 60f);
            min = Math.Max(min, Math.Max(0f, ctx.MinJumpDistance));

            MapTypeInfo type = ctx.Type ?? MapTypeInfo.Unknown(njs);
            float target = original;
            float baseRt = 0f;
            float factor = 1f;
            bool manual = false;
            string source = "map default";

            if (!ctx.Enabled)
            {
                source = "disabled";
            }
            else if (ctx.Override != null && ctx.Override.HasTarget)
            {
                target = ctx.Override.TargetJumpDistance;
                source = "per-map override";
                if (ctx.Profile != null) baseRt = ctx.Profile.Base.EvaluateReactionTime(njs);
                if (ctx.TypeModel != null) factor = ctx.TypeModel.BlendedFactor(type, njs, ctx.TypeModelStrength);
            }
            else if (ctx.ManualOverride)
            {
                target = ctx.ManualJumpDistance;
                manual = true;
                source = "manual override";
                if (ctx.Profile != null) baseRt = ctx.Profile.Base.EvaluateReactionTime(njs);
                if (ctx.TypeModel != null) factor = ctx.TypeModel.BlendedFactor(type, njs, ctx.TypeModelStrength);
            }
            else if (ctx.Profile != null)
            {
                ctx.Profile.EnsureValid();
                BaseJdConfig baseConfig = ctx.Profile.Base;
                TypeModel model = ctx.TypeModel ?? TypeModels.Research;
                baseRt = baseConfig.EvaluateReactionTime(njs);
                factor = model.BlendedFactor(type, njs, ctx.TypeModelStrength);
                float modelTarget = JumpDistanceMath.CalculateJumpDistanceFromReactionTime(baseRt * factor, njs);

                float strength = Clamp01(ctx.ApplyStrength);
                target = original + (modelTarget - original) * strength;
                source = "type model / " + model.Name + " [" +
                         (type.IsKnown ? MapArchetypes.DisplayName(type.PrimaryArchetype) : "global") + "]";
                if (strength < 0.999f)
                {
                    source += " (" + (strength * 100f).ToString("0") + "% map blend)";
                }
            }

            if (target < min)
            {
                target = min;
                source += " [min]";
            }

            if (target > max)
            {
                target = max;
                source += " [max]";
            }

            bool explicitTarget = manual || (ctx.Override != null && ctx.Override.HasTarget);
            float speedGain = ctx.NjsGainMultiplier > 0f ? ctx.NjsGainMultiplier : ctx.SongSpeedMultiplier;
            if (!explicitTarget && ctx.PreserveReactionTime && speedGain > 0f &&
                Math.Abs(speedGain - 1f) > 0.0001f)
            {
                float reactionTime = JumpDistanceMath.CalculateReactionTime(target, njs) * speedGain;
                target = JumpDistanceMath.CalculateJumpDistanceFromReactionTime(reactionTime, njs);
                source += " [RT@" + speedGain.ToString("0.##") + "x]";
                if (target < min) target = min;
                if (target > max) target = max;
            }

            float offset = JumpDistanceMath.SolveBeatOffsetForJumpDistance(bpm, njs, target);
            float finalRt = JumpDistanceMath.CalculateReactionTime(target, njs);

            return new ResolvedJd
            {
                Bpm = bpm,
                Njs = njs,
                MapOffset = ctx.MapOffset,
                OriginalJumpDistance = original,
                TargetJumpDistance = target,
                BeatOffset = offset,
                ReactionTime = finalRt,
                BaseReactionTime = baseRt > 0f ? baseRt : finalRt,
                TypeFactor = factor,
                MinimumJumpDistance = min,
                MaximumJumpDistance = max,
                ManualOverride = manual,
                Source = source,
                Type = type,
                Archetype = type.PrimaryArchetype,
            };
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
