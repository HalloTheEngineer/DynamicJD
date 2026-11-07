using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DynamicJD.Patches
{
    internal static class CompatibilityPatches
    {
        private static FieldInfo _recorderInstance;
        private static MethodInfo _recorderOnBefore;
        private static bool _recorderFailureLogged;

        internal static void Patch(Harmony harmony)
        {
#if BS_MODERN
            if (Needs144Compatibility()) PatchBeatLeader(harmony);
            if (NeedsSongCoreCompatibility()) PatchSongCore(harmony);
#endif
        }

        private static bool Needs144Compatibility()
        {
            string version = Application.version ?? string.Empty;
            return version.StartsWith("1.44", StringComparison.Ordinal);
        }

        private static bool NeedsSongCoreCompatibility()
        {
            string version = Application.version ?? string.Empty;
            return version.StartsWith("1.42", StringComparison.Ordinal) ||
                   version.StartsWith("1.44", StringComparison.Ordinal);
        }

        private static void PatchBeatLeader(Harmony harmony)
        {
            Type recorder = AccessTools.TypeByName("BeatLeader.ReplayRecorder");
            if (recorder == null) return;

            _recorderInstance = AccessTools.Field(recorder, "_instance");
            _recorderOnBefore = AccessTools.Method(recorder, "OnBeforeScoreControllerLateUpdate");

            MethodInfo initialize = AccessTools.Method(recorder, "InitializePatches");
            if (initialize != null)
            {
                harmony.Patch(initialize,
                    prefix: new HarmonyMethod(typeof(CompatibilityPatches), nameof(Skip)));
            }

            MethodInfo dispose = AccessTools.Method(recorder, "DisposePatches");
            if (dispose != null)
            {
                harmony.Patch(dispose,
                    prefix: new HarmonyMethod(typeof(CompatibilityPatches), nameof(Skip)));
            }

            if (_recorderInstance == null || _recorderOnBefore == null)
            {
                Plugin.Log?.Warn("DynamicJD compatibility: BeatLeader ReplayRecorder members not found; score recording left disabled.");
                return;
            }

            MethodInfo scoreUpdate = AccessTools.Method(typeof(ScoreController), "LateUpdate");
            if (scoreUpdate != null)
            {
                harmony.Patch(scoreUpdate,
                    prefix: new HarmonyMethod(typeof(CompatibilityPatches), nameof(ScoreControllerSafePrefix)));
            }

            Plugin.Log?.Warn("DynamicJD compatibility: BeatLeader replay recorder bridge enabled.");
        }

        private static void PatchSongCore(Harmony harmony)
        {
            Type hook = AccessTools.TypeByName("SongCore.Hooks.AllowNegativeVariableNoteJumpSpeedHook");
            if (hook == null) return;

            MethodInfo initialize = AccessTools.Method(hook, "Initialize");
            if (initialize != null)
            {
                harmony.Patch(initialize,
                    prefix: new HarmonyMethod(typeof(CompatibilityPatches), nameof(Skip)));
            }

            MethodInfo dispose = AccessTools.Method(hook, "Dispose");
            if (dispose != null)
            {
                harmony.Patch(dispose,
                    prefix: new HarmonyMethod(typeof(CompatibilityPatches), nameof(Skip)));
            }

            Plugin.Log?.Warn("DynamicJD compatibility: SongCore negative-NJS hook disabled to protect VariableMovementDataProvider.ManualUpdate.");
        }

        private static bool Skip()
        {
            return false;
        }

        private static void ScoreControllerSafePrefix(
            AudioTimeSyncController ____audioTimeSyncController,
            List<float> ____sortedNoteTimesWithoutScoringElements,
            List<ScoringElement> ____sortedScoringElementsWithoutMultiplier)
        {
            object recorder = _recorderInstance.GetValue(null);
            if (recorder == null ||
                ____audioTimeSyncController == null ||
                ____sortedNoteTimesWithoutScoringElements == null ||
                ____sortedScoringElementsWithoutMultiplier == null)
            {
                return;
            }

            try
            {
                _recorderOnBefore.Invoke(recorder, new object[]
                {
                    ____audioTimeSyncController,
                    ____sortedNoteTimesWithoutScoringElements,
                    ____sortedScoringElementsWithoutMultiplier,
                });
            }
            catch (Exception ex)
            {
                if (_recorderFailureLogged) return;
                _recorderFailureLogged = true;
                Plugin.Log?.Error("DynamicJD compatibility: BeatLeader replay bridge failed: " + ex);
            }
        }
    }
}
