using System;
using System.Collections.Generic;

namespace DynamicJD.Core
{
    /// <summary>
    /// Fixed base JD profiles. Built-ins and generated profiles are read-only; only <see cref="ProfileRole.Custom"/>
    /// is editable. Type shaping lives in <see cref="TypeModels"/>.
    /// </summary>
    public static class DefaultProfiles
    {
        public const string CustomName = "Custom";
        public const string SuggestedName = "Suggested";
        public const string DefaultName = "Default";
        public const string CompetitiveName = "Competitive";
        public const string RelaxedName = "Relaxed";
        public const string PerPlayerName = "Per-Player";

        // Legacy display names retained for config/profile migration.
        public const string LegacyResearchName = "Research (Mid-Rank)";
        public const string LegacyRankedName = "Ranked (Top 100)";
        public const string LegacyComfortName = "Comfort (400ms)";
        public const string LegacyClassicName = "Classic JDFixer (500ms)";

        public static IReadOnlyList<JdProfile> CreateAll()
        {
            return new List<JdProfile>
            {
                CreateCustom(),
                CreateSuggested(),
                CreateDefault(),
                CreateCompetitive(),
                CreateRelaxed(),
                CreatePerPlayer(),
            };
        }

        public static JdProfile CreateRole(ProfileRole role)
        {
            switch (role)
            {
                case ProfileRole.Custom: return CreateCustom();
                case ProfileRole.Suggested: return CreateSuggested();
                case ProfileRole.Default: return CreateDefault();
                case ProfileRole.Competitive: return CreateCompetitive();
                case ProfileRole.Relaxed: return CreateRelaxed();
                case ProfileRole.PerPlayer: return CreatePerPlayer();
                default: return CreateDefault();
            }
        }

        public static string DisplayName(ProfileRole role)
        {
            switch (role)
            {
                case ProfileRole.Custom: return CustomName;
                case ProfileRole.Suggested: return SuggestedName;
                case ProfileRole.Competitive: return CompetitiveName;
                case ProfileRole.Relaxed: return RelaxedName;
                case ProfileRole.PerPlayer: return PerPlayerName;
                default: return DefaultName;
            }
        }

        public static JdProfile CreateCustom()
        {
            return Build(CustomName, ProfileRole.Custom, 220f, -1.8f,
                "Your own editable base JD/RT setup. Map-type adaptation is applied on top.");
        }

        public static JdProfile CreateSuggested()
        {
            JdProfile profile = Build(SuggestedName, ProfileRole.Suggested, 220f, -1.8f,
                "Analyze your own plays to generate a suggested setup.");
            profile.SampleCount = 0;
            profile.Confidence = 0f;
            return profile;
        }

        public static JdProfile CreateDefault()
        {
            return Build(DefaultName, ProfileRole.Default, 220f, -1.8f,
                "Community median baseline from ranked-player replay data.");
        }

        public static JdProfile CreateCompetitive()
        {
            return Build(CompetitiveName, ProfileRole.Competitive, 203f, -1.4f,
                "Tighter distances used by top-ranked players.");
        }

        public static JdProfile CreateRelaxed()
        {
            return Build(RelaxedName, ProfileRole.Relaxed, 400f, 0f,
                "Longer reaction time for easier reading.");
        }

        public static JdProfile CreatePerPlayer()
        {
            JdProfile profile = Build(PerPlayerName, ProfileRole.PerPlayer, 220f, -1.8f,
                "Choose a player in Copy a player's setup to fill this profile.");
            profile.Source = "per-player";
            return profile;
        }

        public static string MigrateName(string name)
        {
            switch (name)
            {
                case LegacyResearchName:
                case "Recommended":
                    return DefaultName;
                case LegacyRankedName:
                    return CompetitiveName;
                case LegacyComfortName:
                case "Relaxed":
                    return RelaxedName;
                case LegacyClassicName:
                case "Classic":
                    return DefaultName;
                case "Starting point":
                case "JD config preset":
                    return CustomName;
                default:
                    return name;
            }
        }

        public static ProfileRole? RoleForName(string name)
        {
            switch (name)
            {
                case CustomName:
                case "Starting point":
                case "JD config preset":
                    return ProfileRole.Custom;
                case SuggestedName:
                    return ProfileRole.Suggested;
                case DefaultName:
                case LegacyResearchName:
                case "Recommended":
                    return ProfileRole.Default;
                case CompetitiveName:
                case LegacyRankedName:
                    return ProfileRole.Competitive;
                case RelaxedName:
                case LegacyComfortName:
                    return ProfileRole.Relaxed;
                case PerPlayerName:
                    return ProfileRole.PerPlayer;
                default:
                    return null;
            }
        }

        private static JdProfile Build(string name, ProfileRole role, float rt20, float slope, string notes)
        {
            return new JdProfile
            {
                Name = name,
                Role = role,
                Source = "built-in",
                CreatedAt = DateTime.UtcNow.ToString("o"),
                Notes = notes,
                Base = new BaseJdConfig { Rt20 = rt20, SlopeMsPerNjs = slope },
            };
        }
    }
}
