using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using DynamicJD.Api;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Core.Cloning;
using DynamicJD.Services;
using DynamicJD.UI.Common;
using HMUI;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicJD.UI.Clone
{
    internal sealed class CloneViewController : BSMLResourceViewController
    {
        public override string ResourceName => "DynamicJD.UI.Clone.clone.bsml";

        private string _clonePlayerId = string.Empty;
        private string _cloneSearch = string.Empty;
        private string _cloneStatus = "Search BeatLeader by player name.";
        private CloneResult _cloneResult;
        private bool _cloneBusy;
        private bool _searchBusy;
        private List<PlayerSearchResult> _searchResults = new List<PlayerSearchResult>();
        private readonly List<string> _cloneFeed = new List<string>();
        private const int MaxFeedLines = 8;

        [UIComponent("search_results")]
        public CustomListTableData SearchResults { get; set; }

        [UIComponent("clone_search_setting")]
        public StringSetting CloneSearchSetting { get; set; }

        [UIComponent("clone_spinner")]
        public Image CloneSpinner { get; set; }

        [UIAction("#post-parse")]
        public void PostParse()
        {
            ClearSearchResults();
            SpinnerBinder.Bind(CloneSpinner);
        }

        [UIValue("clone_search")]
        public string CloneSearch
        {
            get => _cloneSearch;
            set => _cloneSearch = value ?? string.Empty;
        }

        [UIAction("set_clone_search")]
        public void SetCloneSearch(string value) => CloneSearch = value;

        [UIValue("selected_text")]
        public string SelectedText => string.IsNullOrEmpty(_clonePlayerId)
            ? "<#8c8c8c>No player selected"
            : "<#81c784>Selected: " + _cloneSearch + " <#8c8c8c>(" + _clonePlayerId + ")";

        [UIValue("clone_count_value")]
        public int CloneSampleCount
        {
            get => PluginConfig.Instance.clone_sample_count;
            set
            {
                PluginConfig.Instance.clone_sample_count = Mathf.Clamp(value, 10, 300);
                PluginConfig.Instance.Changed();
            }
        }

        [UIAction("set_clone_count")]
        public void SetCloneCount(int value) => CloneSampleCount = value;

        [UIValue("clone_status")]
        public string CloneStatus => _cloneStatus;

        [UIValue("clone_feed")]
        public string CloneFeed => string.Join("\n", _cloneFeed);

        [UIValue("has_clone_feed")]
        public bool HasCloneFeed => _cloneFeed.Count > 0;

        [UIValue("clone_activity")]
        public bool CloneActivity => _cloneBusy || _searchBusy;

        [UIValue("can_search")]
        public bool CanSearch => !CloneActivity;

        [UIValue("can_build")]
        public bool CanBuild => !CloneActivity && !string.IsNullOrEmpty(_clonePlayerId);

        [UIValue("can_save")]
        public bool CanSave => !CloneActivity && _cloneResult != null &&
                               _cloneResult.Success && _cloneResult.Profile != null;

        [UIValue("can_edit_count")]
        public bool CanEditCount => !CloneActivity;

        [UIAction("search_players")]
        public void SearchPlayers()
        {
            if (_cloneBusy || _searchBusy) return;
            string query = CloneSearchSetting != null ? CloneSearchSetting.Text : _cloneSearch;
            if (string.IsNullOrWhiteSpace(query)) query = _cloneSearch;
            if (string.IsNullOrWhiteSpace(query))
            {
                _cloneStatus = "Type a player name first.";
                RaiseAll();
                return;
            }

            _searchBusy = true;
            _clonePlayerId = string.Empty;
            _cloneResult = null;
            _cloneFeed.Clear();
            _searchResults = new List<PlayerSearchResult>();
            ClearSearchResults();
            _cloneStatus = "Searching BeatLeader…";
            RaiseAll();
            Task.Run(async () =>
            {
                List<PlayerSearchResult> results = null;
                string error = null;
                try
                {
                    results = await Plugin.BeatLeader.SearchPlayersAsync(query, 12).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Warn("BeatLeader search failed for '" + query + "': " + ex.Message);
                    error = ex.Message;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    _searchBusy = false;
                    if (error != null)
                    {
                        _cloneStatus = "Search failed: " + error;
                        RaiseAll();
                        return;
                    }

                    _searchResults = results ?? new List<PlayerSearchResult>();
                    Plugin.Log?.Info("BeatLeader player search '" + query + "' returned " +
                                     _searchResults.Count + " result(s)");
                    _cloneStatus = _searchResults.Count > 0
                        ? "Select a player from the list below."
                        : "No players matched.";
                    PopulateSearchResults();
                    RaiseAll();
                });
            });
        }

        [UIAction("select_search_result")]
        public void SelectSearchResult(TableView tableView, int row)
        {
            if (_cloneBusy || _searchBusy) return;
            if (row < 0 || row >= _searchResults.Count) return;
            PlayerSearchResult result = _searchResults[row];
            if (!string.Equals(_clonePlayerId, result.Id, StringComparison.Ordinal))
            {
                _cloneResult = null;
                _cloneFeed.Clear();
            }

            _clonePlayerId = result.Id;
            _cloneSearch = result.Name;
            _cloneStatus = "Selected " + result.Name + ". Build the setup when ready.";
            RaiseAll();
        }

        [UIAction("analyze_clone")]
        public void AnalyzeClone()
        {
            if (_cloneBusy || _searchBusy) return;
            if (string.IsNullOrWhiteSpace(_clonePlayerId))
            {
                _cloneStatus = "Select a player from the results.";
                RaiseAll();
                return;
            }

            _cloneBusy = true;
            _cloneResult = null;
            _cloneFeed.Clear();
            AddCloneFeed("Starting " + (string.IsNullOrEmpty(_cloneSearch) ? _clonePlayerId : _cloneSearch) + "…");
            _cloneStatus = "Building setup…";
            RaiseAll();

            string id = _clonePlayerId;
            int count = CloneSampleCount;
            Task.Run(async () =>
            {
                CloneResult result;
                Action<CloneProgress> progress = update => MainThreadDispatcher.Enqueue(() =>
                {
                    if (!string.IsNullOrEmpty(update?.Message)) AddCloneFeed(update.Message);
                    RaiseAll();
                });

                try
                {
                    result = await Plugin.Clone.AnalyzeAsync(id, count, progress).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Warn("Clone failed for " + id + ": " + ex);
                    result = new CloneResult { Message = "Clone failed: " + ex.Message };
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    _cloneBusy = false;
                    _cloneResult = result;
                    _cloneStatus = FormatCloneStatus(result);
                    if (result.Success)
                    {
                        AddCloneFeed("Done: " + result.ModdedSamples + " modified plays");
                    }
                    else
                    {
                        AddCloneFeed("Stopped");
                    }

                    RaiseAll();
                });
            });
        }

        [UIAction("adopt_clone")]
        public void AdoptClone()
        {
            if (_cloneBusy || _searchBusy) return;
            if (_cloneResult == null || !_cloneResult.Success || _cloneResult.Profile == null)
            {
                _cloneStatus = "Build a setup first.";
                RaiseAll();
                return;
            }

            JdProfile profile = _cloneResult.Profile;
            profile.EnsureValid();
            string playerId = string.IsNullOrEmpty(profile.PlayerId) ? _clonePlayerId : profile.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                _cloneStatus = "This result has no player ID and cannot be saved.";
                RaiseAll();
                return;
            }

            string playerName = string.IsNullOrEmpty(_cloneSearch) ? playerId : _cloneSearch;
            float typeStrength = Mathf.Clamp01(_cloneResult.RecommendedTypeStrength);
            var entry = new PerPlayerEntry
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Base = profile.Base.Clone(),
                SampleCount = profile.SampleCount,
                Confidence = profile.Confidence,
            };

            PluginConfig.Instance.type_model_strength = typeStrength;
            PluginConfig.Instance.Changed();
            Plugin.PerPlayer.Upsert(entry);
            Plugin.PerPlayer.Select(entry.PlayerId);
            PerPlayerProfileService.Sync(Plugin.Profiles, Plugin.PerPlayer);
            Plugin.Profiles.SetActive(DefaultProfiles.PerPlayerName);
            _cloneStatus = "<#81c784>Saved to Per-Player: " + entry.PlayerName +
                           ". Global type influence set to " + Math.Round(typeStrength * 100f) + "%.";
            RaiseAll();
        }

        private void ClearSearchResults()
        {
            if (SearchResults == null || SearchResults.TableView == null) return;
            SearchResults.Data = new List<CustomListTableData.CustomCellInfo>();
            SearchResults.TableView.ReloadData();
            SearchResults.TableView.ClearSelection();
        }

        private void PopulateSearchResults()
        {
            if (SearchResults == null)
            {
                Plugin.Log?.Warn("Clone search list component was not bound");
                return;
            }
            if (SearchResults.TableView == null)
            {
                Plugin.Log?.Warn("Clone search list table view is missing");
                return;
            }
            SearchResults.Data = _searchResults
                .Select(r => new CustomListTableData.CustomCellInfo(
                    "#" + r.Rank + "  " + r.Name + (string.IsNullOrEmpty(r.Country) ? "" : "  (" + r.Country + ")"),
                    Math.Round(r.Pp).ToString("0", CultureInfo.InvariantCulture) + " pp"))
                .ToList();
            SearchResults.TableView.SetDataSource(SearchResults, true);
            SearchResults.TableView.ReloadData();
            SearchResults.TableView.ClearSelection();
        }

        private void AddCloneFeed(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (_cloneFeed.Count > 0 && _cloneFeed[_cloneFeed.Count - 1] == line) return;
            _cloneFeed.Add(line);
            while (_cloneFeed.Count > MaxFeedLines) _cloneFeed.RemoveAt(0);
        }

        private static string FormatCloneStatus(CloneResult result)
        {
            if (result == null) return string.Empty;
            if (!result.Success)
            {
                string failure = "<#ff8a80>" + result.Message;
                if (result.Warnings.Count > 0) failure += "\n<#ffd166>Errors: " + result.Warnings.Count;
                return failure;
            }

            string counts = string.Join(" · ", result.SampleCounts
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key + " " + kv.Value));
            string text = "<#81c784>" + result.Message;
            if (!string.IsNullOrEmpty(counts)) text += "\n<#8c8c8c>Samples: " + counts;
            int cleanSamples = result.Profile?.SampleCount ?? result.ModdedSamples;
            float confidence = result.Profile?.Confidence ?? 0f;
            text += "\n<#8c8c8c>" + cleanSamples + " clean plays  ·  " +
                    Math.Round(confidence * 100f) + "% confidence";
            if (cleanSamples < 50)
            {
                text += "  ·  <#ffd166>Low; recommend 80+ clean plays";
            }
            if (result.SkippedSpeedModifiers > 0)
            {
                text += "\n<#8c8c8c>Skipped " + result.SkippedSpeedModifiers + " speed-modifier plays";
            }
            if (result.Warnings.Count > 0)
            {
                text += "\n<#ffd166>Errors: " + result.Warnings.Count;
            }
            return text;
        }

        private void RaiseAll()
        {
            NotifyPropertyChanged(nameof(SelectedText));
            NotifyPropertyChanged(nameof(CloneStatus));
            NotifyPropertyChanged(nameof(CloneFeed));
            NotifyPropertyChanged(nameof(HasCloneFeed));
            NotifyPropertyChanged(nameof(CloneSearch));
            NotifyPropertyChanged(nameof(CloneActivity));
            NotifyPropertyChanged(nameof(CanSearch));
            NotifyPropertyChanged(nameof(CanBuild));
            NotifyPropertyChanged(nameof(CanSave));
            NotifyPropertyChanged(nameof(CanEditCount));
        }
    }
}
