using System;
using DynamicJD.Core;

namespace DynamicJD.Services
{
    /// <summary>Everything known about the map currently being previewed or played.</summary>
    public sealed class MapContext
    {
        public static MapContext Selected { get; set; }
        public static MapContext Current { get; set; }
        public static ResolvedJd CurrentResolved { get; set; }

        public string Hash { get; set; } = string.Empty;
        public string SongName { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public float Bpm { get; set; }
        public float Njs { get; set; }
        public float MapOffset { get; set; }
        public float PlayerOffset { get; set; }
        public float OriginalJumpDistance { get; set; }
        public float SongSpeedMultiplier { get; set; } = 1f;
        public float NjsGainMultiplier { get; set; } = 1f;
        public bool IsPractice { get; set; }
        public string CustomSpeedModifierId { get; set; } = string.Empty;
        public MapTypeInfo Type { get; set; } = MapTypeInfo.Unknown();

        public string Key
        {
            get { return Hash + "|" + Difficulty + "|" + Mode; }
        }

        public bool HasMap
        {
            get { return !string.IsNullOrEmpty(Hash); }
        }

        public string TitleLine
        {
            get
            {
                if (!HasMap) return "<#8c8c8c>No map selected";
                return SongName + "  <#8c8c8c>" + Difficulty + " · " + Mode;
            }
        }

        public static string HashFromLevelId(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                return string.Empty;
            }

            const string prefix = "custom_level_";
            string hash = levelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? levelId.Substring(prefix.Length)
                : levelId;

            return hash.Length > 40 ? hash.Substring(0, 40) : hash;
        }

        public static MapContext FromBeatmap(BeatmapKey key, BeatmapLevel level, float playerOffset,
            GameplayModifiers modifiers, PracticeSettings practiceSettings)
        {
            float speed = practiceSettings?.songSpeedMul ?? modifiers?.songSpeedMul ?? 1f;
            var ctx = new MapContext
            {
                PlayerOffset = playerOffset,
                SongSpeedMultiplier = speed,
                NjsGainMultiplier = speed,
                IsPractice = practiceSettings != null,
            };

            if (practiceSettings == null && ModifiersCoreBridge.TryGetActiveSpeedModifier(out CustomSpeedModifierSpec custom))
            {
                ctx.SongSpeedMultiplier = custom.SongSpeedMultiplier;
                ctx.NjsGainMultiplier = custom.NjsGainMultiplier;
                ctx.CustomSpeedModifierId = custom.Id;
            }

            if (level == null)
            {
                return ctx;
            }

            ctx.Hash = HashFromLevelId(level.levelID);
            ctx.SongName = level.songName;
            ctx.Difficulty = key.difficulty.ToString();
            ctx.Mode = key.beatmapCharacteristic != null
                ? key.beatmapCharacteristic.serializedName
                : "Standard";
            ctx.Bpm = level.beatsPerMinute;

            float njs = JumpDistanceMath.FallbackNoteJumpMovementSpeed;
            float offset = 0f;
            if (level.beatmapBasicData != null
                && key.beatmapCharacteristic != null
                && level.beatmapBasicData.TryGetValue((key.beatmapCharacteristic, key.difficulty), out BeatmapBasicData basic))
            {
                njs = basic.noteJumpMovementSpeed;
                offset = basic.noteJumpStartBeatOffset;
            }

            ctx.Njs = JumpDistanceMath.SanitizeNjs(njs);
            ctx.MapOffset = offset;
            ctx.OriginalJumpDistance = JumpDistanceMath.CalculateJumpDistance(ctx.Bpm, ctx.Njs, offset);
            ctx.Type = MapTypeInfo.Unknown(ctx.Njs);
            return ctx;
        }

        public static MapContext FromGameplay(float bpm, float njs, float beatOffset)
        {
            njs = JumpDistanceMath.SanitizeNjs(njs);
            var ctx = new MapContext
            {
                Bpm = bpm,
                Njs = njs,
                MapOffset = beatOffset,
                PlayerOffset = 0f,
                OriginalJumpDistance = JumpDistanceMath.CalculateJumpDistance(bpm, njs, beatOffset),
                Type = MapTypeInfo.Unknown(njs),
                SongSpeedMultiplier = 1f,
                NjsGainMultiplier = 1f,
            };

            if (ModifiersCoreBridge.TryGetActiveSpeedModifier(out CustomSpeedModifierSpec custom))
            {
                ctx.SongSpeedMultiplier = custom.SongSpeedMultiplier;
                ctx.NjsGainMultiplier = custom.NjsGainMultiplier;
                ctx.CustomSpeedModifierId = custom.Id;
            }

            MapContext existing = Current;
            if (existing != null && existing.HasMap && Math.Abs(existing.Njs - njs) < 0.01f)
            {
                existing.Bpm = bpm;
                return existing;
            }

            return ctx;
        }
    }
}
