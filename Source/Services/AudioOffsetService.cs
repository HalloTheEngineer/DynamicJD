using System;
using System.Threading.Tasks;
using DynamicJD.Configuration;
using DynamicJD.Core;
using UnityEngine;
using Zenject;

namespace DynamicJD.Services
{
    /// <summary>Owns the confirmed write to Beat Saber's audio latency and its undo snapshot.</summary>
    internal sealed class AudioOffsetService
    {
        private readonly SettingsManager _settings;
        private readonly IFileStorage _fileStorage;

        [Inject]
        public AudioOffsetService(SettingsManager settings, IFileStorage fileStorage)
        {
            _settings = settings;
            _fileStorage = fileStorage;
        }

        public float CurrentLatencyMs
        {
            get
            {
                if (_settings == null) return 0f;
                return _settings.settings.audio.latency * 1000f;
            }
        }

        public bool HasUndo
        {
            get
            {
                PluginConfig config = PluginConfig.Instance;
                return config != null && config.audio_offset_has_undo;
            }
        }

        public bool Apply(AudioOffsetResult result, out string status)
        {
            status = string.Empty;
            if (_settings == null)
            {
                status = "Game settings are not available.";
                return false;
            }

            if (result == null || !result.HasSuggestion)
            {
                status = "There is no pending recommendation.";
                return false;
            }

            float current = _settings.settings.audio.latency;
            float measured = result.LatencyAtStartMs / 1000f;
            if (Math.Abs(current - measured) > 0.002f)
            {
                status = "Game latency changed since this run; play another map to remeasure.";
                return false;
            }

            float target = Mathf.Clamp(current + result.SuggestedDeltaMs / 1000f, 0f, 0.3f);
            if (Math.Abs(target - current) < 0.0005f)
            {
                status = "The recommended value is already applied.";
                return false;
            }

            PluginConfig config = PluginConfig.Instance;
            if (config != null && !config.audio_offset_has_undo)
            {
                config.audio_offset_undo_latency = current;
                config.audio_offset_undo_override = _settings.settings.audio.overrideLatency;
                config.audio_offset_has_undo = true;
            }

            _settings.settings.audio.latency = target;
            _settings.settings.audio.overrideLatency = true;
            config?.Changed();

            SaveSettings();
            AudioOffsetTracker.MarkApplied((target - current) * 1000f);
            status = "Applied " + FormatMs((target - current) * 1000f) + " · now " +
                     FormatMs(target * 1000f) + ".";
            return true;
        }

        public bool Undo(out string status)
        {
            status = string.Empty;
            PluginConfig config = PluginConfig.Instance;
            if (_settings == null || config == null || !config.audio_offset_has_undo)
            {
                status = "There is no applied offset to undo.";
                return false;
            }

            _settings.settings.audio.latency = config.audio_offset_undo_latency;
            _settings.settings.audio.overrideLatency = config.audio_offset_undo_override;

            config.audio_offset_has_undo = false;
            config.audio_offset_undo_latency = 0f;
            config.audio_offset_undo_override = false;
            config.Changed();

            SaveSettings();
            AudioOffsetTracker.MarkUndone();
            status = "Restored " + FormatMs(_settings.settings.audio.latency * 1000f) + ".";
            return true;
        }

        private void SaveSettings()
        {
            if (_fileStorage == null)
            {
                Plugin.Log?.Warn("DynamicJD audio offset: file storage unavailable; settings may not persist.");
                return;
            }

            try
            {
                Task task = SettingsIO.SaveAsync(_fileStorage, _settings.settings);
                task.ContinueWith(t =>
                {
                    Plugin.Log?.Error("DynamicJD audio offset: failed to save settings: " +
                                      t.Exception?.GetBaseException().Message);
                }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("DynamicJD audio offset: failed to save settings: " + ex);
            }
        }

        private static string FormatMs(float value)
        {
            return (value >= 0f ? "+" : string.Empty) + value.ToString("0") + " ms";
        }
    }
}
