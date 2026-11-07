using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DynamicJD.Api;
using DynamicJD.Configuration;
using DynamicJD.Core;
using Newtonsoft.Json;

namespace DynamicJD.Services
{
    /// <summary>Resolves BeatLeader map-type bits and caches them by hash/difficulty/mode.</summary>
    public sealed class MapTypeService
    {
        private readonly BeatLeaderClient _client;
        private readonly string _cachePath;
        private readonly ConcurrentDictionary<string, MapTypeInfo> _cache =
            new ConcurrentDictionary<string, MapTypeInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Task<MapTypeInfo>> _inflight =
            new ConcurrentDictionary<string, Task<MapTypeInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _saveLock = new object();

        public event Action<MapContext> TypeUpdated;

        public MapTypeService(string directory, BeatLeaderClient client)
        {
            _client = client;
            Directory.CreateDirectory(directory);
            _cachePath = Path.Combine(directory, "map_types.json");
            Load();
        }

        public static string CacheKey(MapContext ctx)
        {
            return (ctx?.Hash ?? string.Empty) + "|" + (ctx?.Difficulty ?? string.Empty) + "|" +
                   (ctx?.Mode ?? string.Empty);
        }

        public MapTypeInfo Cached(MapContext ctx)
        {
            if (ctx == null) return null;
            _cache.TryGetValue(CacheKey(ctx), out MapTypeInfo info);
            return info;
        }

        public MapTypeInfo ResolveCachedOrUnknown(MapContext ctx)
        {
            return Cached(ctx) ?? MapTypeInfo.Unknown(ctx?.Njs ?? 0f);
        }

        public void EnsureFetch(MapContext ctx, bool force = false)
        {
            if (ctx == null || !ctx.HasMap) return;

            string key = CacheKey(ctx);
            if (!force && _cache.TryGetValue(key, out MapTypeInfo cached))
            {
                ctx.Type = cached;
                TypeUpdated?.Invoke(ctx);
                return;
            }

            if (PluginConfig.Instance?.offline_mode == true) return;
            _inflight.GetOrAdd(key, k => FetchAsync(ctx, k, force));
        }

        private async Task<MapTypeInfo> FetchAsync(MapContext snapshot, string key, bool force)
        {
            try
            {
                MapTypeInfo info = await _client.GetMapTypeAsync(snapshot.Hash, snapshot.Difficulty, snapshot.Mode)
                    .ConfigureAwait(false);
                if (info == null) return null;
                _cache[key] = info;
                Save();
                MainThreadDispatcher.Enqueue(() =>
                {
                    MapContext selected = MapContext.Selected;
                    if (selected != null && CacheKey(selected) == key)
                    {
                        selected.Type = info;
                        TypeUpdated?.Invoke(selected);
                    }

                    MapContext current = MapContext.Current;
                    if (current != null && CacheKey(current) == key && !ReferenceEquals(current, selected))
                    {
                        current.Type = info;
                        TypeUpdated?.Invoke(current);
                    }
                });
                return info;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("BeatLeader map-type lookup failed for " + snapshot.Hash + ": " + ex.Message);
                MapTypeInfo fallback = MapTypeInfo.Unknown(snapshot.Njs);
                fallback.Bitmask = (int)MapTypeBits.Midspeed;
                fallback.Archetypes = new List<MapArchetype> { MapArchetype.Balanced };
                fallback.Source = "auto fallback";
                fallback.Confidence = 0.2f;
                MainThreadDispatcher.Enqueue(() =>
                {
                    MapContext selected = MapContext.Selected;
                    if (selected != null && CacheKey(selected) == key)
                    {
                        selected.Type = fallback;
                        TypeUpdated?.Invoke(selected);
                    }
                });
                return fallback;
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var data = JsonConvert.DeserializeObject<Dictionary<string, MapTypeInfo>>(File.ReadAllText(_cachePath));
                if (data == null) return;
                foreach (KeyValuePair<string, MapTypeInfo> pair in data)
                {
                    if (pair.Value == null) continue;
                    // Old builds cached type=0 results as unknown. Drop them so the new
                    // tag/metric fallback gets a chance to classify the map.
                    if (pair.Value.Bitmask == 0 ||
                        string.Equals(pair.Value.Source, "BeatLeader (no type)", StringComparison.OrdinalIgnoreCase) ||
                        (!pair.Value.IsAuthoritative &&
                         pair.Value.ClassifierVersion < MapTypeInfo.CurrentClassifierVersion))
                    {
                        continue;
                    }
                    pair.Value.EnsureValid();
                    _cache[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not read map_types.json: " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                lock (_saveLock)
                {
                    var snapshot = new Dictionary<string, MapTypeInfo>(_cache);
                    File.WriteAllText(_cachePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("Could not write map_types.json: " + ex.Message);
            }
        }
    }
}
