using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicJD.Core;
using Newtonsoft.Json;

namespace DynamicJD.Configuration
{
    /// <summary>
    /// Fixed-role profile store. Built-ins and generated profiles are read-only; only Custom is editable.
    /// Old profile files are migrated into the fixed roles, keeping the most recent cloned profile as
    /// the Per-Player slot.
    /// </summary>
    public sealed class ProfileStore
    {
        private sealed class FileModel
        {
            public string ActiveProfile { get; set; }
            public List<JdProfile> Profiles { get; set; } = new List<JdProfile>();
        }

        private readonly string _path;
        private readonly List<JdProfile> _profiles = new List<JdProfile>();
        private string _activeName = DefaultProfiles.CustomName;

        public event Action Changed;

        public ProfileStore(string directory)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "profiles.json");
            Load();
        }

        public IReadOnlyList<JdProfile> Profiles => _profiles;
        public IReadOnlyList<string> Names => _profiles.Select(p => p.Name).ToList();
        public string ActiveName => _activeName;

        public JdProfile Active
        {
            get
            {
                JdProfile profile = _profiles.FirstOrDefault(p => p.Name == _activeName);
                if (profile == null)
                {
                    profile = _profiles.FirstOrDefault(p => p.Role == ProfileRole.Custom) ??
                              _profiles.FirstOrDefault();
                    if (profile != null) _activeName = profile.Name;
                }

                return profile;
            }
        }

        public JdProfile Find(string name)
        {
            return _profiles.FirstOrDefault(p => p.Name == name);
        }

        public JdProfile GetByRole(ProfileRole role)
        {
            return _profiles.FirstOrDefault(p => p.Role == role);
        }

        public void SetActive(string name)
        {
            if (!_profiles.Any(p => p.Name == name)) return;
            _activeName = name;
            Save();
            PluginConfig.Instance.active_profile = name;
            PluginConfig.Instance.Changed();
            Changed?.Invoke();
        }

        public JdProfile Upsert(JdProfile profile, bool activate)
        {
            if (profile == null) return null;
            profile.Role ??= ProfileRole.PerPlayer;
            profile.EnsureValid();
            profile.Name = profile.Role == ProfileRole.PerPlayer
                ? DefaultProfiles.PerPlayerName
                : DefaultProfiles.DisplayName(profile.Role.Value);

            JdProfile existing = GetByRole(profile.Role.Value);
            if (existing != null) _profiles.Remove(existing);
            _profiles.Add(profile);
            SortProfiles();

            if (activate)
            {
                _activeName = profile.Name;
                PluginConfig.Instance.active_profile = profile.Name;
                PluginConfig.Instance.Changed();
            }

            Save();
            Changed?.Invoke();
            return profile;
        }

        public JdProfile Add(JdProfile profile, bool activate)
        {
            profile.Role ??= ProfileRole.PerPlayer;
            return Upsert(profile, activate);
        }

        public bool Delete(string name)
        {
            if (_profiles.Count <= 1) return false;
            int index = _profiles.FindIndex(p => p.Name == name);
            if (index < 0) return false;
            _profiles.RemoveAt(index);
            if (_activeName == name)
            {
                _activeName = _profiles[0].Name;
                PluginConfig.Instance.active_profile = _activeName;
                PluginConfig.Instance.Changed();
            }

            Save();
            Changed?.Invoke();
            return true;
        }

        public void Save()
        {
            var model = new FileModel { ActiveProfile = _activeName, Profiles = _profiles };
            string json = JsonConvert.SerializeObject(model, Formatting.Indented);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        private void Load()
        {
            var loaded = new List<JdProfile>();
            string requested = DefaultProfiles.MigrateName(PluginConfig.Instance?.active_profile);
            string legacyCloneOriginalName = null;

            try
            {
                if (File.Exists(_path))
                {
                    var model = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(_path));
                    if (model?.Profiles != null)
                    {
                        loaded = model.Profiles.Where(p => p != null).ToList();
                        requested = DefaultProfiles.MigrateName(model.ActiveProfile) ?? requested;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not read profiles.json; seeding defaults: " + ex.Message);
            }

            var byRole = new Dictionary<ProfileRole, JdProfile>();
            var legacyClones = new List<JdProfile>();

            foreach (JdProfile profile in loaded)
            {
                profile.EnsureValid();
                if (profile.Role.HasValue)
                {
                    ProfileRole role = profile.Role.Value;
                    if (!byRole.ContainsKey(role))
                    {
                        profile.Name = DefaultProfiles.DisplayName(role);
                        ApplyBuiltInDefaults(profile);
                        byRole[role] = profile;
                    }
                    continue;
                }

                string migrated = DefaultProfiles.MigrateName(profile.Name);
                ProfileRole? roleFromName = DefaultProfiles.RoleForName(migrated);
                if (roleFromName.HasValue)
                {
                    ProfileRole role = roleFromName.Value;
                    if (!byRole.ContainsKey(role))
                    {
                        profile.Role = role;
                        profile.Name = DefaultProfiles.DisplayName(role);
                        ApplyBuiltInDefaults(profile);
                        byRole[role] = profile;
                    }
                }
                else
                {
                    legacyClones.Add(profile);
                }
            }

            if (!byRole.ContainsKey(ProfileRole.PerPlayer) && legacyClones.Count > 0)
            {
                JdProfile mostRecent = legacyClones
                    .OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal)
                    .First();
                legacyCloneOriginalName = mostRecent.Name;
                mostRecent.Role = ProfileRole.PerPlayer;
                mostRecent.Name = DefaultProfiles.PerPlayerName;
                if (string.IsNullOrEmpty(mostRecent.PlayerName))
                {
                    mostRecent.PlayerName = legacyCloneOriginalName;
                }

                byRole[ProfileRole.PerPlayer] = mostRecent;
            }

            foreach (ProfileRole role in Enum.GetValues(typeof(ProfileRole)))
            {
                if (!byRole.ContainsKey(role))
                {
                    byRole[role] = DefaultProfiles.CreateRole(role);
                }
            }

            _profiles.Clear();
            _profiles.AddRange(byRole.Values);
            SortProfiles();

            if (!string.IsNullOrEmpty(requested) && _profiles.Any(p => p.Name == requested))
            {
                _activeName = requested;
            }
            else if (!string.IsNullOrEmpty(legacyCloneOriginalName) &&
                     string.Equals(requested, legacyCloneOriginalName, StringComparison.Ordinal))
            {
                _activeName = DefaultProfiles.PerPlayerName;
            }
            else
            {
                JdProfile custom = GetByRole(ProfileRole.Custom);
                _activeName = custom?.Name ?? DefaultProfiles.CustomName;
            }

            if (PluginConfig.Instance != null)
            {
                PluginConfig.Instance.active_profile = _activeName;
            }

            // Save is deferred out of plugin construction; the next menu/profile change persists it.
        }

        private static void ApplyBuiltInDefaults(JdProfile profile)
        {
            if (profile?.Role != ProfileRole.Default &&
                profile?.Role != ProfileRole.Competitive &&
                profile?.Role != ProfileRole.Relaxed)
            {
                return;
            }

            JdProfile canonical = DefaultProfiles.CreateRole(profile.Role.Value);
            profile.Base = canonical.Base;
            profile.Notes = canonical.Notes;
            profile.Source = canonical.Source;
        }

        private void SortProfiles()
        {
            _profiles.Sort((a, b) => (a.Role ?? ProfileRole.Custom).CompareTo(b.Role ?? ProfileRole.Custom));
        }

        private string UniqueName(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) requested = "Per-Player";
            if (_profiles.All(p => p.Name != requested)) return requested;
            for (int i = 2; ; i++)
            {
                string candidate = requested + " (" + i + ")";
                if (_profiles.All(p => p.Name != candidate)) return candidate;
            }
        }
    }
}
