using System;
using System.Reflection;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Services;
using HarmonyLib;

namespace DynamicJD.Patches
{
    internal static class VariableMovementDataProviderInitPatch
    {
        private static readonly string[] PatchOrder =
        {
            "BeatLeader",
            "BeatLeader.AutoPatchesRegistry",
            "ScoreSaber",
            "com.umbranox.BeatSaber.ScoreSaber",
            "Legato",
        };

        internal static void Patch(Harmony harmony)
        {
            MethodBase init = AccessTools.Method(typeof(VariableMovementDataProvider), "Init");
            MethodBase update = AccessTools.Method(typeof(VariableMovementDataProvider), "ManualUpdate");
            if (init == null || update == null)
            {
                Plugin.Log?.Warn("DynamicJD: VariableMovementDataProvider methods not found; JD override disabled.");
                return;
            }

            harmony.Patch(init,
                prefix: new HarmonyMethod(typeof(VariableMovementDataProviderInitPatch), nameof(Prefix))
                {
                    after = PatchOrder,
                });
            harmony.Patch(update,
                postfix: new HarmonyMethod(typeof(VariableMovementDataProviderNjsPatch), nameof(VariableMovementDataProviderNjsPatch.Postfix)));
        }

        private static void Prefix(ref float noteJumpMovementSpeed, float bpm,
            ref BeatmapObjectSpawnMovementData.NoteJumpValueType noteJumpValueType, ref float noteJumpValue)
        {
            PluginConfig config = PluginConfig.Instance;
            if (config == null || !config.enabled) return;
            if (!config.apply_in_replays && ReplayGuard.IsReplay) return;
            if (!JumpDistanceMath.IsUsableBpm(bpm))
            {
                Plugin.Log?.Warn("Skipping DynamicJD: unusable BPM " + bpm);
                return;
            }

            noteJumpValueType = BeatmapObjectSpawnMovementData.NoteJumpValueType.BeatOffset;

            float njs = JumpDistanceMath.SanitizeNjs(noteJumpMovementSpeed);
            MapContext ctx = MapContext.Current ?? MapContext.FromGameplay(bpm, njs, noteJumpValue);
            ctx.Bpm = bpm;
            ctx.Njs = njs;

            float effectiveOffset = config.respect_vanilla_offset ? noteJumpValue : ctx.MapOffset;
            float original = JumpDistanceMath.CalculateJumpDistance(bpm, njs, effectiveOffset);
            ctx.OriginalJumpDistance = original;
            if (config.respect_vanilla_offset) ctx.MapOffset = noteJumpValue;

            if (!ctx.Type.IsKnown)
            {
                MapTypeInfo cached = Plugin.MapTypes?.Cached(ctx);
                if (cached != null) ctx.Type = cached;
                else if (MapContext.Selected != null &&
                         string.Equals(MapContext.Selected.Hash, ctx.Hash, System.StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(MapContext.Selected.Difficulty, ctx.Difficulty, System.StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(MapContext.Selected.Mode, ctx.Mode, System.StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Type = MapContext.Selected.Type;
                }
            }

            MapOverride mapOverride = Plugin.Overrides?.Get(ctx.Key);
            ResolvedJd resolved = JdResolver.Resolve(new ResolveContext
            {
                Enabled = true,
                Bpm = bpm,
                Njs = njs,
                MapOffset = effectiveOffset,
                OriginalJumpDistance = original,
                Type = ctx.Type,
                Profile = Plugin.Profiles?.Active,
                TypeModel = TypeModels.Get(config.type_model),
                TypeModelStrength = config.type_model_strength,
                ApplyStrength = config.strength,
                Override = mapOverride,
                ManualOverride = config.manual_override,
                ManualJumpDistance = config.manual_jd,
                MinJumpDistance = config.min_jump_distance,
                MaxJumpDistance = config.max_jump_distance,
                PreserveReactionTime = config.song_speed_mode == 1,
                SongSpeedMultiplier = ctx.SongSpeedMultiplier,
                NjsGainMultiplier = ctx.NjsGainMultiplier,
            });

            noteJumpValue = resolved.BeatOffset;
            MapContext.Current = ctx;
            MapContext.CurrentResolved = resolved;
            NjsCompensation.Arm(bpm, njs, resolved.BeatOffset);

            Plugin.Log?.Info("DynamicJD " + ctx.SongName + " [" + ctx.Difficulty + "] type=" +
                             (ctx.Type.IsKnown ? ctx.Type.PrimaryArchetype.ToString() : "unknown") +
                             " NJS=" + JumpDistanceMath.FormatNjs(njs) +
                             " speed=" + ctx.SongSpeedMultiplier.ToString("0.##") +
                             " njsGain=" + ctx.NjsGainMultiplier.ToString("0.##") +
                             (string.IsNullOrEmpty(ctx.CustomSpeedModifierId) ? string.Empty : " " + ctx.CustomSpeedModifierId) +
                             " JD " + JumpDistanceMath.FormatJd(original) + " -> " +
                             JumpDistanceMath.FormatJd(resolved.TargetJumpDistance) +
                             " RT " + JumpDistanceMath.FormatRt(resolved.ReactionTime) +
                             " (" + resolved.Source + ")");
        }

    }

    internal static class VariableMovementDataProviderNjsPatch
    {
        internal static void Postfix(VariableMovementDataProvider __instance)
        {
            try
            {
                if (__instance == null || !ReplayGuard.ShouldApplyModBehaviors || !__instance.wasUpdatedThisFrame) return;
                float? adjusted = NjsCompensation.AdjustedBeats(__instance.noteJumpSpeed);
                if (!adjusted.HasValue) return;
                ProviderMutator.ApplyBeats(__instance, adjusted.Value, __instance.noteJumpSpeed);
                Plugin.Log?.Debug("DynamicJD NJS ramp: half jump scaled to " + adjusted.Value.ToString("0.###") + " beats");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("DynamicJD NJS ramp patch failed: " + ex);
            }
        }
    }
}
