using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicJD.Core
{
    public sealed class CurveAnchor
    {
        public float Njs { get; set; }
        public float JumpDistance { get; set; }

        public CurveAnchor()
        {
        }

        public CurveAnchor(float njs, float jumpDistance)
        {
            Njs = njs;
            JumpDistance = jumpDistance;
        }
    }

    /// <summary>Piecewise-linear JD-vs-NJS curve. Kept deliberately simple and inspectable.</summary>
    public sealed class JdCurve
    {
        public List<CurveAnchor> Anchors { get; set; } = new List<CurveAnchor>();

        public JdCurve()
        {
        }

        public JdCurve(IEnumerable<CurveAnchor> anchors)
        {
            Anchors = anchors
                .Where(a => a != null && !float.IsNaN(a.Njs) && !float.IsNaN(a.JumpDistance))
                .OrderBy(a => a.Njs)
                .GroupBy(a => a.Njs)
                .Select(g => g.Last())
                .ToList();
        }

        public static JdCurve FromReactionTimes(params (float njs, float rt)[] points)
        {
            return new JdCurve(points.Select(p => new CurveAnchor(
                p.njs,
                JumpDistanceMath.CalculateJumpDistanceFromReactionTime(p.rt, p.njs))));
        }

        public JdCurve Normalized(IEnumerable<float> njsGrid)
        {
            return new JdCurve(njsGrid.Select(n => new CurveAnchor(n, Evaluate(n))));
        }

        public float Evaluate(float njs)
        {
            if (Anchors == null || Anchors.Count == 0)
            {
                return 0f;
            }

            var sorted = Anchors.OrderBy(a => a.Njs).ToList();
            if (sorted.Count == 1 || njs <= sorted[0].Njs)
            {
                return Math.Max(0f, sorted[0].JumpDistance);
            }

            for (int i = 1; i < sorted.Count; i++)
            {
                if (njs <= sorted[i].Njs)
                {
                    CurveAnchor a = sorted[i - 1];
                    CurveAnchor b = sorted[i];
                    float span = b.Njs - a.Njs;
                    if (span <= 0.0001f)
                    {
                        return Math.Max(0f, b.JumpDistance);
                    }

                    float t = (njs - a.Njs) / span;
                    return Math.Max(0f, a.JumpDistance + (b.JumpDistance - a.JumpDistance) * t);
                }
            }

            return Math.Max(0f, sorted[sorted.Count - 1].JumpDistance);
        }

        public JdCurve Clone()
        {
            return new JdCurve(Anchors.Select(a => new CurveAnchor(a.Njs, a.JumpDistance)));
        }

        public void SetAnchor(int index, float njs, float jumpDistance)
        {
            Anchors = Anchors.OrderBy(a => a.Njs).ToList();
            while (Anchors.Count <= index)
            {
                Anchors.Add(new CurveAnchor());
            }

            Anchors[index] = new CurveAnchor(njs, Math.Max(0f, jumpDistance));
            Anchors = Anchors.OrderBy(a => a.Njs).ToList();
        }
    }
}
