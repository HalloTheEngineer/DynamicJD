using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicJD.Core
{
    public enum MapArchetype
    {
        Balanced = 0,
        Accuracy = 1,
        Tech = 2,
        Speed = 3,
        Fitbeat = 4,
        Linear = 5,
        BombReset = 6,
    }

    /// <summary>BeatLeader map-type bits. Extreme is intentionally not an archetype.</summary>
    [Flags]
    public enum MapTypeBits
    {
        None = 0,
        Accuracy = 1 << 0,
        Tech = 1 << 1,
        Midspeed = 1 << 2,
        Speed = 1 << 3,
        Fitbeat = 1 << 4,
        Linear = 1 << 5,
        BombReset = 1 << 6,
    }

    public static class MapArchetypes
    {
        public static readonly MapArchetype[] All =
        {
            MapArchetype.Balanced,
            MapArchetype.Accuracy,
            MapArchetype.Tech,
            MapArchetype.Speed,
            MapArchetype.Fitbeat,
            MapArchetype.Linear,
            MapArchetype.BombReset,
        };

        public static string DisplayName(MapArchetype archetype)
        {
            switch (archetype)
            {
                case MapArchetype.Accuracy: return "Accuracy";
                case MapArchetype.Tech: return "Tech";
                case MapArchetype.Speed: return "Speed";
                case MapArchetype.Fitbeat: return "Fitbeat";
                case MapArchetype.Linear: return "Linear";
                case MapArchetype.BombReset: return "Bomb Reset";
                default: return "Balanced";
            }
        }

        public static string Color(MapArchetype archetype)
        {
            switch (archetype)
            {
                case MapArchetype.Accuracy: return "#c58bff";
                case MapArchetype.Tech: return "#ff6b6b";
                case MapArchetype.Speed: return "#ffab40";
                case MapArchetype.Fitbeat: return "#4dd0e1";
                case MapArchetype.Linear: return "#64b5f6";
                case MapArchetype.BombReset: return "#ffd166";
                default: return "#81c784";
            }
        }

        public static IEnumerable<MapArchetype> FromMask(int mask)
        {
            if ((mask & (int)MapTypeBits.Accuracy) != 0) yield return MapArchetype.Accuracy;
            if ((mask & (int)MapTypeBits.Tech) != 0) yield return MapArchetype.Tech;
            if ((mask & (int)MapTypeBits.Midspeed) != 0) yield return MapArchetype.Balanced;
            if ((mask & (int)MapTypeBits.Speed) != 0) yield return MapArchetype.Speed;
            if ((mask & (int)MapTypeBits.Fitbeat) != 0) yield return MapArchetype.Fitbeat;
            if ((mask & (int)MapTypeBits.Linear) != 0) yield return MapArchetype.Linear;
            if ((mask & (int)MapTypeBits.BombReset) != 0) yield return MapArchetype.BombReset;
        }
    }

    public sealed class MapTypeInfo
    {
        public const int CurrentClassifierVersion = 2;

        public int Bitmask { get; set; }
        public int ClassifierVersion { get; set; }
        public List<MapArchetype> Archetypes { get; set; } = new List<MapArchetype>();
        public float Njs { get; set; }
        public float BeatOffset { get; set; }
        public float Stars { get; set; }
        public float Nps { get; set; }
        public string Source { get; set; } = "none";
        public float Confidence { get; set; }
        public string SongHash { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;

        public static MapTypeInfo Unknown(float njs = 0f)
        {
            return new MapTypeInfo
            {
                Bitmask = 0,
                Archetypes = new List<MapArchetype> { MapArchetype.Balanced },
                Njs = njs,
                Source = "fallback",
                Confidence = 0f,
            };
        }

        public bool IsKnown => Bitmask != 0 && Archetypes != null && Archetypes.Count > 0;

        public bool IsAuthoritative => string.Equals(Source, "BeatLeader", StringComparison.OrdinalIgnoreCase);

        public void EnsureValid()
        {
            if (Archetypes == null || Archetypes.Count == 0)
            {
                Archetypes = MapArchetypes.FromMask(Bitmask).Distinct().ToList();
            }
            if (Archetypes.Count == 0)
            {
                Archetypes.Add(MapArchetype.Balanced);
            }
            if (Njs <= 0f)
            {
                Njs = 20f;
            }
        }

        public MapArchetype PrimaryArchetype
        {
            get
            {
                if (!IsKnown || Archetypes == null)
                {
                    return MapArchetype.Balanced;
                }

                // Prefer the most specialised bit for the headline label.
                if (Archetypes.Contains(MapArchetype.Speed)) return MapArchetype.Speed;
                if (Archetypes.Contains(MapArchetype.Tech)) return MapArchetype.Tech;
                if (Archetypes.Contains(MapArchetype.Accuracy)) return MapArchetype.Accuracy;
                if (Archetypes.Contains(MapArchetype.BombReset)) return MapArchetype.BombReset;
                if (Archetypes.Contains(MapArchetype.Fitbeat)) return MapArchetype.Fitbeat;
                if (Archetypes.Contains(MapArchetype.Linear)) return MapArchetype.Linear;
                return MapArchetype.Balanced;
            }
        }

        public string ChipText
        {
            get
            {
                if (!IsKnown || Archetypes == null)
                {
                    return "<#8c8c8c>TYPE UNKNOWN";
                }

                string chip = string.Join(" ", Archetypes.Select(a =>
                    "<color=" + MapArchetypes.Color(a) + ">" + MapArchetypes.DisplayName(a).ToUpperInvariant() + "</color>"));
                if (!IsAuthoritative)
                {
                    chip += " <color=#8c8c8c>INFERRED</color>";
                }
                return chip;
            }
        }

        public static MapTypeInfo FromBeatLeader(int bitmask, float njs, float offset, float stars, float nps,
            string hash, string difficulty, string mode, int speedTags = 0, int styleTags = 0,
            int featureTags = 0, float techRating = 0f, float accRating = 0f, float peakEbpm = 0f,
            float? linearPercentage = null, int bombs = 0, int notes = 0)
        {
            int computedMask = bitmask;
            string source = "BeatLeader";
            float confidence = 1f;

            if (computedMask == 0)
            {
                int tagMask = MaskFromTags(speedTags, styleTags, featureTags);
                if (tagMask != 0)
                {
                    computedMask = tagMask;
                    source = "BeatLeader tags";
                    confidence = 0.8f;
                }
                else
                {
                    int metricMask = MaskFromMetrics(njs, stars, nps, techRating, accRating, peakEbpm,
                        linearPercentage, bombs, notes);
                    if (metricMask != 0)
                    {
                        computedMask = metricMask;
                        source = "BeatLeader metrics";
                        confidence = 0.55f;
                    }
                    else
                    {
                        computedMask = (int)MapTypeBits.Midspeed;
                        source = "BeatLeader fallback";
                        confidence = 0.35f;
                    }
                }
            }

            var archetypes = MapArchetypes.FromMask(computedMask).Distinct().ToList();
            var info = new MapTypeInfo
            {
                Bitmask = computedMask,
                Archetypes = archetypes.Count > 0 ? archetypes : new List<MapArchetype> { MapArchetype.Balanced },
                Njs = njs,
                BeatOffset = offset,
                Stars = stars,
                Nps = nps,
                Source = source,
                Confidence = confidence,
                ClassifierVersion = CurrentClassifierVersion,
                SongHash = hash ?? string.Empty,
                Difficulty = difficulty ?? string.Empty,
                Mode = mode ?? string.Empty,
            };
            info.EnsureValid();
            return info;
        }

        private static int MaskFromTags(int speedTags, int styleTags, int featureTags)
        {
            int mask = 0;

            // speedTags: slow=1, medium=2, fast=4, extreme=8, insane=16
            if ((speedTags & 0x1C) != 0) mask |= (int)MapTypeBits.Speed;

            // styleTags: linear=1, true/standard acc=2/4, tech/tech acc=8/16, reset=32/64,
            // balanced=128, fitbeat=512, dance=1024, challenge=2048, jump/stream/stamina=4096/8192/16384
            if ((styleTags & 0x01) != 0) mask |= (int)MapTypeBits.Linear;
            if ((styleTags & 0x06) != 0) mask |= (int)MapTypeBits.Accuracy;
            if ((styleTags & 0x18) != 0) mask |= (int)MapTypeBits.Tech;
            if ((styleTags & 0x60) != 0) mask |= (int)MapTypeBits.BombReset | (int)MapTypeBits.Tech;
            if ((styleTags & 0x480) != 0) mask |= (int)MapTypeBits.Midspeed;
            if ((styleTags & 0x200) != 0) mask |= (int)MapTypeBits.Fitbeat;
            if ((styleTags & 0x800) != 0) mask |= (int)MapTypeBits.Tech;
            if ((styleTags & 0x7000) != 0) mask |= (int)MapTypeBits.Speed;

            // featureTags: reset=512, angular/circling/momentum tech=2048/4096/8192,
            // complex stream=16384.
            if ((featureTags & 0x200) != 0) mask |= (int)MapTypeBits.BombReset;
            if ((featureTags & 0x3800) != 0) mask |= (int)MapTypeBits.Tech;
            if ((featureTags & 0x4000) != 0) mask |= (int)MapTypeBits.Speed;

            return mask;
        }

        private static int MaskFromMetrics(float njs, float stars, float nps, float techRating,
            float accRating, float peakEbpm, float? linearPercentage, int bombs, int notes)
        {
            int mask = 0;
            float safeStars = Math.Max(stars, 0.1f);
            float techRatio = techRating / safeStars;
            float accRatio = accRating / safeStars;
            bool linearKnown = linearPercentage.HasValue;
            float linear = linearPercentage ?? 0f;

            if (peakEbpm >= 240f || (nps >= 5.5f && njs >= 18f) || nps >= 6.5f)
            {
                mask |= (int)MapTypeBits.Speed;
            }

            if (techRatio >= 0.80f || (linearKnown && linear < 0.10f && techRating >= 5f))
            {
                mask |= (int)MapTypeBits.Tech;
            }

            if (accRatio >= 1.15f && techRatio < 0.95f)
            {
                mask |= (int)MapTypeBits.Accuracy;
            }

            if (linearKnown && linear >= 0.12f)
            {
                mask |= (int)MapTypeBits.Linear;
            }

            if (notes > 0 && bombs > notes * 0.10f)
            {
                mask |= (int)MapTypeBits.BombReset;
            }

            int specialized = (int)(MapTypeBits.Tech | MapTypeBits.Speed | MapTypeBits.Fitbeat |
                                   MapTypeBits.BombReset);
            bool hasSignal = stars > 0f && (techRating > 0f || accRating > 0f || nps > 0f);
            if (!linearKnown && hasSignal && (mask & specialized) == 0)
            {
                mask |= (int)MapTypeBits.Midspeed;
            }

            return mask;
        }
    }
}
