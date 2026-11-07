using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicJD.Core;
using Newtonsoft.Json;

namespace DynamicJD.Configuration
{
    /// <summary>Stores multiple cloned player base setups and the one selected for the Per-Player profile.</summary>
    public sealed class PerPlayerStore
    {
        private sealed class FileModel
        {
            public string SelectedPlayerId { get; set; }
            public List<PerPlayerEntry> Entries { get; set; } = new List<PerPlayerEntry>();
        }

        private readonly string _path;
        private readonly List<PerPlayerEntry> _entries = new List<PerPlayerEntry>();
        private string _selectedId = string.Empty;

        public event Action Changed;

        public PerPlayerStore(string directory)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "per_players.json");
            Load();
        }

        public IReadOnlyList<PerPlayerEntry> Entries => _entries;
        public string SelectedPlayerId => _selectedId;

        public PerPlayerEntry SelectedEntry
        {
            get
            {
                PerPlayerEntry entry = _entries.FirstOrDefault(e =>
                    string.Equals(e.PlayerId, _selectedId, StringComparison.OrdinalIgnoreCase));
                return entry ?? _entries.FirstOrDefault();
            }
        }

        public void Upsert(PerPlayerEntry entry)
        {
            if (entry == null) return;
            entry.EnsureValid();
            entry.UpdatedAt = DateTime.UtcNow.ToString("o");
            _entries.RemoveAll(e => string.Equals(e.PlayerId, entry.PlayerId, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, entry);
            if (string.IsNullOrEmpty(_selectedId)) _selectedId = entry.PlayerId;
            Save();
            Changed?.Invoke();
        }

        public void Select(string playerId)
        {
            if (_entries.All(e => !string.Equals(e.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))) return;
            _selectedId = playerId;
            Save();
            Changed?.Invoke();
        }

        public void Remove(string playerId)
        {
            int removed = _entries.RemoveAll(e => string.Equals(e.PlayerId, playerId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;
            if (string.Equals(_selectedId, playerId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedId = _entries.FirstOrDefault()?.PlayerId ?? string.Empty;
            }

            Save();
            Changed?.Invoke();
        }

        public void SeedFromProfile(JdProfile profile)
        {
            if (_entries.Count > 0 || profile == null) return;
            if (string.IsNullOrEmpty(profile.PlayerId) && string.IsNullOrEmpty(profile.PlayerName)) return;
            profile.EnsureValid();
            _entries.Add(new PerPlayerEntry
            {
                PlayerId = profile.PlayerId ?? string.Empty,
                PlayerName = profile.PlayerName ?? profile.PlayerId ?? "Unknown player",
                Base = profile.Base.Clone(),
                SampleCount = profile.SampleCount,
                Confidence = profile.Confidence,
                Notes = profile.Notes ?? string.Empty,
                UpdatedAt = profile.LastAnalyzedAt ?? profile.CreatedAt ?? DateTime.UtcNow.ToString("o"),
            });
            _selectedId = _entries[0].PlayerId;
        }

        private void Save()
        {
            var model = new FileModel { SelectedPlayerId = _selectedId, Entries = _entries };
            string json = JsonConvert.SerializeObject(model, Formatting.Indented);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var model = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(_path));
                if (model?.Entries == null) return;
                _entries.Clear();
                _entries.AddRange(model.Entries.Where(e => e != null));
                foreach (PerPlayerEntry entry in _entries) entry.EnsureValid();
                _selectedId = model.SelectedPlayerId ?? string.Empty;
                if (_entries.All(e => !string.Equals(e.PlayerId, _selectedId, StringComparison.OrdinalIgnoreCase)))
                {
                    _selectedId = _entries.FirstOrDefault()?.PlayerId ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not read per_players.json: " + ex.Message);
            }
        }
    }
}
