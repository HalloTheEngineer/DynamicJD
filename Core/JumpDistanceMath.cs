using System;
using System.Globalization;

namespace DynamicJD.Core
{
    /// <summary>
    /// Jump-distance math transcribed from the game's own calculation. This is deliberately free of
    /// Unity and IPA dependencies so it can be unit tested.
    /// </summary>
    public static class JumpDistanceMath
    {
        public const float StartHalfJumpDurationInBeats = 4f;
        public const float MaxHalfJumpDistance = 18f;
        public const float MaxHalfJumpDistanceEpsilon = 0.001f;
        public const float MinHalfJumpDurationInBeats = 0.25f;
        public const float MinNoteJumpMovementSpeed = 0.01f;
        public const float FallbackNoteJumpMovementSpeed = 10f;
        public const float ReactionTimeDivisor = 500f;

        public static float SanitizeNjs(float njs)
        {
            if (float.IsNaN(njs) || float.IsInfinity(njs) || njs <= MinNoteJumpMovementSpeed)
            {
                return FallbackNoteJumpMovementSpeed;
            }

            return njs;
        }

        public static bool IsUsableBpm(float bpm)
        {
            return bpm > 0f && !float.IsNaN(bpm) && !float.IsInfinity(bpm);
        }

        /// <summary>
        /// Mirrors CoreMathUtils.CalculateHalfJumpDurationInBeats. The game subtracts 0.001 from the
        /// cap before the halving loop, so the effective cutoff is 17.999.
        /// </summary>
        public static float CalculateHalfJumpDurationInBeats(float bpm, float njs, float beatOffset)
        {
            if (!IsUsableBpm(bpm))
            {
                return 0f;
            }

            njs = SanitizeNjs(njs);
            float oneBeatDuration = 60f / bpm;
            float beats = StartHalfJumpDurationInBeats;
            float distance = njs * oneBeatDuration * beats;
            float cap = MaxHalfJumpDistance - MaxHalfJumpDistanceEpsilon;

            while (distance > cap)
            {
                beats /= 2f;
                distance = njs * oneBeatDuration * beats;
            }

            beats += beatOffset;
            if (beats < MinHalfJumpDurationInBeats)
            {
                beats = MinHalfJumpDurationInBeats;
            }

            return beats;
        }

        public static float CalculateJumpDistance(float bpm, float njs, float beatOffset)
        {
            if (!IsUsableBpm(bpm))
            {
                return 0f;
            }

            njs = SanitizeNjs(njs);
            return njs * (60f / bpm) * CalculateHalfJumpDurationInBeats(bpm, njs, beatOffset) * 2f;
        }

        /// <summary>
        /// Inverse of <see cref="CalculateJumpDistance"/>: the beat offset the game needs in order to
        /// produce the requested distance.
        /// </summary>
        public static float SolveBeatOffsetForJumpDistance(float bpm, float njs, float jumpDistance)
        {
            if (!IsUsableBpm(bpm) || jumpDistance <= 0f || float.IsNaN(jumpDistance) || float.IsInfinity(jumpDistance))
            {
                return 0f;
            }

            njs = SanitizeNjs(njs);
            float oneBeatDuration = 60f / bpm;
            float beats = StartHalfJumpDurationInBeats;
            float cap = MaxHalfJumpDistance - MaxHalfJumpDistanceEpsilon;

            while (njs * oneBeatDuration * beats > cap)
            {
                beats /= 2f;
            }

            return jumpDistance / (njs * oneBeatDuration * 2f) - beats;
        }

        public static float CalculateReactionTime(float jumpDistance, float njs)
        {
            njs = SanitizeNjs(njs);
            return jumpDistance / (2f * njs) * ReactionTimeDivisor;
        }

        public static float CalculateJumpDistanceFromReactionTime(float reactionTime, float njs)
        {
            njs = SanitizeNjs(njs);
            return reactionTime * (2f * njs) / ReactionTimeDivisor;
        }

        /// <summary>The lowest JD the map can actually reach through the 0.25-beat floor.</summary>
        public static float MinimumJumpDistance(float bpm, float njs)
        {
            return CalculateJumpDistance(bpm, njs, -1000f);
        }

        public static string FormatJd(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string FormatRt(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        public static string FormatNjs(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
