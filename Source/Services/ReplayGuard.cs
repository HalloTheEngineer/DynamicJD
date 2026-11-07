using System;
using System.Reflection;
using DynamicJD.Configuration;
using HarmonyLib;
using IPA.Loader;

namespace DynamicJD.Services
{
    /// <summary>Detects ScoreSaber/BeatLeader replay sessions.</summary>
    internal static class ReplayGuard
    {
        public static bool IsReplay { get; private set; }

        public static bool ShouldApplyModBehaviors
        {
            get
            {
                PluginConfig config = PluginConfig.Instance;
                return !IsReplay || config == null || config.apply_in_replays;
            }
        }

        public static void Refresh(string sceneName)
        {
            IsReplay = false;
            if (!string.Equals(sceneName, "GameCore", StringComparison.Ordinal)) return;

            try
            {
                if (IsBeatLeaderReplay())
                {
                    IsReplay = true;
                    Plugin.Log?.Info("DynamicJD replay guard: BeatLeader replay detected; " +
                                     (ShouldApplyModBehaviors ? "applying mod behaviors." : "JD changes disabled."));
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("DynamicJD replay guard: BeatLeader check failed: " + ex.Message);
            }

            try
            {
                if (IsScoreSaberReplay())
                {
                    IsReplay = true;
                    Plugin.Log?.Info("DynamicJD replay guard: ScoreSaber replay detected; " +
                                     (ShouldApplyModBehaviors ? "applying mod behaviors." : "JD changes disabled."));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("DynamicJD replay guard: ScoreSaber check failed: " + ex.Message);
            }
        }

        private static bool IsBeatLeaderReplay()
        {
            PluginMetadata plugin = PluginManager.GetPluginFromId("BeatLeader");
            Type launcher = plugin?.Assembly?.GetType("BeatLeader.Replayer.ReplayerLauncher");
            PropertyInfo started = launcher?.GetProperty("IsStartedAsReplay",
                BindingFlags.Static | BindingFlags.Public);
            return started != null && started.GetValue(null, null) is bool value && value;
        }

        private static bool IsScoreSaberReplay()
        {
            Type registry = AccessTools.TypeByName("ScoreSaber.Features.Replays.ReplayStateRegistry");
            PropertyInfo playback = registry?.GetProperty("IsPlaybackEnabled",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (playback != null && playback.GetValue(null, null) is bool current && current)
            {
                return true;
            }

            Type legacyPatch = AccessTools.TypeByName(
                "ScoreSaber.Core.ReplaySystem.HarmonyPatches.PatchHandleHMDUnmounted");
            if (legacyPatch == null) return false;

            MethodInfo prefix = AccessTools.Method(legacyPatch, "Prefix");
            return prefix != null && prefix.Invoke(null, null) is bool value && !value;
        }
    }
}
