using DynamicJD.Configuration;
using DynamicJD.Core;

namespace DynamicJD.Services
{
    public static class PerPlayerProfileService
    {
        /// <summary>Projects the selected per-player entry into the fixed Per-Player profile role.</summary>
        public static void Sync(ProfileStore profiles, PerPlayerStore perPlayers)
        {
            if (profiles == null || perPlayers == null) return;
            JdProfile profile = profiles.GetByRole(ProfileRole.PerPlayer) ?? DefaultProfiles.CreatePerPlayer();
            PerPlayerEntry entry = perPlayers.SelectedEntry;

            if (entry == null)
            {
                profile.Base = new BaseJdConfig();
                profile.PlayerId = string.Empty;
                profile.PlayerName = string.Empty;
                profile.SampleCount = 0;
                profile.Confidence = 0f;
                profile.RecommendedTypeStrength = -1f;
                profile.Notes = "Choose a player in Copy a player's setup to fill this profile.";
            }
            else
            {
                profile.Base = entry.Base.Clone();
                profile.PlayerId = entry.PlayerId;
                profile.PlayerName = entry.PlayerName;
                profile.SampleCount = entry.SampleCount;
                profile.Confidence = entry.Confidence;
                profile.RecommendedTypeStrength = -1f;
                profile.LastAnalyzedAt = entry.UpdatedAt;
                profile.Notes = "Per-Player setup for " + entry.PlayerName + ".";
            }

            profiles.Upsert(profile, false);
        }
    }
}
