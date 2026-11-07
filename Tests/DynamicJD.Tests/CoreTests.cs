using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DynamicJD.Api;
using DynamicJD.Core;
using DynamicJD.Core.Cloning;
using Xunit;

namespace DynamicJD.Tests
{
    public class JumpDistanceMathTests
    {
        [Theory]
        [InlineData(120f, 16f, 0f, 32f)]
        [InlineData(120f, 10f, 0f, 20f)]
        [InlineData(120f, 20f, 0f, 20f)]
        [InlineData(100f, 10f, 0f, 24f)]
        [InlineData(120f, 16f, 1f, 48f)]
        [InlineData(120f, 16f, -3f, 4f)]
        public void JumpDistance_MatchesGameFormula(float bpm, float njs, float offset, float expected)
        {
            Assert.Equal(expected, JumpDistanceMath.CalculateJumpDistance(bpm, njs, offset), 3);
        }

        [Theory]
        [InlineData(120f, 16f, 24f)]
        [InlineData(120f, 16f, 32f)]
        [InlineData(180f, 20f, 12f)]
        [InlineData(90f, 12f, 40f)]
        [InlineData(140f, 17.3f, 22.5f)]
        public void Solver_RoundTrips(float bpm, float njs, float jd)
        {
            float offset = JumpDistanceMath.SolveBeatOffsetForJumpDistance(bpm, njs, jd);
            Assert.Equal(jd, JumpDistanceMath.CalculateJumpDistance(bpm, njs, offset), 2);
        }

        [Fact]
        public void ReactionTime_IsJdFixerConvention()
        {
            Assert.Equal(500f, JumpDistanceMath.CalculateReactionTime(32f, 16f), 3);
            Assert.Equal(32f, JumpDistanceMath.CalculateJumpDistanceFromReactionTime(500f, 16f), 3);
        }

        [Fact]
        public void InvalidBpm_DoesNotHang()
        {
            Assert.Equal(0f, JumpDistanceMath.CalculateJumpDistance(0f, 16f, 0f));
            Assert.Equal(0f, JumpDistanceMath.CalculateJumpDistance(float.NaN, 16f, 0f));
        }
    }

    public class CurveTests
    {
        [Fact]
        public void PiecewiseInterpolation_ClampsAtEnds()
        {
            var curve = new JdCurve(new[]
            {
                new CurveAnchor(10f, 10f),
                new CurveAnchor(20f, 20f),
            });
            Assert.Equal(10f, curve.Evaluate(5f), 3);
            Assert.Equal(15f, curve.Evaluate(15f), 3);
            Assert.Equal(20f, curve.Evaluate(30f), 3);
        }

        [Fact]
        public void ResearchModel_OrdersTypesAndEvaluatesFloatNjs()
        {
            JdProfile profile = DefaultProfiles.CreateDefault();
            MapTypeInfo speedType = TypeInfo(MapTypeBits.Speed);
            MapTypeInfo techType = TypeInfo(MapTypeBits.Tech);
            MapTypeInfo accType = TypeInfo(MapTypeBits.Accuracy);

            float speed = TypeModels.Research.TargetJumpDistance(profile.Base, speedType, 20.5f, 1f);
            float tech = TypeModels.Research.TargetJumpDistance(profile.Base, techType, 20.5f, 1f);
            float acc = TypeModels.Research.TargetJumpDistance(profile.Base, accType, 20.5f, 1f);
            Assert.True(speed < tech);
            Assert.True(tech < acc);
        }

        private static MapTypeInfo TypeInfo(MapTypeBits bits)
        {
            return new MapTypeInfo
            {
                Bitmask = (int)bits,
                Archetypes = MapArchetypes.FromMask((int)bits).ToList(),
                Source = "BeatLeader",
            };
        }


        [Fact]
        public void BaseConfig_EvaluatesFloatNjsSmoothly()
        {
            var config = new BaseJdConfig { Rt20 = 220f, SlopeMsPerNjs = -1.8f };
            Assert.Equal(219.1f, config.EvaluateReactionTime(20.5f), 2);
            Assert.Equal(1f, config.EvaluateReactionTime(20.5f) / 219.1f, 3);
        }

        [Fact]
        public void NeutralModel_DoesNotChangeBaseConfig()
        {
            var baseConfig = new BaseJdConfig { Rt20 = 250f, SlopeMsPerNjs = -2f };
            var type = TypeInfo(MapTypeBits.Tech);
            float expected = baseConfig.EvaluateJumpDistance(19.5f);
            float actual = TypeModels.Neutral.TargetJumpDistance(baseConfig, type, 19.5f, 1f);
            Assert.Equal(expected, actual, 3);
        }

