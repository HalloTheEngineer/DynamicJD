using System;
using System.Collections.Generic;

namespace DynamicJD.Core
{
    public sealed class CustomSpeedModifierSpec
    {
        public string Id { get; }
        public float SongSpeedMultiplier { get; }
        public float NjsGainMultiplier { get; }

        public CustomSpeedModifierSpec(string id, float songSpeedMultiplier)
        {
            Id = id;
            SongSpeedMultiplier = songSpeedMultiplier;
            NjsGainMultiplier = 1f + (songSpeedMultiplier - 1f) * 0.5f;
        }
    }

    public static class CustomSpeedModifiers
    {
        private static readonly Dictionary<string, CustomSpeedModifierSpec> Specs =
            new Dictionary<string, CustomSpeedModifierSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["BFS"] = new CustomSpeedModifierSpec("BFS", 1.2f),
                ["BSF"] = new CustomSpeedModifierSpec("BSF", 1.5f),
            };

        public static IReadOnlyList<CustomSpeedModifierSpec> All { get; } =
            new List<CustomSpeedModifierSpec>
            {
                Specs["BFS"],
                Specs["BSF"],
            };

        public static bool TryGet(string id, out CustomSpeedModifierSpec spec)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                spec = null;
                return false;
            }

            return Specs.TryGetValue(id.Trim(), out spec);
        }
    }
}
