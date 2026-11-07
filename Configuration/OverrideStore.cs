using System;
using System.Collections.Generic;
using System.IO;
using DynamicJD.Core;
using Newtonsoft.Json;

namespace DynamicJD.Configuration
{
    public sealed class OverrideStore
    {
        private readonly string _path;
        private readonly Dictionary<string, MapOverride> _overrides =
            new Dictionary<string, MapOverride>(StringComparer.OrdinalIgnoreCase);

        public OverrideStore(string directory)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "overrides.json");
            Load();
        }

        public MapOverride Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            _overrides.TryGetValue(key, out MapOverride value);
            return value;
        }

        public void Set(string key, MapOverride value)
        {
            if (string.IsNullOrEmpty(key) || value == null) return;
            value.UpdatedAt = DateTime.UtcNow.ToString("o");
            _overrides[key] = value;
            Save();
        }

        public void Remove(string key)
        {
            if (_overrides.Remove(key)) Save();
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(_overrides, Formatting.Indented);
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
                var data = JsonConvert.DeserializeObject<Dictionary<string, MapOverride>>(File.ReadAllText(_path));
                if (data == null) return;
                foreach (KeyValuePair<string, MapOverride> pair in data)
                {
                    if (pair.Value != null) _overrides[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not read overrides.json: " + ex.Message);
            }
        }
    }
}
