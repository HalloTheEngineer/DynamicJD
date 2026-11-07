using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.GameplaySetup;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Services;
using DynamicJD.UI.Clone;
using DynamicJD.UI.Common;
using HMUI;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace DynamicJD.UI.Menu
{
    internal sealed class MainMenuTab : IInitializable, IDisposable, INotifyPropertyChanged
    {
        public const string TabName = "DynamicJD";
        private const string Resource = "DynamicJD.UI.Menu.mainMenu.bsml";

        private readonly JdUIManager _uiManager;
        private readonly MainFlowCoordinator _mainFlow;
        private readonly CloneFlowCoordinator _cloneFlow;
        private readonly PlayerDataModel _playerDataModel;
        private string _suggestionStatus = string.Empty;
        private bool _suggestionBusy;

        [Inject]
        public MainMenuTab(JdUIManager uiManager, MainFlowCoordinator mainFlow,
            CloneFlowCoordinator cloneFlow, PlayerDataModel playerDataModel)
        {
            _uiManager = uiManager;
            _mainFlow = mainFlow;
            _cloneFlow = cloneFlow;
            _playerDataModel = playerDataModel;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [UIComponent("profile_increment")]
        public IncrementSetting ProfileIncrement { get; set; }

        [UIComponent("per_player_increment")]
        public IncrementSetting PerPlayerIncrement { get; set; }

        [UIComponent("suggestion_spinner")]
        public Image SuggestionSpinner { get; set; }

        public void Initialize()
        {
            _uiManager.MapChanged += OnMapChanged;
            Plugin.Profiles.Changed += OnProfilesChanged;
            Plugin.ManualOverrideChanged += OnProfilesChanged;
            PerPlayerProfileService.Sync(Plugin.Profiles, Plugin.PerPlayer);
            GameplaySetup.Instance?.AddTab(TabName, Resource, this,
                MenuType.Solo | MenuType.Campaign | MenuType.Online);
        }

        public void Dispose()
        {
            _uiManager.MapChanged -= OnMapChanged;
            Plugin.Profiles.Changed -= OnProfilesChanged;
            Plugin.ManualOverrideChanged -= OnProfilesChanged;
            GameplaySetup.Instance?.RemoveTab(TabName);
        }

        private void OnMapChanged(MapContext ctx) => RaiseAll();

        private void OnProfilesChanged() => RaiseAll();

        [UIAction("open_clone")]
        public void OpenClone()
        {
            FlowCoordinator parent = _mainFlow.YoungestChildFlowCoordinatorOrSelf();
            _cloneFlow.ParentFlow = parent;
            parent.PresentFlowCoordinator(_cloneFlow);
        }

        [UIAction("#post-parse")]
        public void PostParse()
        {
            SyncProfileIncrement();
            SyncPerPlayerIncrement();
            SpinnerBinder.Bind(SuggestionSpinner);
        }

        // ------------------------------------------------------------------ header / config

        [UIValue("enabled")]
        public bool Enabled
        {
            get => PluginConfig.Instance.enabled;
            set
            {
                PluginConfig.Instance.enabled = value;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_enabled")]
        public void SetEnabled(bool value) => Enabled = value;

        [UIValue("instant_note_rotation")]
        public bool InstantNoteRotation
        {
            get => PluginConfig.Instance.instant_note_rotation;
            set
            {
                PluginConfig.Instance.instant_note_rotation = value;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_instant_note_rotation")]
        public void SetInstantNoteRotation(bool value) => InstantNoteRotation = value;

        [UIValue("profile_index")]
        public int ProfileIndex
        {
            get
            {
                IReadOnlyList<JdProfile> profiles = Plugin.Profiles.Profiles;
                for (int i = 0; i < profiles.Count; i++)
                {
                    if (profiles[i].Name == Plugin.Profiles.ActiveName) return i;
                }
                return 0;
            }
            set
            {
                IReadOnlyList<JdProfile> profiles = Plugin.Profiles.Profiles;
                if (profiles.Count == 0) return;
                value = Mathf.Clamp(value, 0, profiles.Count - 1);
                Plugin.Profiles.SetActive(profiles[value].Name);
                if (profiles[value].Role == ProfileRole.PerPlayer)
                {
                    PerPlayerProfileService.Sync(Plugin.Profiles, Plugin.PerPlayer);
                }
                else if (profiles[value].Role == ProfileRole.Suggested &&
                         profiles[value].SampleCount == 0 && string.IsNullOrEmpty(_suggestionStatus))
                {
                    _suggestionStatus = "<#8c8c8c>Click Refresh suggestion to analyze your plays.";
                }
                RaiseAll();
            }
        }

        [UIValue("profile_max")]
        public int ProfileMax => Math.Max(0, Plugin.Profiles.Profiles.Count - 1);

        [UIAction("set_profile_index")]
        public void SetProfileIndex(int value) => ProfileIndex = value;

        [UIAction("profile_formatter")]
        public string ProfileFormatter(int index)
        {
            IReadOnlyList<JdProfile> profiles = Plugin.Profiles.Profiles;
            if (index < 0 || index >= profiles.Count) return "—";
            JdProfile profile = profiles[index];
            switch (profile.Role)
            {
                case ProfileRole.Suggested:
                    return profile.SampleCount > 0 ? "Suggested" : "Suggested (not analyzed)";
                case ProfileRole.PerPlayer:
                    return string.IsNullOrEmpty(profile.PlayerName)
                        ? "Per-Player (not set)"
                        : "Per-Player: " + profile.PlayerName;
                default:
                    return profile.Name;
            }
        }

        [UIValue("is_custom_active")]
        public bool IsCustomActive => Plugin.Profiles.Active?.Role == ProfileRole.Custom;

        [UIValue("is_per_player_active")]
        public bool IsPerPlayerActive => Plugin.Profiles.Active?.Role == ProfileRole.PerPlayer;

        [UIValue("has_per_player_entries")]
        public bool HasPerPlayerEntries => Plugin.PerPlayer.Entries.Count > 0;

        [UIValue("show_per_player_selector")]
        public bool ShowPerPlayerSelector => IsPerPlayerActive && HasPerPlayerEntries;

        [UIValue("per_player_max")]
        public int PerPlayerMax => Math.Max(0, Plugin.PerPlayer.Entries.Count - 1);

        [UIValue("per_player_index")]
        public int PerPlayerIndex
        {
            get
            {
                PerPlayerEntry selected = Plugin.PerPlayer.SelectedEntry;
                if (selected == null) return 0;
                for (int i = 0; i < Plugin.PerPlayer.Entries.Count; i++)
                {
                    if (ReferenceEquals(Plugin.PerPlayer.Entries[i], selected)) return i;
                }
                return 0;
            }
            set
            {
                IReadOnlyList<PerPlayerEntry> entries = Plugin.PerPlayer.Entries;
                if (entries.Count == 0) return;
                int index = Mathf.Clamp(value, 0, entries.Count - 1);
                Plugin.PerPlayer.Select(entries[index].PlayerId);
                PerPlayerProfileService.Sync(Plugin.Profiles, Plugin.PerPlayer);
                RaiseAll();
            }
        }

        [UIAction("set_per_player_index")]
        public void SetPerPlayerIndex(int value) => PerPlayerIndex = value;

        [UIAction("per_player_formatter")]
        public string PerPlayerFormatter(int index)
        {
            IReadOnlyList<PerPlayerEntry> entries = Plugin.PerPlayer.Entries;
            return index >= 0 && index < entries.Count ? entries[index].PlayerName : "No players";
        }

        [UIValue("per_player_summary")]
        public string PerPlayerSummary
        {
            get
            {
                PerPlayerEntry entry = Plugin.PerPlayer.SelectedEntry;
                if (entry == null) return "<#8c8c8c>No player selected. Copy a player's setup to add one.";
                entry.EnsureValid();
                string summary = "<#8c8c8c>" + entry.PlayerName + "  ·  " +
                                 JumpDistanceMath.FormatRt(entry.Base.Rt20) + " @ NJS 20  ·  slope " +
                                 entry.Base.SlopeMsPerNjs.ToString("0.0", CultureInfo.InvariantCulture) + " ms/NJS";
                if (entry.SampleCount > 0)
                {
                    summary += "  ·  " + entry.SampleCount + " plays  ·  " +
                               Math.Round(entry.Confidence * 100f) + "% confidence";
                }
                if (Math.Abs(entry.Base.CurvatureMsPerNjs2) > 0.0001f)
                {
                    summary += "  ·  curve " +
                               entry.Base.CurvatureMsPerNjs2.ToString("0.00", CultureInfo.InvariantCulture) +
                               " ms/NJS²";
                }
                return summary;
            }
        }

        [UIAction("remove_per_player")]
        public void RemovePerPlayer()
        {
            PerPlayerEntry entry = Plugin.PerPlayer.SelectedEntry;
            if (entry == null) return;
            Plugin.PerPlayer.Remove(entry.PlayerId);
            PerPlayerProfileService.Sync(Plugin.Profiles, Plugin.PerPlayer);
            RaiseAll();
        }

        [UIValue("profile_description")]
        public string ProfileDescription
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                if (profile == null) return "No profile selected.";
                string description = profile.Notes ?? string.Empty;
                if (profile.Role == ProfileRole.Suggested && profile.SampleCount > 0)
                {
                    description += "  ·  " + profile.SampleCount + " plays  ·  " +
                                   Math.Round(profile.Confidence * 100f) + "% confidence";
                }
                return "<#8c8c8c>" + description;
            }
        }

        [UIValue("suggestion_status")]
        public string SuggestionStatus => _suggestionStatus;

        [UIValue("show_suggestion_button")]
        public bool ShowSuggestionButton => Plugin.Profiles.Active?.Role == ProfileRole.Suggested;

        [UIValue("suggestion_busy")]
        public bool SuggestionBusy => _suggestionBusy;

        [UIValue("suggestion_interactable")]
        public bool SuggestionInteractable => !_suggestionBusy;

        [UIAction("refresh_suggestion")]
        public void RefreshSuggestion()
        {
            if (_suggestionBusy)
            {
                _suggestionStatus = "<#ffd166>Analysis is already running…";
                RaiseAll();
                return;
            }

            if (!TryResolveSuggestionIdentity(out string playerId, out string playerName))
            {
                _suggestionStatus = "<#ff8a80>No BeatLeader identity found. Set a Beat Saber player name or run the game with Steam.";
                Plugin.Log?.Warn("Suggested profile: no local player ID or usable player name was available.");
                RaiseAll();
                return;
            }

            _suggestionBusy = true;
            string target = string.IsNullOrEmpty(playerName) ? playerId : playerName;
            _suggestionStatus = "<#8c8c8c>Analyzing " + target + "…";
            Plugin.Log?.Info("Suggested profile: analyzing " + target + " (" + playerId + ")");
            RaiseAll();
            Task.Run(async () =>
            {
                JdProfile suggestion;
                try
                {
                    suggestion = await Plugin.Suggested.AnalyzeAsync(playerId, playerName, 120)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Warn("Suggested profile failed: " + ex);
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        _suggestionBusy = false;
                        _suggestionStatus = "<#ff8a80>Suggestion failed: " + ex.Message;
                        RaiseAll();
                    });
                    return;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    _suggestionBusy = false;
                    Plugin.Profiles.Upsert(suggestion, true);
                    _suggestionStatus = suggestion.Notes;
                    Plugin.Log?.Info("Suggested profile: " + suggestion.Notes);
                    RaiseAll();
                });
            });
        }

        private static void ReadSteamIdentity(out string playerId, out string playerName)
        {
            playerId = string.Empty;
            playerName = string.Empty;

            Type steamApi = FindType("Steamworks.SteamAPI");
            Type steamUser = FindType("Steamworks.SteamUser");
            Type steamFriends = FindType("Steamworks.SteamFriends");
            if (steamApi == null || steamUser == null)
            {
                return;
            }

            bool running = ToBool(InvokeStatic(steamApi, "IsSteamRunning"));
            bool loggedOn = ToBool(InvokeStatic(steamUser, "BLloggedOn"));
            if (!running || !loggedOn) return;

            object steamId = InvokeStatic(steamUser, "GetSteamID");
            if (steamId != null)
            {
                Type idType = steamId.GetType();
                MethodInfo valid = idType.GetMethod("IsValid", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo value = idType.GetField("m_SteamID", BindingFlags.Public | BindingFlags.Instance);
                bool isValid = valid != null && ToBool(valid.Invoke(steamId, null));
                ulong id = value == null ? 0UL : Convert.ToUInt64(value.GetValue(steamId), CultureInfo.InvariantCulture);
                if (isValid && id != 0) playerId = id.ToString(CultureInfo.InvariantCulture);
            }

            string name = InvokeStatic(steamFriends, "GetPersonaName") as string;
            if (IsUsablePlayerName(name)) playerName = name.Trim();
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }

            return null;
        }

        private static object InvokeStatic(Type type, string name)
        {
            MethodInfo method = type?.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, null);
        }

        private static bool ToBool(object value)
        {
            return value is bool result && result;
        }

        private bool TryResolveSuggestionIdentity(out string playerId, out string playerName)
        {
            playerId = string.Empty;
            playerName = string.Empty;

            try
            {
                ReadSteamIdentity(out playerId, out playerName);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not read the Steam player identity: " + ex.Message);
            }

            if (string.IsNullOrEmpty(playerId))
            {
                string id = _playerDataModel?.playerData?.playerId;
                if (!string.IsNullOrWhiteSpace(id) && id.Trim() != "0") playerId = id.Trim();
            }

            if (string.IsNullOrEmpty(playerName))
            {
                string name = _playerDataModel?.playerData?.playerName;
                if (IsUsablePlayerName(name)) playerName = name.Trim();
            }

            return !string.IsNullOrEmpty(playerId) || !string.IsNullOrEmpty(playerName);
        }

        private static bool IsUsablePlayerName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   !string.Equals(name.Trim(), "<NO NAME!>", StringComparison.OrdinalIgnoreCase);
        }

        [UIValue("has_map")]
        public bool HasMap => MapContext.Selected != null && MapContext.Selected.HasMap;

        [UIValue("config_mode")]
        public int ConfigMode
        {
            get => PluginConfig.Instance.config_mode;
            set
            {
                PluginConfig.Instance.config_mode = Mathf.Clamp(value, 0, 1);
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_config_mode")]
        public void SetConfigMode(int value) => ConfigMode = value;

        [UIAction("config_mode_formatter")]
        public string ConfigModeFormatter(int value) => value == 1 ? "Jump Distance" : "Reaction Time";

        [UIValue("show_rt_editor")]
        public bool ShowRtEditor => IsCustomActive && ConfigMode == 0;

        [UIValue("show_jd_editor")]
        public bool ShowJdEditor => IsCustomActive && ConfigMode == 1;

        [UIValue("rt_value")]
        public float RtValue
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                profile?.EnsureValid();
                return profile?.Base.Rt20 ?? 220f;
            }
            set
            {
                JdProfile profile = EditableProfile();
                if (profile == null) return;
                profile.EnsureValid();
                profile.Base.Rt20 = Mathf.Clamp(value, 100f, 900f);
                Plugin.Profiles.Save();
                RaiseAll();
            }
        }

        [UIAction("set_rt_value")]
        public void SetRtValue(float value) => RtValue = value;

        [UIAction("rt_formatter")]
        public string RtFormatter(float value) => JumpDistanceMath.FormatRt(value);

        [UIValue("jd_value")]
        public float JdValue
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                profile?.EnsureValid();
                return profile?.Base.Jd20 ?? 17.6f;
            }
            set
            {
                JdProfile profile = EditableProfile();
                if (profile == null) return;
                profile.EnsureValid();
                profile.Base.Jd20 = Mathf.Clamp(value, 8f, 72f);
                Plugin.Profiles.Save();
                RaiseAll();
            }
        }

        [UIAction("set_jd_value")]
        public void SetJdValue(float value) => JdValue = value;

        [UIAction("jd_formatter")]
        public string JdFormatter(float value) => JumpDistanceMath.FormatJd(value);

        [UIValue("slope_value")]
        public float SlopeValue
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                profile?.EnsureValid();
                return profile?.Base.SlopeMsPerNjs ?? -1.8f;
            }
            set
            {
                JdProfile profile = EditableProfile();
                if (profile == null) return;
                profile.EnsureValid();
                profile.Base.SlopeMsPerNjs = Mathf.Clamp(value, -35f, 5f);
                Plugin.Profiles.Save();
                RaiseAll();
            }
        }

        [UIAction("set_slope_value")]
        public void SetSlopeValue(float value) => SlopeValue = value;

        [UIAction("slope_formatter")]
        public string SlopeFormatter(float value) => value.ToString("0.0", CultureInfo.InvariantCulture) + " ms/NJS";

        [UIValue("curvature_value")]
        public float CurvatureValue
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                profile?.EnsureValid();
                return profile?.Base.CurvatureMsPerNjs2 ?? 0f;
            }
            set
            {
                JdProfile profile = EditableProfile();
                if (profile == null) return;
                profile.EnsureValid();
                profile.Base.CurvatureMsPerNjs2 = Mathf.Clamp(value, -2f, 2f);
                Plugin.Profiles.Save();
                RaiseAll();
            }
        }

        [UIAction("set_curvature_value")]
        public void SetCurvatureValue(float value) => CurvatureValue = value;

        [UIAction("curvature_formatter")]
        public string CurvatureFormatter(float value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture) + " ms/NJS²";

        [UIValue("apply_strength")]
        public float ApplyStrength
        {
            get => PluginConfig.Instance.strength;
            set
            {
                PluginConfig.Instance.strength = Mathf.Clamp01(value);
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_apply_strength")]
        public void SetApplyStrength(float value) => ApplyStrength = value;

        [UIAction("apply_strength_formatter")]
        public string ApplyStrengthFormatter(float value) =>
            (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "% influence";

        [UIValue("not_manual")]
        public bool NotManual => !PluginConfig.Instance.manual_override;

        // ------------------------------------------------------------------ type model

        [UIValue("type_model_index")]
        public int TypeModelIndex
        {
            get
            {
                string active = PluginConfig.Instance.type_model;
                for (int i = 0; i < TypeModels.All.Count; i++)
                {
                    if (string.Equals(TypeModels.All[i].Name, active, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return 0;
            }
            set
            {
                int index = Mathf.Clamp(value, 0, TypeModels.All.Count - 1);
                PluginConfig.Instance.type_model = TypeModels.All[index].Name;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIValue("type_model_max")]
        public int TypeModelMax => TypeModels.All.Count - 1;

        [UIAction("set_type_model_index")]
        public void SetTypeModelIndex(int value) => TypeModelIndex = value;

        [UIAction("type_model_formatter")]
        public string TypeModelFormatter(int index)
        {
            return index >= 0 && index < TypeModels.All.Count ? TypeModels.All[index].Name : "—";
        }

        [UIValue("type_model_strength")]
        public float TypeModelStrength
        {
            get => PluginConfig.Instance.type_model_strength;
            set
            {
                PluginConfig.Instance.type_model_strength = Mathf.Clamp01(value);
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_type_model_strength")]
        public void SetTypeModelStrength(float value) => TypeModelStrength = value;

        [UIAction("type_model_strength_formatter")]
        public string TypeModelStrengthFormatter(float value) =>
            (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

        // ------------------------------------------------------------------ advanced

        [UIValue("njs_event_compensation")]
        public bool NjsEventCompensation
        {
            get => PluginConfig.Instance.njs_event_compensation;
            set
            {
                PluginConfig.Instance.njs_event_compensation = value;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_njs_event_compensation")]
        public void SetNjsEventCompensation(bool value) => NjsEventCompensation = value;

        [UIValue("njs_compensation_interactable")]
        public bool NjsCompensationInteractable => PreserveRtAtSongSpeed;

        [UIValue("apply_in_replays")]
        public bool ApplyInReplays
        {
            get => PluginConfig.Instance.apply_in_replays;
            set
            {
                PluginConfig.Instance.apply_in_replays = value;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_apply_in_replays")]
        public void SetApplyInReplays(bool value) => ApplyInReplays = value;

        [UIValue("preserve_rt_at_song_speed")]
        public bool PreserveRtAtSongSpeed
        {
            get => PluginConfig.Instance.song_speed_mode == 1;
            set
            {
                PluginConfig.Instance.song_speed_mode = value ? 1 : 0;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_preserve_rt_at_song_speed")]
        public void SetPreserveRtAtSongSpeed(bool value) => PreserveRtAtSongSpeed = value;

        [UIValue("respect_vanilla_offset")]
        public bool RespectVanillaOffset
        {
            get => PluginConfig.Instance.respect_vanilla_offset;
            set
            {
                PluginConfig.Instance.respect_vanilla_offset = value;
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_respect_vanilla_offset")]
        public void SetRespectVanillaOffset(bool value) => RespectVanillaOffset = value;

        [UIValue("min_jump_distance")]
        public float MinJumpDistance
        {
            get => PluginConfig.Instance.min_jump_distance;
            set
            {
                float next = Mathf.Clamp(value, 1f, 99f);
                PluginConfig.Instance.min_jump_distance = next;
                if (PluginConfig.Instance.max_jump_distance <= next)
                {
                    PluginConfig.Instance.max_jump_distance = Mathf.Min(100f, next + 1f);
                }
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_min_jump_distance")]
        public void SetMinJumpDistance(float value) => MinJumpDistance = value;

        [UIAction("min_jump_distance_formatter")]
        public string MinJumpDistanceFormatter(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " JD";

        [UIValue("max_jump_distance")]
        public float MaxJumpDistance
        {
            get => PluginConfig.Instance.max_jump_distance;
            set
            {
                float next = Mathf.Clamp(value, 2f, 100f);
                PluginConfig.Instance.max_jump_distance = next;
                if (PluginConfig.Instance.min_jump_distance >= next)
                {
                    PluginConfig.Instance.min_jump_distance = Mathf.Max(1f, next - 1f);
                }
                PluginConfig.Instance.Changed();
                RaiseAll();
            }
        }

        [UIAction("set_max_jump_distance")]
        public void SetMaxJumpDistance(float value) => MaxJumpDistance = value;

        [UIAction("max_jump_distance_formatter")]
        public string MaxJumpDistanceFormatter(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " JD";

        [UIValue("type_model_summary")]
        public string TypeModelSummary
        {
            get
            {
                MapContext ctx = MapContext.Selected;
                TypeModel model = TypeModels.Get(PluginConfig.Instance.type_model);
                if (ctx == null || !ctx.HasMap)
                {
                    return "<#8c8c8c>" + model.Name + " · " + model.Description;
                }

                float njs = ctx.Njs;
                float factor = model.BlendedFactor(ctx.Type, njs, PluginConfig.Instance.type_model_strength);
                JdProfile profile = Plugin.Profiles.Active;
                profile?.EnsureValid();
                float baseRt = profile?.Base.EvaluateReactionTime(njs) ?? 0f;
                return "<#8c8c8c>" + model.Name + " · " + (ctx.Type?.ChipText ?? "TYPE UNKNOWN") +
                       "  <#8c8c8c>NJS " + JumpDistanceMath.FormatNjs(njs) +
                       "  ×" + factor.ToString("0.000", CultureInfo.InvariantCulture) +
                       "  " + JumpDistanceMath.FormatRt(baseRt) + " → " +
                       JumpDistanceMath.FormatRt(baseRt * factor);
            }
        }

        // ------------------------------------------------------------------ map readout

        [UIValue("map_title")]
        public string MapTitle
        {
            get
            {
                MapContext ctx = MapContext.Selected;
                if (ctx == null || !ctx.HasMap) return "<#8c8c8c>No map selected";
                return ctx.TitleLine + "   " + (ctx.Type?.ChipText ?? "<#8c8c8c>TYPE UNKNOWN");
            }
        }

        [UIValue("type_chips")]
        public string TypeChips => MapContext.Selected?.Type?.ChipText ?? "<#8c8c8c>TYPE UNKNOWN";

        [UIValue("map_jd")]
        public string MapJumpDistance
        {
            get
            {
                MapContext ctx = MapContext.Selected;
                if (ctx == null || !ctx.HasMap) return "<#8c8c8c>—";
                return "<#ffff00>" + JumpDistanceMath.FormatJd(ctx.OriginalJumpDistance) +
                       "  <#c58bff>" + JumpDistanceMath.FormatRt(
                           JumpDistanceMath.CalculateReactionTime(ctx.OriginalJumpDistance, ctx.Njs));
            }
        }

        [UIValue("map_njs")]
        public string MapNjs
        {
            get
            {
                MapContext ctx = MapContext.Selected;
                return ctx == null || !ctx.HasMap ? "<#8c8c8c>—" : JumpDistanceMath.FormatNjs(ctx.Njs);
            }
        }

        [UIValue("target_jd_rt")]
        public string TargetJumpDistanceReactionTime
        {
            get
            {
                ResolvedJd resolved = ResolveSelected();
                return resolved == null
                    ? "<#8c8c8c>—"
                    : "<#ffff00>" + JumpDistanceMath.FormatJd(resolved.TargetJumpDistance) +
                      "  <#c58bff>" + JumpDistanceMath.FormatRt(resolved.ReactionTime);
            }
        }

        [UIValue("model_summary")]
        public string ModelSummary
        {
            get
            {
                JdProfile profile = Plugin.Profiles.Active;
                if (profile == null) return "<#8c8c8c>No active config";
                profile.EnsureValid();
                string summary = "<#8c8c8c>" + profile.Name + "  ·  " +
                                 JumpDistanceMath.FormatRt(profile.Base.Rt20) + " @ NJS 20  ·  slope " +
                                 profile.Base.SlopeMsPerNjs.ToString("0.0", CultureInfo.InvariantCulture) + " ms/NJS";
                if (Math.Abs(profile.Base.CurvatureMsPerNjs2) > 0.0001f)
                {
                    summary += "  ·  curve " +
                               profile.Base.CurvatureMsPerNjs2.ToString("0.00", CultureInfo.InvariantCulture) +
                               " ms/NJS²";
                }
                return summary;
            }
        }

        private ResolvedJd ResolveSelected()
        {
            MapContext ctx = MapContext.Selected;
            if (ctx == null || !ctx.HasMap) return null;
            PluginConfig config = PluginConfig.Instance;
            return JdResolver.Resolve(new ResolveContext
            {
                Enabled = config.enabled,
                Bpm = ctx.Bpm,
                Njs = ctx.Njs,
                MapOffset = ctx.MapOffset,
                OriginalJumpDistance = ctx.OriginalJumpDistance,
                Type = ctx.Type,
                Profile = Plugin.Profiles.Active,
                TypeModel = TypeModels.Get(config.type_model),
                TypeModelStrength = config.type_model_strength,
                ApplyStrength = config.strength,
                Override = Plugin.Overrides.Get(ctx.Key),
                ManualOverride = config.manual_override,
                ManualJumpDistance = config.manual_jd,
                MinJumpDistance = config.min_jump_distance,
                MaxJumpDistance = config.max_jump_distance,
                PreserveReactionTime = config.song_speed_mode == 1,
                SongSpeedMultiplier = 1f,
            });
        }

        private JdProfile EditableProfile()
        {
            JdProfile profile = Plugin.Profiles.Active;
            if (profile == null) return null;
            if (profile.Role == ProfileRole.Custom) return profile;

            JdProfile custom = profile.Clone(DefaultProfiles.CustomName);
            custom.Role = ProfileRole.Custom;
            custom.Name = DefaultProfiles.CustomName;
            custom.Source = "custom";
            custom.Notes = "Your own setup, copied from " + profile.Name + ".";
            return Plugin.Profiles.Upsert(custom, true);
        }

        private void SyncProfileIncrement()
        {
            if (ProfileIncrement == null) return;
            int max = Math.Max(0, Plugin.Profiles.Profiles.Count - 1);
            ProfileIncrement.MinValue = 0;
            ProfileIncrement.MaxValue = max;
            ProfileIncrement.Value = Mathf.Clamp(ProfileIndex, 0, max);
        }

        private void SyncPerPlayerIncrement()
        {
            if (PerPlayerIncrement == null) return;
            int max = Math.Max(0, Plugin.PerPlayer.Entries.Count - 1);
            PerPlayerIncrement.MinValue = 0;
            PerPlayerIncrement.MaxValue = max;
            PerPlayerIncrement.Value = Mathf.Clamp(PerPlayerIndex, 0, max);
        }

        private void RaiseAll()
        {
            SyncProfileIncrement();
            SyncPerPlayerIncrement();
            foreach (string name in new[]
            {
                "MapTitle", "TypeChips", "MapJumpDistance", "MapNjs", "TargetJumpDistanceReactionTime",
                "ModelSummary", "ProfileIndex", "ProfileMax", "HasMap", "Enabled",
                "IsCustomActive", "IsPerPlayerActive", "HasPerPlayerEntries", "ShowPerPlayerSelector",
                "ProfileDescription",
                "SuggestionStatus", "ShowSuggestionButton", "SuggestionBusy", "SuggestionInteractable",
                "PerPlayerIndex", "PerPlayerMax",
                "PerPlayerSummary",
                "ConfigMode", "ShowRtEditor", "ShowJdEditor", "RtValue", "JdValue", "SlopeValue",
                "CurvatureValue", "ApplyStrength", "NotManual", "InstantNoteRotation",
                "TypeModelIndex", "TypeModelMax", "TypeModelStrength", "TypeModelSummary",
                "NjsEventCompensation", "NjsCompensationInteractable", "ApplyInReplays", "PreserveRtAtSongSpeed", "RespectVanillaOffset",
                "MinJumpDistance", "MaxJumpDistance"
            })
            {
                RaiseProperty(name);
            }
        }

        private void RaiseProperty(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null) return;
            try
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Debug("DynamicJD UI update skipped for " + name + ": " + ex.Message);
            }
        }
    }
}
