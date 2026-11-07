using System;
using System.Linq;
using System.Reflection;
using DynamicJD.Services;
using HarmonyLib;

namespace DynamicJD.Patches
{
    /// <summary>
    /// Captures the real map identity and song-speed multiplier before the gameplay scene loads.
    /// Registered manually from <see cref="Plugin.OnApplicationStart"/> so a future game version with
    /// a different transition signature produces a log line instead of aborting PatchAll.
    /// </summary>
    internal static class LevelScenesTransitionPatches
    {
        internal static void Patch(Harmony harmony)
        {
            PatchType(harmony, typeof(StandardLevelScenesTransitionSetupDataSO));
            PatchType(harmony, typeof(MultiplayerLevelScenesTransitionSetupDataSO));
            PatchType(harmony, typeof(MissionLevelScenesTransitionSetupDataSO));
        }

        private static void PatchType(Harmony harmony, Type type)
        {
            MethodBase method = AccessTools.FirstMethod(type,
                m => m.Name == "Init" && m.GetParameters().Any(p => p.ParameterType == typeof(BeatmapLevel)));
            if (method == null)
            {
                Plugin.Log?.Warn("DynamicJD: no map transition Init found on " + type.Name);
                return;
            }

            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(LevelScenesTransitionPatches), nameof(Capture))));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("DynamicJD: could not patch " + type.Name + ": " + ex.Message);
            }
        }

        internal static void Capture(object[] __args)
        {
            BeatmapKey key = default;
            BeatmapLevel level = null;
            GameplayModifiers modifiers = null;
            PracticeSettings practice = null;
            PlayerSpecificSettings playerSettings = null;

            foreach (object arg in __args)
            {
                if (arg is BeatmapKey k) key = k;
                else if (arg is BeatmapLevel l) level = l;
                else if (arg is GameplayModifiers gm) modifiers = gm;
                else if (arg is PracticeSettings ps) practice = ps;
                else if (arg is PlayerSpecificSettings pss) playerSettings = pss;
            }

            if (level == null) return;

            MapContext ctx = MapContext.FromBeatmap(key, level,
                playerSettings?.noteJumpStartBeatOffset ?? 0f, modifiers, practice);
            MapContext selected = MapContext.Selected;
            if (selected != null &&
                string.Equals(selected.Hash, ctx.Hash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Difficulty, ctx.Difficulty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Mode, ctx.Mode, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Type = selected.Type;
            }

            MapContext.Current = ctx;
            Plugin.MapTypes?.EnsureFetch(ctx);
            Plugin.Log?.Debug("Captured map " + ctx.Hash + " " + ctx.Difficulty + " " + ctx.Mode +
                              " NJS " + ctx.Njs + " speed " + ctx.SongSpeedMultiplier +
                              " njsGain " + ctx.NjsGainMultiplier +
                              (string.IsNullOrEmpty(ctx.CustomSpeedModifierId) ? string.Empty : " " + ctx.CustomSpeedModifierId));
        }
    }
}