        [Fact]
        public void LegacyProfile_MigratesGlobalCurveToBaseConfig()
        {
            var legacy = new JdProfile
            {
                Name = "legacy",
                Global = JdCurve.FromReactionTimes((12f, 240f), (20f, 220f), (28f, 200f)),
            };
            legacy.EnsureValid();
            Assert.NotNull(legacy.Base);
            Assert.Equal(220f, legacy.Base.Rt20, 1);
            Assert.Equal(-2.5f, legacy.Base.SlopeMsPerNjs, 1);
        }

        [Fact]
        public void CurveFitter_RecoversSyntheticLine()
        {
            var samples = new List<CloneSample>();
            for (int i = 0; i < 12; i++)
            {
                float njs = 12f + i;
                samples.Add(new CloneSample { Njs = njs, ReactionTime = 200f + 2f * (njs - 20f) });
            }

            FitResult fit = CurveFitter.Fit(samples);
            Assert.Equal(2f, fit.Slope, 1);
            Assert.Equal(200f, fit.InterceptAtNjs20, 1);
        }
    }


    public class TypeModelVerificationTests
    {
        [Fact]
        public void ResearchModel_HasEveryArchetype()
        {
            foreach (MapArchetype archetype in MapArchetypes.All)
            {
                Assert.True(TypeModels.Research.Coefficients.ContainsKey(archetype));
                Assert.True(TypeModels.Neutral.Coefficients.ContainsKey(archetype));
            }
        }

        [Fact]
        public void NeutralModel_IsNeutralEverywhere()
        {
            foreach (MapArchetype archetype in MapArchetypes.All)
            {
                Assert.Equal(1f, TypeModels.Neutral.Factor(archetype, 8f), 4);
                Assert.Equal(1f, TypeModels.Neutral.Factor(archetype, 20f), 4);
                Assert.Equal(1f, TypeModels.Neutral.Factor(archetype, 32f), 4);
            }
        }

        [Theory]
        [InlineData(12f, 0.952f)]
        [InlineData(16f, 0.948f)]
        [InlineData(20f, 0.944f)]
        [InlineData(24f, 0.940f)]
        public void ResearchSpeedFactor_MatchesFittedCurve(float njs, float expected)
        {
            Assert.Equal(expected, TypeModels.Research.Factor(MapArchetype.Speed, njs), 2);
            Assert.Equal(expected, TypeModels.Research.Factor(MapArchetype.Linear, njs), 2);
        }

        [Theory]
        [InlineData(12f, 1.058f)]
        [InlineData(16f, 1.054f)]
        [InlineData(20f, 1.050f)]
        [InlineData(24f, 1.046f)]
        public void ResearchTechFactor_MatchesFittedCurve(float njs, float expected)
        {
            Assert.Equal(expected, TypeModels.Research.Factor(MapArchetype.Tech, njs), 2);
        }

        [Theory]
        [InlineData(12f, 1.5248f)]
        [InlineData(16f, 1.403f)]
        [InlineData(20f, 1.320f)]
        [InlineData(24f, 1.275f)]
        public void ResearchAccuracyFactor_MatchesFittedCurve(float njs, float expected)
        {
            Assert.Equal(expected, TypeModels.Research.Factor(MapArchetype.Accuracy, njs), 3);
        }

        [Fact]
        public void ResearchBasePlusModel_ReproducesResearchRt()
        {
            var baseConfig = new BaseJdConfig { Rt20 = 220f, SlopeMsPerNjs = -1.8f };
            var speed = new MapTypeInfo
            {
                Bitmask = (int)MapTypeBits.Speed,
                Archetypes = new List<MapArchetype> { MapArchetype.Speed },
                Source = "BeatLeader",
            };
            var accuracy = new MapTypeInfo
            {
                Bitmask = (int)MapTypeBits.Accuracy,
                Archetypes = new List<MapArchetype> { MapArchetype.Accuracy },
                Source = "BeatLeader",
            };

            Assert.Equal(215.4f, TypeModels.Research.TargetReactionTime(baseConfig, speed, 16f, 1f), 1);
            Assert.Equal(207.7f, TypeModels.Research.TargetReactionTime(baseConfig, speed, 20f, 1f), 1);
            Assert.Equal(318.8f, TypeModels.Research.TargetReactionTime(baseConfig, accuracy, 16f, 1f), 1);
            Assert.Equal(290.4f, TypeModels.Research.TargetReactionTime(baseConfig, accuracy, 20f, 1f), 1);
        }
    }

