using System.Collections.Generic;
using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace DynamicJD.Configuration
{
    internal class PluginConfig
    {
        internal static PluginConfig Instance { get; set; }

        public virtual bool enabled { get; set; } = true;
        public virtual bool instant_note_rotation { get; set; } = false;
        public virtual string active_profile { get; set; } = Core.DefaultProfiles.CustomName;
        public virtual float strength { get; set; } = 1f;
        public virtual float min_jump_distance { get; set; } = 2f;
        public virtual float max_jump_distance { get; set; } = 60f;

        /// <summary>0 = edit reaction time, 1 = edit jump distance.</summary>
        public virtual int config_mode { get; set; }

        public virtual string type_model { get; set; } = Core.TypeModels.ResearchName;
        public virtual float type_model_strength { get; set; } = 1f;

        public virtual bool manual_override { get; set; }
        public virtual float manual_jd { get; set; } = 20f;

        /// <summary>0 = preserve JD across song speed, 1 = preserve reaction time.</summary>
        public virtual int song_speed_mode { get; set; }

        /// <summary>When false, the player's vanilla NJS offset is ignored while the model is active.</summary>
        public virtual bool respect_vanilla_offset { get; set; } = false;

        public virtual bool njs_event_compensation { get; set; } = true;
        public virtual bool apply_in_replays { get; set; } = true;
        public virtual bool show_hud { get; set; } = false;

        public virtual bool audio_offset_analysis { get; set; } = false;
        public virtual int audio_offset_min_samples { get; set; } = 30;
        public virtual float audio_offset_max_step_ms { get; set; } = 50f;
        public virtual bool audio_offset_has_undo { get; set; }
        public virtual float audio_offset_undo_latency { get; set; }
        public virtual bool audio_offset_undo_override { get; set; }

        public virtual int clone_sample_count { get; set; } = 60;
        public virtual bool offline_mode { get; set; } = false;
        public virtual float curve_editor_min_njs { get; set; } = 8f;
        public virtual float curve_editor_max_njs { get; set; } = 32f;

        public virtual void OnLoad()
        {
            if (strength < 0f) strength = 0f;
            if (strength > 1f) strength = 1f;
            if (type_model_strength < 0f) type_model_strength = 0f;
            if (type_model_strength > 1f) type_model_strength = 1f;
            if (config_mode < 0 || config_mode > 1) config_mode = 0;
            if (string.IsNullOrWhiteSpace(type_model)) type_model = Core.TypeModels.ResearchName;
            if (manual_jd < 4f) manual_jd = 4f;
            if (manual_jd > 32f) manual_jd = 32f;
            if (min_jump_distance < 0.1f) min_jump_distance = 0.1f;
            if (max_jump_distance <= min_jump_distance) max_jump_distance = min_jump_distance + 1f;
            if (song_speed_mode < 0 || song_speed_mode > 1) song_speed_mode = 0;
            if (audio_offset_min_samples < 10) audio_offset_min_samples = 10;
            if (audio_offset_min_samples > 200) audio_offset_min_samples = 200;
            if (audio_offset_max_step_ms < 5f) audio_offset_max_step_ms = 5f;
            if (audio_offset_max_step_ms > 100f) audio_offset_max_step_ms = 100f;
            if (audio_offset_has_undo &&
                (audio_offset_undo_latency < 0f || audio_offset_undo_latency > 0.3f))
            {
                audio_offset_has_undo = false;
                audio_offset_undo_latency = 0f;
                audio_offset_undo_override = false;
            }
            if (clone_sample_count < 10) clone_sample_count = 10;
            if (clone_sample_count > 300) clone_sample_count = 300;
            if (curve_editor_min_njs < 1f) curve_editor_min_njs = 1f;
            if (curve_editor_max_njs <= curve_editor_min_njs) curve_editor_max_njs = curve_editor_min_njs + 1f;
        }

        public virtual void Changed()
        {
            OnLoad();
        }
    }
}
