using System;
using System.Reflection;
using DynamicJD.Core;
using HarmonyLib;
using UnityEngine;

namespace DynamicJD.Services
{
    /// <summary>
    /// Writes resolved jump-distance values into the game's movement provider. Fields are resolved
    /// lazily by name so a 1.40-1.44 version difference degrades to a log line instead of a patch
    /// crash.
    /// </summary>
    internal static class ProviderMutator
    {
        private static bool _initialized;
        private static bool _available;

        private static FieldInfo _beats;
        private static FieldInfo _halfDuration;
        private static FieldInfo _jumpDuration;
        private static FieldInfo _jumpDistance;
        private static FieldInfo _halfDistance;
        private static FieldInfo _moveStart;
        private static FieldInfo _moveEnd;
        private static FieldInfo _jumpEnd;
        private static FieldInfo _center;
        private static FieldInfo _forward;
        private static FieldInfo _waiting;
        private static FieldInfo _spawnAhead;
        private static FieldInfo _oneBeat;
        private static FieldInfo _initNjs;
        private static FieldInfo _currentNjs;

        public static void ApplyJumpDistance(VariableMovementDataProvider provider, float bpm, float njs,
            float jumpDistance)
        {
            if (!Ensure() || provider == null || !JumpDistanceMath.IsUsableBpm(bpm)) return;
            float offset = JumpDistanceMath.SolveBeatOffsetForJumpDistance(bpm, JumpDistanceMath.SanitizeNjs(njs), jumpDistance);
            float beats = JumpDistanceMath.CalculateHalfJumpDurationInBeats(bpm, njs, offset);
            ApplyBeats(provider, beats, JumpDistanceMath.SanitizeNjs(njs));
        }

        public static void ApplyBeats(VariableMovementDataProvider provider, float beats, float currentNjs)
        {
            if (!Ensure() || provider == null) return;

            float initNjs = GetFloat(provider, _initNjs);
            float num = initNjs > 0f ? Mathf.Min(currentNjs / initNjs, 1f) : 1f;
            if (num <= 0f) num = 1f;

            float half = GetFloat(provider, _oneBeat) * beats / num;
            float jumpDuration = half * 2f;
            float jumpDistance = currentNjs * jumpDuration;
            float halfDistance = jumpDistance * 0.5f;

            SetFloat(provider, _beats, beats);
            SetFloat(provider, _halfDuration, half);
            SetFloat(provider, _jumpDuration, jumpDuration);
            SetFloat(provider, _jumpDistance, jumpDistance);
            SetFloat(provider, _halfDistance, halfDistance);

            Vector3 center = GetVector(provider, _center);
            Vector3 forward = GetVector(provider, _forward);
            SetVector(provider, _moveStart, center + forward * (100f + halfDistance));
            SetVector(provider, _moveEnd, center + forward * halfDistance);
            SetVector(provider, _jumpEnd, center - forward * halfDistance);
            SetFloat(provider, _waiting, GetFloat(provider, _spawnAhead) - 0.5f - half);
        }

        public static float GetBaseNjs(VariableMovementDataProvider provider)
        {
            return !Ensure() || provider == null ? 0f : GetFloat(provider, _currentNjs);
        }

        private static bool Ensure()
        {
            if (_initialized) return _available;
            _initialized = true;
            _beats = Field("_halfJumpDurationInBeats");
            _halfDuration = Field("_halfJumpDuration");
            _jumpDuration = Field("_jumpDuration");
            _jumpDistance = Field("_jumpDistance");
            _halfDistance = Field("_halfJumpDistance");
            _moveStart = Field("_moveStartPosition");
            _moveEnd = Field("_moveEndPosition");
            _jumpEnd = Field("_jumpEndPosition");
            _center = Field("_centerPosition");
            _forward = Field("_forwardVector");
            _waiting = Field("_waitingDuration");
            _spawnAhead = Field("_spawnAheadTime");
            _oneBeat = Field("_initOneBeatDuration");
            _initNjs = Field("_initNoteJumpMovementSpeed");
            _currentNjs = Field("_noteJumpMovementSpeed");
            _available = _beats != null && _halfDuration != null && _jumpDuration != null &&
                         _jumpDistance != null && _halfDistance != null && _moveStart != null &&
                         _moveEnd != null && _jumpEnd != null && _center != null && _forward != null &&
                         _waiting != null && _spawnAhead != null && _oneBeat != null &&
                         _initNjs != null && _currentNjs != null;
            if (!_available)
            {
                Plugin.Log?.Warn("DynamicJD: VariableMovementDataProvider internals changed; live JD edits disabled.");
            }

            return _available;
        }

        private static FieldInfo Field(string name)
        {
            return AccessTools.Field(typeof(VariableMovementDataProvider), name);
        }

        private static float GetFloat(object target, FieldInfo field) => (float)field.GetValue(target);
        private static void SetFloat(object target, FieldInfo field, float value) => field.SetValue(target, value);
        private static Vector3 GetVector(object target, FieldInfo field) => (Vector3)field.GetValue(target);
        private static void SetVector(object target, FieldInfo field, Vector3 value) => field.SetValue(target, value);
    }
}