    public class ResolverTests
    {
        private static ResolveContext Context(JdProfile profile)
        {
            return new ResolveContext
            {
                Enabled = true,
                Bpm = 120f,
                Njs = 16f,
                MapOffset = 0f,
                OriginalJumpDistance = 32f,
                Type = new MapTypeInfo
                {
                    Bitmask = (int)MapTypeBits.Midspeed,
                    Archetypes = new List<MapArchetype> { MapArchetype.Balanced },
                    Source = "BeatLeader",
                },
                Profile = profile,
                TypeModel = TypeModels.Research,
                TypeModelStrength = 1f,
                ApplyStrength = 1f,
                MinJumpDistance = 2f,
                MaxJumpDistance = 60f,
                SongSpeedMultiplier = 1f,
            };
        }

        [Fact]
        public void Strength_BlendsWithMapOriginal()
        {
            JdProfile comfort = DefaultProfiles.CreateRelaxed();
            ResolveContext ctx = Context(comfort);
            ctx.ApplyStrength = 0.5f;
            ResolvedJd result = JdResolver.Resolve(ctx);
            float full = JumpDistanceMath.CalculateJumpDistanceFromReactionTime(400f, 16f);
            Assert.Equal(32f + (full - 32f) * 0.5f, result.TargetJumpDistance, 2);
        }

        [Fact]
        public void Override_WinsOverProfile()
        {
            ResolveContext ctx = Context(DefaultProfiles.CreateRelaxed());
            ctx.Override = new MapOverride { TargetJumpDistance = 21f };
            Assert.Equal(21f, JdResolver.Resolve(ctx).TargetJumpDistance, 3);
        }

        [Fact]
        public void ManualOverride_IgnoresTypeModel()
        {
            ResolveContext ctx = Context(DefaultProfiles.CreateDefault());
            ctx.ManualOverride = true;
            ctx.ManualJumpDistance = 18f;
            ResolvedJd result = JdResolver.Resolve(ctx);
            Assert.Equal(18f, result.TargetJumpDistance, 3);
            Assert.True(result.ManualOverride);
        }

        [Fact]
        public void PerMapOverride_WinsOverManualOverride()
        {
            ResolveContext ctx = Context(DefaultProfiles.CreateDefault());
            ctx.ManualOverride = true;
            ctx.ManualJumpDistance = 18f;
            ctx.Override = new MapOverride { TargetJumpDistance = 22f };
            Assert.Equal(22f, JdResolver.Resolve(ctx).TargetJumpDistance, 3);
        }

        [Fact]
        public void ReactionTimePreservation_ScalesJdWithSongSpeed()
        {
            ResolveContext ctx = Context(DefaultProfiles.CreateRelaxed());
            ctx.PreserveReactionTime = true;
            ctx.SongSpeedMultiplier = 1.2f;
            ResolvedJd result = JdResolver.Resolve(ctx);
            float normal = JumpDistanceMath.CalculateJumpDistanceFromReactionTime(400f, 16f);
            Assert.Equal(normal * 1.2f, result.TargetJumpDistance, 1);
        }

        [Fact]
        public void ReactionTimePreservation_UsesNjsGainForCustomSpeed()
        {
            ResolveContext ctx = Context(DefaultProfiles.CreateRelaxed());
            ctx.PreserveReactionTime = true;
            ctx.SongSpeedMultiplier = 1.2f;
            ctx.NjsGainMultiplier = 1.1f;
            ResolvedJd result = JdResolver.Resolve(ctx);
            float normal = JumpDistanceMath.CalculateJumpDistanceFromReactionTime(400f, 16f);
            Assert.Equal(normal * 1.1f, result.TargetJumpDistance, 1);
            Assert.Contains("RT@1.1x", result.Source);
        }
    }


    public class ProfileRoleTests
    {
        [Fact]
        public void CreateAll_ReturnsFixedRoleOrder()
        {
            var profiles = DefaultProfiles.CreateAll();
            Assert.Equal(new[]
            {
                ProfileRole.Custom,
                ProfileRole.Suggested,
                ProfileRole.Default,
                ProfileRole.Competitive,
                ProfileRole.Relaxed,
                ProfileRole.PerPlayer,
            }, profiles.Select(p => p.Role.Value).ToArray());
            Assert.DoesNotContain(profiles, p => p.Name == "Classic");
        }
    }

