using System;
using System.Reflection;
using DynamicJD.Services;
using HarmonyLib;

namespace DynamicJD.Patches
{
    /// <summary>
    /// Starts a live timing sample session and feeds it only clean local note cuts.
    /// </summary>
    internal static class AudioOffsetPatches
    {
        internal static void Patch(Harmony harmony)
        {
            MethodBase audioStart = AccessTools.Method(typeof(AudioTimeSyncController), "Start");
            if (audioStart == null)
            {
                Plugin.Log?.Warn("DynamicJD audio offset: AudioTimeSyncController.Start not found.");
            }
            else
            {
                harmony.Patch(audioStart,
                    postfix: new HarmonyMethod(typeof(AudioOffsetPatches), nameof(AudioStartPostfix)));
            }

            MethodBase scoreStart = AccessTools.Method(typeof(ScoreController), "Start");
            if (scoreStart == null)
            {
                Plugin.Log?.Warn("DynamicJD audio offset: ScoreController.Start not found.");
            }
            else
            {
                harmony.Patch(scoreStart,
                    postfix: new HarmonyMethod(typeof(AudioOffsetPatches), nameof(ScoreStartPostfix)));
            }
        }

        private static void AudioStartPostfix(AudioTimeSyncController __instance)
        {
            try
            {
                AudioOffsetTracker.BeginRun(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("DynamicJD audio offset: could not begin run: " + ex);
            }
        }

        private static void ScoreStartPostfix(ScoreController __instance)
        {
            try
            {
                if (!AudioOffsetTracker.AnalysisEnabled) return;
                __instance.scoringForNoteStartedEvent += AudioOffsetTracker.OnScoringForNoteStarted;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("DynamicJD audio offset: could not attach cut listener: " + ex);
            }
        }
    }
}
