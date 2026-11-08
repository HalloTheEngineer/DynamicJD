using System;
using System.IO;
using System.Net;
using DynamicJD.Api;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Installers;
using DynamicJD.Patches;
using DynamicJD.Services;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Loader;
using SiraUtil.Zenject;
using UnityEngine;
using UnityEngine.SceneManagement;
using IPALogger = IPA.Logging.Logger;

namespace DynamicJD
{
    [Plugin(RuntimeOptions.DynamicInit)]
    public sealed class Plugin
    {
        public static event Action ManualOverrideChanged;

        internal static void NotifyManualOverrideChanged()
        {
            ManualOverrideChanged?.Invoke();
        }

        internal static Harmony Harmony { get; private set; }
        internal static IPALogger Log { get; private set; }

        internal static ProfileStore Profiles { get; private set; }
        internal static PerPlayerStore PerPlayer { get; private set; }
        internal static OverrideStore Overrides { get; private set; }
        internal static BeatLeaderClient BeatLeader { get; private set; }
        internal static MapTypeService MapTypes { get; private set; }
        internal static CloneService Clone { get; private set; }
        internal static SuggestedProfileService Suggested { get; private set; }

        [Init]
        public Plugin(IPALogger logger, Config conf, Zenjector zenjector)
        {
            Log = logger;
            PluginConfig.Instance = conf.Generated<PluginConfig>();
            PluginConfig.Instance.OnLoad();

            string dataPath = string.IsNullOrEmpty(Application.dataPath)
                ? Environment.CurrentDirectory
                : Application.dataPath;
            string directory = Path.Combine(dataPath, "..", "UserData", "DynamicJD");
            BeatLeader = new BeatLeaderClient();
            Profiles = new ProfileStore(directory);
            PerPlayer = new PerPlayerStore(directory);
            PerPlayer.SeedFromProfile(Profiles.GetByRole(ProfileRole.PerPlayer));
            Overrides = new OverrideStore(directory);
            MapTypes = new MapTypeService(directory, BeatLeader);
            Clone = new CloneService(BeatLeader);
            Suggested = new SuggestedProfileService(BeatLeader);

            zenjector.Install<MenuInstaller>(Location.Menu);
        }

        [OnEnable]
        public void OnApplicationStart()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // Older Mono builds may not expose Tls12; HttpClient falls back to its defaults.
            }

            MainThreadDispatcher.Ensure();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            foreach (string conflicting in new[] { "JDFixer", "NjsFixer", "LevelTweaks" })
            {
                if (PluginManager.GetPluginFromId(conflicting) != null)
                {
                    Log.Warn("Detected " + conflicting + ". DynamicJD and " + conflicting +
                             " both control jump distance; disable one to avoid unpredictable results.");
                }
            }

            Harmony = new Harmony("com.dynamicjd.BeatSaber");
            LevelScenesTransitionPatches.Patch(Harmony);
            VariableMovementDataProviderInitPatch.Patch(Harmony);
            InstantNoteRotationPatch.Patch(Harmony);
            CompatibilityPatches.Patch(Harmony);
            AudioOffsetPatches.Patch(Harmony);
            Log.Info("DynamicJD enabled. Profiles: " + string.Join(", ", Profiles.Names));
        }

        [OnDisable]
        public void OnApplicationQuit()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Harmony?.UnpatchSelf();
            Harmony = null;
            NjsCompensation.Disarm();
            ReplayGuard.Refresh(string.Empty);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            AudioOffsetTracker.EndRun();
            ReplayGuard.Refresh(next.name);
            if (ReplayGuard.ShouldApplyModBehaviors) return;
            NjsCompensation.Disarm();
        }
    }
}
