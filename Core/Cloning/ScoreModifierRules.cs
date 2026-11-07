using System;
using System.Collections.Generic;

namespace DynamicJD.Core.Cloning
{
    /// <summary>Classifies BeatLeader score modifiers that change the NJS/RT relationship.</summary>
    public static class ScoreModifierRules
    {
        private static readonly HashSet<string> SpeedModifiers = new HashSet<string>(
            new[] { "FS", "SF", "SS", "FN", "BFS", "BSF" }, StringComparer.OrdinalIgnoreCase);

        public static bool HasSpeedModifier(string modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifiers)) return false;
            foreach (string part in modifiers.Split(','))
            {
                if (SpeedModifiers.Contains(part.Trim())) return true;
            }
            return false;
        }
    }
}
