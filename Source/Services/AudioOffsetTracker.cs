using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicJD.Configuration;
using DynamicJD.Core;
using HarmonyLib;

namespace DynamicJD.Services
{
    /// <summary>
    /// Collects clean live cuts and turns one song's sample set into a pending latency suggestion.
    /// Suggestions are deliberately never applied here; the menu requires explicit confirmation.
    /// </summary>
    internal static class AudioOffsetTracker
    {
        private static readonly List<AudioOffsetSample> Samples = new List<AudioOffsetSample>(256);
        private static readonly FieldInfo AudioLatencyField =
            AccessTools.Field(typeof(AudioTimeSyncController), "_audioLatency");

        private static bool _collecting;
        private static float _latencyAtStartMs;
        private static string _lastRunNote = "No calibration run yet.";

        public static event Action Changed;

        public static AudioOffsetResult LastResult { get; private set; }
        public static AudioOffsetResult Pending { get; private set; }
        public static bool IsCollecting => _collecting;
        public static string LastRunNote => _lastRunNote;

        public static bool AnalysisEnabled
        {
            get
            {
                PluginConfig config = PluginConfig.Instance;
                return config != null && config.audio_offset_analysis;
            }
        }

        public static void BeginRun(AudioTimeSyncController controller)
        {
            Samples.Clear();
            Pending = null;
            _collecting = false;

            if (controller == null)
            {
                Skip("Could not find the song audio controller.");
                return;
            }

            if (!AnalysisEnabled)
            {
                Skip("Audio offset analysis is off.");
                return;
            }

            if (ReplayGuard.IsReplay)
            {
                Skip("Skipped replay playback.");
                return;
            }

            MapContext map = MapContext.Current;
            if (map != null && map.IsPractice)
            {
                Skip("Skipped practice mode.");
                return;
            }

            if (Math.Abs(controller.timeScale - 1f) > 0.01f)
            {
                Skip("Skipped non-1.0x song speed.");
                return;
            }

            if (controller.forcedNoAudioSync)
            {
                Skip("Skipped because audio sync is disabled.");
                return;
            }

            if (AudioLatencyField == null)
            {
                Skip("Could not read the game's audio latency.");
                return;
            }

            try
            {
                _latencyAtStartMs = (float)AudioLatencyField.GetValue(controller) * 1000f;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("DynamicJD audio offset: could not read latency: " + ex.Message);
                Skip("Could not read the game's audio latency.");
                return;
            }

            _collecting = true;
            _lastRunNote = "Collecting cuts at 1.0x...";
            Notify();
        }

        public static void OnScoringForNoteStarted(ScoringElement element)
        {
            if (!_collecting) return;

            GoodCutScoringElement goodCut = element as GoodCutScoringElement;
            if (goodCut == null) return;

            IReadonlyCutScoreBuffer buffer = goodCut.cutScoreBuffer;
            if (buffer == null) return;

            NoteCutInfo info = buffer.noteCutInfo;
            if (!info.allIsOK || info.noteData == null) return;
            if (info.noteData.time < 5f) return;

            NoteData.ScoringType scoringType = info.noteData.scoringType;
            if (scoringType == NoteData.ScoringType.Ignore ||
                scoringType == NoteData.ScoringType.NoScore ||
                scoringType == NoteData.ScoringType.ChainLink ||
                scoringType == NoteData.ScoringType.ChainLinkArcHead)
            {
                return;
            }

            float deviationMs = info.timeDeviation * 1000f;
            if (float.IsNaN(deviationMs) || float.IsInfinity(deviationMs) ||
                Math.Abs(deviationMs) > AudioOffsetEstimator.MaxSampleDeviationMs)
            {
                return;
            }

            Samples.Add(new AudioOffsetSample
            {
                NoteTime = info.noteData.time,
                DeviationMs = deviationMs,
            });
        }

        public static void EndRun()
        {
            if (!_collecting) return;
            _collecting = false;

            PluginConfig config = PluginConfig.Instance;
            int minSamples = config != null
                ? config.audio_offset_min_samples
                : AudioOffsetEstimator.DefaultMinSamples;
            float maxStepMs = config != null
                ? config.audio_offset_max_step_ms
                : AudioOffsetEstimator.DefaultMaxStepMs;

            AudioOffsetResult result = AudioOffsetEstimator.Estimate(Samples, _latencyAtStartMs,
                minSamples, maxStepMs);
            LastResult = result;
            Pending = result.HasSuggestion ? result : null;
            _lastRunNote = result.Status;
            Notify();
        }

        public static void MarkApplied(float appliedDeltaMs)
        {
            Pending = null;
            _lastRunNote = "Applied " + FormatDelta(appliedDeltaMs) + " · verify on another map.";
            Notify();
        }

        public static void MarkUndone()
        {
            _lastRunNote = "Restored previous latency.";
            Notify();
        }

        public static void Clear()
        {
            Samples.Clear();
            _collecting = false;
            LastResult = null;
            Pending = null;
            _lastRunNote = "No calibration run yet.";
            Notify();
        }

        private static void Skip(string reason)
        {
            _lastRunNote = reason;
            Notify();
        }

        private static string FormatDelta(float value)
        {
            return (value >= 0f ? "+" : string.Empty) + value.ToString("0") + " ms";
        }

        private static void Notify()
        {
            Changed?.Invoke();
        }
    }
}
