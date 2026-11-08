using System;
using System.ComponentModel;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using DynamicJD.Configuration;
using DynamicJD.Core;
using DynamicJD.Services;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace DynamicJD.UI.Audio
{
    internal sealed class AudioMenuTab : IInitializable, IDisposable, INotifyPropertyChanged
    {
        public const string TabName = "DynamicJD - Audio";
        private const string Resource = "DynamicJD.UI.Audio.audioMenu.bsml";
        private const int GraphWidth = 512;
        private const int GraphHeight = 180;
        private const int PlotPadding = 8;

        private readonly AudioOffsetService _service;
        private Texture2D _graphTexture;
        private Color32[] _graphPixels;
        private string _actionStatus = string.Empty;

        [Inject]
        public AudioMenuTab(AudioOffsetService service)
        {
            _service = service;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [UIComponent("audio_offset_graph")]
        public RawImage AudioOffsetGraph { get; set; }

        public void Initialize()
        {
            AudioOffsetTracker.Changed += OnAudioDataChanged;
            GameplaySetup.Instance?.AddTab(TabName, Resource, this,
                MenuType.Solo | MenuType.Campaign | MenuType.Online);
        }

        public void Dispose()
        {
            AudioOffsetTracker.Changed -= OnAudioDataChanged;
            GameplaySetup.Instance?.RemoveTab(TabName);
            if (_graphTexture != null)
            {
                UnityEngine.Object.Destroy(_graphTexture);
                _graphTexture = null;
                _graphPixels = null;
            }
        }

        [UIAction("#post-parse")]
        public void PostParse()
        {
            Texture2D texture = EnsureTexture();
            if (AudioOffsetGraph != null)
            {
                AudioOffsetGraph.raycastTarget = false;
                AudioOffsetGraph.texture = texture;
            }
            RenderGraph();
        }

        private void OnAudioDataChanged()
        {
            _actionStatus = string.Empty;
            RaiseAll();
            RenderGraph();
        }

        // ------------------------------------------------------------------ analysis

        [UIValue("analysis_enabled")]
        public bool AnalysisEnabled
        {
            get
            {
                PluginConfig config = PluginConfig.Instance;
                return config != null && config.audio_offset_analysis;
            }
            set
            {
                PluginConfig config = PluginConfig.Instance;
                if (config == null || config.audio_offset_analysis == value) return;
                config.audio_offset_analysis = value;
                config.Changed();
                if (!value)
                {
                    AudioOffsetTracker.Clear();
                    _actionStatus = "Analysis disabled.";
                }
                else
                {
                    _actionStatus = "Analysis enabled. Play a normal 1.0x song next.";
                }
                RaiseAll();
                RenderGraph();
            }
        }

        [UIAction("set_analysis_enabled")]
        public void SetAnalysisEnabled(bool value) => AnalysisEnabled = value;

        [UIValue("current_offset")]
        public string CurrentOffset => CurrentLatencyMs.ToString("0") + " ms";

        [UIValue("has_suggestion")]
        public bool HasSuggestion => AudioOffsetTracker.Pending != null;

        [UIValue("has_undo")]
        public bool HasUndo => _service != null && _service.HasUndo;

        [UIValue("has_graph")]
        public bool HasGraph
        {
            get
            {
                AudioOffsetResult result = AudioOffsetTracker.LastResult;
                return result != null && result.Samples != null && result.Samples.Count > 0;
            }
        }

        [UIValue("has_actions")]
        public bool HasActions => HasSuggestion || HasUndo || HasGraph;

        // ------------------------------------------------------------------ recommendation

        [UIValue("recommendation_title")]
        public string RecommendationTitle
        {
            get
            {
                AudioOffsetResult pending = AudioOffsetTracker.Pending;
                if (pending != null)
                {
                    return "Based on the latest <color=#66ccff><b>" + pending.AcceptedSamples +
                           "</b></color> notes, your recommended audio offset is:";
                }

                return "No confident recommendation yet.";
            }
        }

        [UIValue("recommendation_value")]
        public string RecommendationValue
        {
            get
            {
                AudioOffsetResult pending = AudioOffsetTracker.Pending;
                if (pending == null) return "<color=#8c8c8c>—</color>";

                float current = CurrentLatencyMs;
                float target = Mathf.Clamp(current + pending.SuggestedDeltaMs, 0f, 300f);
                return "<color=#66ccff><b>" + FormatDelta(pending.SuggestedDeltaMs) +
                       "</b></color>   <color=#a0a0a0>" + current.ToString("0") +
                       " ms → " + target.ToString("0") + " ms</color>";
            }
        }

        [UIValue("recommendation_detail")]
        public string RecommendationDetail
        {
            get
            {
                AudioOffsetResult pending = AudioOffsetTracker.Pending;
                if (pending != null)
                {
                    return pending.AcceptedSamples + "/" + pending.TotalSamples +
                           " cuts · 95% CI ±" +
                           pending.ConfidenceHalfWidthMs.ToString("0.0") + " ms";
                }

                AudioOffsetResult last = AudioOffsetTracker.LastResult;
                if (last != null) return last.Status;
                return "Play a 1.0x map; need 30 clean cuts.";
            }
        }

        [UIValue("graph_caption")]
        public string GraphCaption
        {
            get
            {
                AudioOffsetResult result = AudioOffsetTracker.LastResult;
                if (result == null || result.Samples == null || result.Samples.Count == 0)
                {
                    return "No data.";
                }

                return "Top = early · ±" + GraphScaleMs(result).ToString("0") +
                       " ms · blue used · orange outlier · green mean";
            }
        }

        [UIValue("status_text")]
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(_actionStatus)) return _actionStatus;
                PluginConfig config = PluginConfig.Instance;
                if (config == null || !config.audio_offset_analysis)
                {
                    return "Analysis off.";
                }
                return AudioOffsetTracker.LastRunNote;
            }
        }

        // ------------------------------------------------------------------ actions

        [UIAction("apply_suggestion")]
        public void ApplySuggestion()
        {
            AudioOffsetResult pending = AudioOffsetTracker.Pending;
            if (pending == null)
            {
                _actionStatus = "There is no pending recommendation.";
                RaiseAll();
                return;
            }

            if (_service == null)
            {
                _actionStatus = "Audio settings service is not available.";
                RaiseAll();
                return;
            }

            if (!_service.Apply(pending, out string status))
            {
                _actionStatus = status;
                RaiseAll();
                return;
            }

            _actionStatus = status;
            RaiseAll();
            RenderGraph();
        }

        [UIAction("undo_offset")]
        public void UndoOffset()
        {
            if (_service == null)
            {
                _actionStatus = "Audio settings service is not available.";
                RaiseAll();
                return;
            }

            if (!_service.Undo(out string status))
            {
                _actionStatus = status;
                RaiseAll();
                return;
            }

            _actionStatus = status;
            RaiseAll();
            RenderGraph();
        }

        [UIAction("clear_results")]
        public void ClearResults()
        {
            AudioOffsetTracker.Clear();
            _actionStatus = "Cleared calibration data.";
            RaiseAll();
            RenderGraph();
        }

        // ------------------------------------------------------------------ graph

        private Texture2D EnsureTexture()
        {
            if (_graphTexture != null) return _graphTexture;

            _graphTexture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false)
            {
                name = "DynamicJDAudioOffsetGraph",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _graphPixels = new Color32[GraphWidth * GraphHeight];
            return _graphTexture;
        }

        private void RenderGraph()
        {
            Texture2D texture = EnsureTexture();
            if (texture == null || _graphPixels == null) return;

            if (AudioOffsetGraph != null) AudioOffsetGraph.texture = texture;

            Color32 background = new Color32(18, 18, 25, 255);
            Color32 grid = new Color32(42, 42, 56, 255);
            Color32 zero = new Color32(105, 105, 125, 255);
            Color32 accepted = new Color32(78, 205, 255, 255);
            Color32 rejected = new Color32(255, 166, 66, 255);
            Color32 mean = new Color32(83, 230, 158, 255);

            Fill(background);

            int left = PlotPadding;
            int right = GraphWidth - PlotPadding;
            int top = GraphHeight - PlotPadding;
            int bottom = PlotPadding;
            int center = (top + bottom) / 2;

            DrawRectangle(left, bottom, right, top, grid);
            DrawHorizontal(left, right, center, zero);

            AudioOffsetResult result = AudioOffsetTracker.LastResult;
            if (result == null || result.Samples == null || result.Samples.Count == 0)
            {
                texture.SetPixels32(_graphPixels);
                texture.Apply(false, false);
                return;
            }

            float scale = GraphScaleMs(result);
            float halfHeight = (top - bottom) * 0.5f;
            float firstTime = result.Samples[0]?.NoteTime ?? 0f;
            float lastTime = firstTime;
            for (int i = 1; i < result.Samples.Count; i++)
            {
                AudioOffsetSample sample = result.Samples[i];
                if (sample != null && sample.NoteTime > lastTime) lastTime = sample.NoteTime;
            }

            float timeSpan = lastTime - firstTime;
            for (int i = 0; i < result.Samples.Count; i++)
            {
                AudioOffsetSample sample = result.Samples[i];
                if (sample == null) continue;

                float xNorm = timeSpan > 0.01f
                    ? (sample.NoteTime - firstTime) / timeSpan
                    : (result.Samples.Count > 1 ? i / (float)(result.Samples.Count - 1) : 0.5f);
                float yNorm = Mathf.Clamp(sample.DeviationMs / scale, -1f, 1f);

                int x = Mathf.RoundToInt(left + xNorm * (right - left));
                int y = Mathf.RoundToInt(center + yNorm * halfHeight);
                DrawPoint(x, y, sample.Accepted ? accepted : rejected);
            }

            if (result.AcceptedSamples > 0 && Math.Abs(result.MeanDeviationMs) > 0.01f)
            {
                float meanY = Mathf.Clamp(result.MeanDeviationMs / scale, -1f, 1f);
                int y = Mathf.RoundToInt(center + meanY * halfHeight);
                DrawHorizontal(left, right, y, mean);
            }

            texture.SetPixels32(_graphPixels);
            texture.Apply(false, false);
        }

        private static float GraphScaleMs(AudioOffsetResult result)
        {
            float max = 20f;
            bool hasAccepted = false;
            for (int i = 0; i < result.Samples.Count; i++)
            {
                AudioOffsetSample sample = result.Samples[i];
                if (sample == null || !sample.Accepted) continue;
                hasAccepted = true;
                max = Mathf.Max(max, Math.Abs(sample.DeviationMs));
            }

            if (!hasAccepted)
            {
                for (int i = 0; i < result.Samples.Count; i++)
                {
                    AudioOffsetSample sample = result.Samples[i];
                    if (sample == null) continue;
                    max = Mathf.Max(max, Math.Abs(sample.DeviationMs));
                }
            }

            return Mathf.Clamp(Mathf.Ceil(max / 10f) * 10f, 20f, 300f);
        }

        private void Fill(Color32 color)
        {
            for (int i = 0; i < _graphPixels.Length; i++) _graphPixels[i] = color;
        }

        private void DrawRectangle(int left, int bottom, int right, int top, Color32 color)
        {
            DrawHorizontal(left, right, bottom, color);
            DrawHorizontal(left, right, top, color);
            DrawVertical(bottom, top, left, color);
            DrawVertical(bottom, top, right, color);
        }

        private void DrawHorizontal(int left, int right, int y, Color32 color)
        {
            if (y < 0 || y >= GraphHeight) return;
            for (int x = Mathf.Max(0, left); x <= Mathf.Min(GraphWidth - 1, right); x++)
            {
                _graphPixels[y * GraphWidth + x] = color;
            }
        }

        private void DrawVertical(int bottom, int top, int x, Color32 color)
        {
            if (x < 0 || x >= GraphWidth) return;
            for (int y = Mathf.Max(0, bottom); y <= Mathf.Min(GraphHeight - 1, top); y++)
            {
                _graphPixels[y * GraphWidth + x] = color;
            }
        }

        private void DrawPoint(int centerX, int centerY, Color32 color)
        {
            for (int y = -1; y <= 1; y++)
            {
                int py = centerY + y;
                if (py < 0 || py >= GraphHeight) continue;
                for (int x = -1; x <= 1; x++)
                {
                    int px = centerX + x;
                    if (px < 0 || px >= GraphWidth) continue;
                    _graphPixels[py * GraphWidth + px] = color;
                }
            }
        }

        private float CurrentLatencyMs => _service != null ? _service.CurrentLatencyMs : 0f;

        private static string FormatDelta(float value)
        {
            return (value >= 0f ? "+" : string.Empty) + value.ToString("0") + " ms";
        }

        private void RaiseAll()
        {
            foreach (string name in new[]
            {
                "AnalysisEnabled", "CurrentOffset", "HasSuggestion", "HasUndo", "HasGraph", "HasActions",
                "RecommendationTitle", "RecommendationValue", "RecommendationDetail",
                "GraphCaption", "StatusText",
            })
            {
                Raise(name);
            }
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null) return;
            try
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Debug("DynamicJD audio UI update skipped for " + name + ": " + ex.Message);
            }
        }
    }
}
