using System;

namespace DynamicJD.Core
{
    public enum JdConfigMode
    {
        ReactionTime = 0,
        JumpDistance = 1,
    }

    /// <summary>
    /// The user-editable part of the model. The hardcoded type model is applied on top of this at
    /// evaluation time, so switching type models never mutates the user's base configuration.
    /// </summary>
    public sealed class BaseJdConfig
    {
        /// <summary>Reaction time at NJS 20, in JDFixer-compatible milliseconds.</summary>
        public float Rt20 { get; set; } = 220f;

        /// <summary>RT change per NJS step. Negative values make high-NJS maps use shorter RT.</summary>
        public float SlopeMsPerNjs { get; set; } = -1.8f;

        /// <summary>Optional RT curvature per squared NJS step. Zero keeps the classic linear profile.</summary>
        public float CurvatureMsPerNjs2 { get; set; }

        public float Jd20
        {
            get { return JumpDistanceMath.CalculateJumpDistanceFromReactionTime(Rt20, 20f); }
            set { Rt20 = JumpDistanceMath.CalculateReactionTime(Math.Max(0.1f, value), 20f); }
        }

        public float EvaluateReactionTime(float njs)
        {
            float offset = njs - 20f;
            float rt = Rt20 + SlopeMsPerNjs * offset + CurvatureMsPerNjs2 * offset * offset;
            return Math.Max(20f, rt);
        }

        public float EvaluateJumpDistance(float njs)
        {
            return JumpDistanceMath.CalculateJumpDistanceFromReactionTime(EvaluateReactionTime(njs), njs);
        }

        public void EnsureValid()
        {
            if (float.IsNaN(Rt20) || float.IsInfinity(Rt20) || Rt20 < 20f) Rt20 = 220f;
            if (float.IsNaN(SlopeMsPerNjs) || float.IsInfinity(SlopeMsPerNjs)) SlopeMsPerNjs = -1.8f;
            if (SlopeMsPerNjs < -35f) SlopeMsPerNjs = -35f;
            if (SlopeMsPerNjs > 5f) SlopeMsPerNjs = 5f;
            if (float.IsNaN(CurvatureMsPerNjs2) || float.IsInfinity(CurvatureMsPerNjs2)) CurvatureMsPerNjs2 = 0f;
            if (CurvatureMsPerNjs2 < -2f) CurvatureMsPerNjs2 = -2f;
            if (CurvatureMsPerNjs2 > 2f) CurvatureMsPerNjs2 = 2f;
        }

        public BaseJdConfig Clone()
        {
            return new BaseJdConfig
            {
                Rt20 = Rt20,
                SlopeMsPerNjs = SlopeMsPerNjs,
                CurvatureMsPerNjs2 = CurvatureMsPerNjs2,
            };
        }
    }
}