    public class MapTypeTests
    {
        [Fact]
        public void BeatLeaderBits_MapToArchetypes()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                (int)MapTypeBits.Tech, 20f, 0f, 8f, 6f, "hash", "ExpertPlus", "Standard");
            Assert.Contains(MapArchetype.Tech, info.Archetypes);
            Assert.Equal(MapArchetype.Tech, info.PrimaryArchetype);
        }

        [Fact]
        public void NoBitmask_UsesBeatLeaderMetricsFallback()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 16f, 0f, 5.6896f, 2.619f, "hash", "Expert", "Standard",
                0, 0, 0, techRating: 5.29f, accRating: 7.35f, peakEbpm: 145f,
                linearPercentage: 0.07f, bombs: 0, notes: 628);
            Assert.True(info.IsKnown);
            Assert.Contains(MapArchetype.Tech, info.Archetypes);
            Assert.Equal("BeatLeader metrics", info.Source);
        }

        [Fact]
        public void MissingLinearity_AddsBalancedUncertaintyToAccuracyMap()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 17.5f, 0f, 7.2981906f, 5.0049677f, "HeavyWeight", "ExpertPlus", "Standard",
                techRating: 4.422043f, accRating: 9.383945f, linearPercentage: null);

            Assert.Equal((int)(MapTypeBits.Accuracy | MapTypeBits.Midspeed), info.Bitmask);
            Assert.Equal(new[] { MapArchetype.Accuracy, MapArchetype.Balanced }, info.Archetypes);
            Assert.Equal("BeatLeader metrics", info.Source);
            Assert.Contains("ACCURACY", info.ChipText);
            Assert.Contains("BALANCED", info.ChipText);
            Assert.DoesNotContain("LINEAR", info.ChipText);
        }

        [Fact]
        public void MissingLinearity_DoesNotTriggerZeroLinearityTechRule()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 20f, 0f, 12.6047316f, 10.6566277f, "hash", "ExpertPlus", "Standard",
                techRating: 7.3f, accRating: 12.5f, linearPercentage: null);

            Assert.Contains(MapArchetype.Speed, info.Archetypes);
            Assert.DoesNotContain(MapArchetype.Tech, info.Archetypes);
            Assert.DoesNotContain(MapArchetype.Balanced, info.Archetypes);
        }

        [Fact]
        public void MissingLinearity_WithNoMetrics_StillFallsBackToBalanced()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 16f, 0f, 0f, 0f, "hash", "Expert", "Standard");

            Assert.Equal("BeatLeader fallback", info.Source);
            Assert.Contains(MapArchetype.Balanced, info.Archetypes);
        }

        [Fact]
        public void AccuracyBalancedInference_DilutesAccuracyFactor()
        {
            MapTypeInfo inferred = MapTypeInfo.FromBeatLeader(
                0, 17.5f, 0f, 7.2981906f, 5.0049677f, "HeavyWeight", "ExpertPlus", "Standard",
                techRating: 4.422043f, accRating: 9.383945f, linearPercentage: null);
            var accuracyOnly = new MapTypeInfo
            {
                Bitmask = (int)MapTypeBits.Accuracy,
                Archetypes = new List<MapArchetype> { MapArchetype.Accuracy },
                Source = "BeatLeader metrics",
            };

            float combined = TypeModels.Research.BlendedFactor(inferred, 17.5f, 1f);
            float accuracy = TypeModels.Research.BlendedFactor(accuracyOnly, 17.5f, 1f);
            Assert.True(combined < accuracy);
            Assert.Equal(1.18375f, combined, 3);
        }

        [Fact]
        public void NoMapData_FallsBackToBalancedInsteadOfUnknown()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 16f, 0f, 0f, 0f, "hash", "Expert", "Standard");
            Assert.True(info.IsKnown);
            Assert.Contains(MapArchetype.Balanced, info.Archetypes);
            Assert.Equal("BeatLeader fallback", info.Source);
        }

        [Fact]
        public void CombinedBits_AreAllRetained()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                (int)MapTypeBits.Speed | (int)MapTypeBits.Linear, 22f, 0f, 9f, 8f, "hash", "ExpertPlus", "Standard");
            Assert.Contains(MapArchetype.Speed, info.Archetypes);
            Assert.Contains(MapArchetype.Linear, info.Archetypes);
            Assert.Equal(MapArchetype.Speed, info.PrimaryArchetype);
        }
    }


    public class ProfileFitterTests
    {
        private static CloneSample Sample(float njs, float rt, MapTypeBits bits = MapTypeBits.Midspeed)
        {
            return new CloneSample
            {
                Njs = njs,
                ReactionTime = rt,
                TypeMask = (int)bits,
                Archetypes = MapArchetypes.FromMask((int)bits).ToList(),
            };
        }

        [Fact]
        public void FlatData_SelectsFlatCurve()
        {
            var samples = Enumerable.Range(0, 50)
                .Select(i => Sample(10f + i * 16f / 49f, 200f))
                .ToList();

            ProfileFitResult fit = ProfileFitter.Fit(samples);

            Assert.True(fit.Success);
            Assert.Equal(ProfileCurveKind.Flat, fit.Curve);
            Assert.Equal(200f, fit.Base.Rt20, 1);
            Assert.Equal(0f, fit.Base.SlopeMsPerNjs, 2);
            Assert.Equal(0f, fit.Base.CurvatureMsPerNjs2, 2);
        }

        [Fact]
        public void LinearData_RecoversSlope()
        {
            var samples = Enumerable.Range(0, 70)
                .Select(i =>
                {
                    float njs = 10f + i * 16f / 69f;
                    return Sample(njs, 220f - 2f * (njs - 20f));
                })
                .ToList();

            ProfileFitResult fit = ProfileFitter.Fit(samples);

            Assert.True(fit.Success);
            Assert.Equal(ProfileCurveKind.Linear, fit.Curve);
            Assert.Equal(220f, fit.Base.Rt20, 2);
            Assert.Equal(-2f, fit.Base.SlopeMsPerNjs, 0.3f);
        }

        [Fact]
        public void CurvedData_SelectsQuadraticCurve()
        {
            var samples = Enumerable.Range(0, 90)
                .Select(i =>
                {
                    float njs = 10f + i * 18f / 89f;
                    float offset = njs - 20f;
                    return Sample(njs, 220f - 1.2f * offset + 0.8f * offset * offset);
                })
                .ToList();

            ProfileFitResult fit = ProfileFitter.Fit(samples);

            Assert.True(fit.Success);
            Assert.Equal(ProfileCurveKind.Quadratic, fit.Curve);
            Assert.Equal(0.8f, fit.Base.CurvatureMsPerNjs2, 0.2f);
        }

        [Fact]
        public void TypeShapedData_EstimatesTypeStrength()
        {
            var masks = new[] { MapTypeBits.Speed, MapTypeBits.Accuracy, MapTypeBits.Midspeed };
            var samples = Enumerable.Range(0, 90)
                .Select(i =>
                {
                    float njs = 12f + i * 14f / 89f;
                    MapTypeBits bits = masks[i % masks.Length];
                    float baseRt = 220f - 1.5f * (njs - 20f);
                    float factor = TypeModels.Research.RawFactor(
                        MapArchetypes.FromMask((int)bits).ToList(), njs);
                    return Sample(njs, baseRt * factor, bits);
                })
                .ToList();

            ProfileFitResult fit = ProfileFitter.Fit(samples);

            Assert.True(fit.Success);
            Assert.True(fit.RecommendedTypeStrength > 0.5f);
            Assert.Equal(220f, fit.Base.Rt20, 10f);
            Assert.Equal(-1.5f, fit.Base.SlopeMsPerNjs, 0.7f);
        }

        [Fact]
        public void SpeedModifierRules_DetectSpeedChanges()
        {
            Assert.True(ScoreModifierRules.HasSpeedModifier("FS,BE"));
            Assert.True(ScoreModifierRules.HasSpeedModifier("NA,SF"));
            Assert.True(ScoreModifierRules.HasSpeedModifier("SS"));
            Assert.True(ScoreModifierRules.HasSpeedModifier("FN"));
            Assert.True(ScoreModifierRules.HasSpeedModifier("BFS,PM"));
            Assert.True(ScoreModifierRules.HasSpeedModifier("BSF"));
            Assert.False(ScoreModifierRules.HasSpeedModifier("BE,NF,DA"));
            Assert.False(ScoreModifierRules.HasSpeedModifier(""));
        }

        [Fact]
        public void CustomSpeedModifiers_MapBeatLeaderSpeedsAndNjsGain()
        {
            Assert.True(CustomSpeedModifiers.TryGet("BFS", out CustomSpeedModifierSpec bfs));
            Assert.Equal(1.2f, bfs.SongSpeedMultiplier, 3);
            Assert.Equal(1.1f, bfs.NjsGainMultiplier, 3);

            Assert.True(CustomSpeedModifiers.TryGet("bsf", out CustomSpeedModifierSpec bsf));
            Assert.Equal(1.5f, bsf.SongSpeedMultiplier, 3);
            Assert.Equal(1.25f, bsf.NjsGainMultiplier, 3);

            Assert.False(CustomSpeedModifiers.TryGet("unknown", out _));
            Assert.Equal(2, CustomSpeedModifiers.All.Count);
        }

        [Fact]
        public void BaseConfig_EvaluatesCurvature()
        {
            var config = new BaseJdConfig
            {
                Rt20 = 220f,
                SlopeMsPerNjs = -1.8f,
                CurvatureMsPerNjs2 = 0.5f,
            };

            Assert.Equal(220f, config.EvaluateReactionTime(20f), 2);
            Assert.Equal(219.225f, config.EvaluateReactionTime(20.5f), 2);
            Assert.Equal(0.5f, config.Clone().CurvatureMsPerNjs2, 2);
        }
    }


    public class MapTypeAuthorityTests
    {
        [Fact]
        public void AuthoritativeBeatLeaderType_UsesShaping()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                (int)MapTypeBits.Speed, 20f, 0f, 8f, 5f, "hash", "ExpertPlus", "Standard");

            Assert.True(info.IsAuthoritative);
            Assert.True(TypeModels.Research.BlendedFactor(info, 20f, 1f) < 1f);
        }

        [Fact]
        public void InferredMapType_UsesFallbackShaping()
        {
            MapTypeInfo info = MapTypeInfo.FromBeatLeader(
                0, 20f, 0f, 8f, 5f, "hash", "ExpertPlus", "Standard",
                techRating: 8f, accRating: 8f);
            info.Source = "BeatLeader metrics";

            Assert.False(info.IsAuthoritative);
            Assert.True(TypeModels.Research.BlendedFactor(info, 20f, 1f) > 1f);
            Assert.Contains("INFERRED", info.ChipText);
        }
    }


    public class NjsCompensationTests
    {
        [Fact]
        public void DownwardRamp_ScalesBeats()
        {
            float beats = NjsCompensationMath.CompensatedHalfJumpBeats(4f, 20f, 10f);
            Assert.Equal(2f, beats, 3);
        }

        [Fact]
        public void ReturnToInitial_RestoresInitialBeats()
        {
            float beats = NjsCompensationMath.CompensatedHalfJumpBeats(4f, 20f, 20f);
            Assert.Equal(4f, beats, 3);
        }

        [Fact]
        public void AboveInitial_RestoresInitialBeats()
        {
            float beats = NjsCompensationMath.CompensatedHalfJumpBeats(4f, 20f, 25f);
            Assert.Equal(4f, beats, 3);
        }

        [Fact]
        public void VeryLowNjs_ClampsToMinimum()
        {
            float beats = NjsCompensationMath.CompensatedHalfJumpBeats(4f, 20f, 0.5f);
            Assert.Equal(JumpDistanceMath.MinHalfJumpDurationInBeats, beats, 3);
        }

        [Fact]
        public void InvalidInput_ReturnsInitialBeats()
        {
            Assert.Equal(4f, NjsCompensationMath.CompensatedHalfJumpBeats(4f, 0f, 10f), 3);
        }
    }

    public class BsorTests
    {
        [Fact]
        public void HeaderReader_ExtractsJumpDistance()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(0x442d3d69);
                w.Write((byte)1);
                w.Write((byte)0);
                string[] strings =
                {
                    "0.9.33", "1.39.1", "1793674551", "player", "name", "pc", "OpenXR", "hmd", "controller",
                    "HASH", "song", "mapper", "ExpertPlus",
                };
                foreach (string s in strings)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(s);
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }
                w.Write(12345);
                foreach (string s in new[] { "Standard", "Env", "BE" })
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(s);
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }
                w.Write(15.5f);
                w.Flush();

                BsorHeader header = BsorHeaderReader.Read(ms.ToArray());
                Assert.NotNull(header);
                Assert.Equal(15.5f, header.JumpDistance, 3);
                Assert.Equal("HASH", header.Hash);
                Assert.Equal("player", header.PlayerId);
                Assert.Equal("name", header.PlayerName);
            }
        }
    }
}
