namespace DynamicJD.Core
{
    public static class NjsCompensationMath
    {
        public static float CompensatedHalfJumpBeats(float initialBeats, float initialNjs, float currentNjs)
        {
            if (initialBeats <= 0f || initialNjs <= 0f || currentNjs <= 0f) return initialBeats;

            float scale = currentNjs / initialNjs;
            float target = scale < 1f ? initialBeats * scale : initialBeats;
            if (target < JumpDistanceMath.MinHalfJumpDurationInBeats)
            {
                target = JumpDistanceMath.MinHalfJumpDurationInBeats;
            }
            return target;
        }
    }
}
