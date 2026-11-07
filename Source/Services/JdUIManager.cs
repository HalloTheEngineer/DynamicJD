using System;
using DynamicJD.Core;
using Zenject;

namespace DynamicJD.Services
{
    /// <summary>Tracks the map selected in the standard level detail view.</summary>
    public sealed class JdUIManager : IInitializable, IDisposable
    {
        private readonly StandardLevelDetailViewController _levelDetail;
        private readonly PlayerDataModel _playerDataModel;

        public event Action<MapContext> MapChanged;

        [Inject]
        public JdUIManager(StandardLevelDetailViewController levelDetail, PlayerDataModel playerDataModel)
        {
            _levelDetail = levelDetail;
            _playerDataModel = playerDataModel;
        }

        public void Initialize()
        {
            _levelDetail.didChangeDifficultyBeatmapEvent += OnChanged;
            _levelDetail.didChangeContentEvent += OnContentChanged;
            Plugin.MapTypes.TypeUpdated += OnTypeUpdated;
            RefreshSelected();
        }

        public void Dispose()
        {
            _levelDetail.didChangeDifficultyBeatmapEvent -= OnChanged;
            _levelDetail.didChangeContentEvent -= OnContentChanged;
            Plugin.MapTypes.TypeUpdated -= OnTypeUpdated;
        }

        public void RefreshSelected()
        {
            BeatmapLevel level = _levelDetail.beatmapLevel;
            BeatmapKey key = _levelDetail.beatmapKey;
            if (level == null || !key.IsValid())
            {
                MapContext.Selected = null;
                MapChanged?.Invoke(null);
                return;
            }

            float playerOffset = 0f;
            try
            {
                playerOffset = _playerDataModel.playerData.playerSpecificSettings.noteJumpStartBeatOffset;
            }
            catch
            {
                // Player data is not always available during menu transitions.
            }

            MapContext ctx = MapContext.FromBeatmap(key, level, playerOffset, null, null);
            MapContext cachedTypeSource = MapContext.Selected;
            if (cachedTypeSource != null &&
                string.Equals(cachedTypeSource.Hash, ctx.Hash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cachedTypeSource.Difficulty, ctx.Difficulty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cachedTypeSource.Mode, ctx.Mode, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Type = cachedTypeSource.Type;
            }

            MapContext.Selected = ctx;
            MapTypeInfo cached = Plugin.MapTypes.Cached(ctx);
            if (cached != null)
            {
                ctx.Type = cached;
            }
            else
            {
                Plugin.MapTypes.EnsureFetch(ctx);
            }

            MapChanged?.Invoke(ctx);
        }

        private void OnChanged(StandardLevelDetailViewController controller) => RefreshSelected();

        private void OnContentChanged(StandardLevelDetailViewController controller,
            StandardLevelDetailViewController.ContentType contentType)
        {
            if (contentType == StandardLevelDetailViewController.ContentType.OwnedAndReady)
            {
                RefreshSelected();
            }
        }

        private void OnTypeUpdated(MapContext ctx)
        {
            MapContext selected = MapContext.Selected;
            if (selected != null &&
                string.Equals(selected.Hash, ctx.Hash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Difficulty, ctx.Difficulty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Mode, ctx.Mode, StringComparison.OrdinalIgnoreCase))
            {
                selected.Type = ctx.Type;
                MapChanged?.Invoke(selected);
            }
        }
    }
}
