using System;
using DynamicJD.Configuration;
using DynamicJD.Core;

namespace DynamicJD.Services
{
    /// <summary>
    /// When reaction time is authoritative, NJS downward ramps stretch the jump duration in the base
    /// game. This scales the stored half-jump beats by the same factor the provider applies, keeping
    /// the configured RT instead of letting it drift.
    /// </summary>
    internal static class NjsCompensation
    {
        private static bool _armed;
        private static float _initialNjs;
        private static float _initialBeats;

        public static void Arm(float bpm, float njs, float beatOffset)
        {
            PluginConfig config = PluginConfig.Instance;
            _armed = config != null && config.enabled && config.njs_event_compensation &&
                     config.song_speed_mode == 1 && bpm > 0f && njs > 0f;
            if (!_armed) return;

            _initialNjs = njs;
            _initialBeats = JumpDistanceMath.CalculateHalfJumpDurationInBeats(bpm, njs, beatOffset);
            Plugin.Log?.Info("NJS compensation armed: initial NJS " + njs.ToString("0.###") +
                             ", beats " + _initialBeats.ToString("0.###"));
        }

        public static void Disarm()
        {
            _armed = false;
        }

        public static float? AdjustedBeats(float currentNjs)
        {
            if (!_armed || _initialNjs <= 0f || currentNjs <= 0f) return null;
            float target = NjsCompensationMath.CompensatedHalfJumpBeats(
                _initialBeats, _initialNjs, currentNjs);
            Plugin.Log?.Debug("NJS compensation: initial NJS " + _initialNjs.ToString("0.###") +
                              ", current NJS " + currentNjs.ToString("0.###") +
                              ", initial beats " + _initialBeats.ToString("0.###") +
                              ", target beats " + target.ToString("0.###"));
            return target;
        }
    }
}
