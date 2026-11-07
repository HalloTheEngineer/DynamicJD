using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DynamicJD.Core;
using Newtonsoft.Json.Linq;

namespace DynamicJD.Api
{
    public sealed class PlayerScore
    {
        public long ScoreId { get; set; }
        public string PlayerId { get; set; }
        public string ReplayUrl { get; set; }
        public string Modifiers { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public string Hash { get; set; }
        public string SongName { get; set; }
        public float Bpm { get; set; }
        public string Difficulty { get; set; }
        public string Mode { get; set; }
        public float Njs { get; set; }
        public float MapOffset { get; set; }
        public int TypeMask { get; set; }
        public float Stars { get; set; }
        public float Nps { get; set; }
        public int MissedNotes { get; set; }
        public int BadCuts { get; set; }
        public bool FullCombo { get; set; }
    }

    public sealed class PlayerSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float Pp { get; set; }
        public int Rank { get; set; }
        public float AverageAccuracy { get; set; }
    }

    /// <summary>Public BeatLeader API client. Only GETs, no authentication required.</summary>
    public sealed class BeatLeaderClient
    {
        public const string BaseUrl = "https://api.beatleader.xyz";
        private const int MaxScoresPerPage = 100;
        private const int MaxScoresTotal = 300;

        private static readonly HttpClient Http = CreateClient();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(3, 3);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicJD/0.1 (+BeatSaber mod)");
            return client;
        }

        public async Task<MapTypeInfo> GetMapTypeAsync(string hash, string difficulty, string mode,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            string url = BaseUrl + "/leaderboards/hash/" + Uri.EscapeDataString(hash.ToUpperInvariant());
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string json = await GetStringCheckedAsync(url, token).ConfigureAwait(false);
                return ParseMapType(json, hash, difficulty, mode);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<PlayerScore>> GetPlayerScoresAsync(string playerId, int count,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return new List<PlayerScore>();

            int requested = Math.Max(1, Math.Min(MaxScoresTotal, count));
            int pageCount = Math.Min(MaxScoresPerPage, requested);
            var scores = new List<PlayerScore>();
            var seen = new HashSet<long>();
            int page = 1;

            while (scores.Count < requested)
            {
                string url = BaseUrl + "/player/" + Uri.EscapeDataString(playerId) +
                             "/scores?page=" + page + "&count=" + pageCount +
                             "&sortBy=top&order=desc";

                string json;
                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    json = await GetStringCheckedAsync(url, token).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }

                List<PlayerScore> pageScores = ParseScores(json);
                int added = 0;
                foreach (PlayerScore score in pageScores)
                {
                    if (score.ScoreId != 0 && !seen.Add(score.ScoreId)) continue;
                    scores.Add(score);
                    added++;
                }

                if (added == 0 || pageScores.Count < pageCount) break;
                page++;
            }

            if (scores.Count > requested) scores.RemoveRange(requested, scores.Count - requested);
            return scores;
        }

        public async Task<PlayerSummary> GetPlayerAsync(string playerId, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return null;
            string url = BaseUrl + "/player/" + Uri.EscapeDataString(playerId.Trim());
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string json = await GetStringCheckedAsync(url, token).ConfigureAwait(false);
                JObject root = JObject.Parse(json);
                return new PlayerSummary
                {
                    Id = (string)root["id"] ?? playerId,
                    Name = (string)root["name"] ?? playerId,
                    Pp = (float?)root["pp"] ?? 0f,
                    Rank = (int?)root["rank"] ?? 0,
                    AverageAccuracy = (float?)root["scoreStats"]?["averageAccuracy"] ?? 0f,
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<PlayerSearchResult>> SearchPlayersAsync(string query, int limit,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<PlayerSearchResult>();
            string url = BaseUrl + "/v2/players?search=" + Uri.EscapeDataString(query.Trim()) +
                         "&page=1&count=" + Math.Max(1, Math.Min(50, limit)) + "&sortBy=pp&mapsType=all";
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string json = await GetStringCheckedAsync(url, token).ConfigureAwait(false);
                return ParsePlayerSearch(json);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<BsorHeader> GetReplayHeaderAsync(string replayUrl, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(replayUrl)) return null;
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, replayUrl);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 8191);
                using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        byte[] buffer = await ReadAtMostAsync(stream, 16384, token).ConfigureAwait(false);
                        return BsorHeaderReader.Read(buffer);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }


        private static async Task<string> GetStringCheckedAsync(string url, CancellationToken token)
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string detail = (body ?? string.Empty).Trim();
                    if (detail.Length > 240) detail = detail.Substring(0, 240);
                    string message = "BeatLeader " + (int)response.StatusCode + " " + response.ReasonPhrase +
                                     (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
                    Plugin.Log?.Warn(message + " [" + url + "]");
                    throw new HttpRequestException(message);
                }

                return body;
            }
        }

        private static async Task<byte[]> ReadAtMostAsync(Stream stream, int limit, CancellationToken token)
        {
            var buffer = new byte[limit];
            int total = 0;
            while (total < limit)
            {
                int read = await stream.ReadAsync(buffer, total, limit - total, token).ConfigureAwait(false);
                if (read <= 0) break;
                total += read;
            }

            if (total == limit) return buffer;
            Array.Resize(ref buffer, total);
            return buffer;
        }

        private static MapTypeInfo ParseMapType(string json, string hash, string difficulty, string mode)
        {
            JObject root = JObject.Parse(json);
            JArray boards = root["leaderboards"] as JArray;
            if (boards == null || boards.Count == 0) return MapTypeInfo.Unknown();

            JObject selected = null;
            foreach (JToken token in boards)
            {
                JObject diff = token["difficulty"] as JObject;
                if (diff == null) continue;
                string diffName = (string)diff["difficultyName"] ?? string.Empty;
                string modeName = (string)diff["modeName"] ?? string.Empty;
                if (string.Equals(diffName, difficulty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(modeName, mode, StringComparison.OrdinalIgnoreCase))
                {
                    selected = diff;
                    break;
                }
            }

            if (selected == null && string.IsNullOrEmpty(mode))
            {
                selected = boards.Select(b => b["difficulty"] as JObject)
                    .FirstOrDefault(d => d != null && string.Equals((string)d["difficultyName"], difficulty,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null) return MapTypeInfo.Unknown();

            return MapTypeInfo.FromBeatLeader(
                ReadTypeMask(selected["type"]),
                (float?)selected["njs"] ?? 0f,
                (float?)selected["noteJumpStartBeatOffset"] ?? 0f,
                (float?)selected["stars"] ?? 0f,
                (float?)selected["nps"] ?? 0f,
                hash,
                difficulty,
                mode,
                (int?)selected["speedTags"] ?? 0,
                (int?)selected["styleTags"] ?? 0,
                (int?)selected["featureTags"] ?? 0,
                (float?)selected["techRating"] ?? 0f,
                (float?)selected["accRating"] ?? 0f,
                (float?)selected["peakSustainedEBPM"] ?? 0f,
                (float?)selected["linearPercentage"],
                (int?)selected["bombs"] ?? 0,
                (int?)selected["notes"] ?? 0);
        }


        private static int ReadTypeMask(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type == JTokenType.Integer) return (int)token;
            if (token.Type != JTokenType.String) return 0;

            int mask = 0;
            foreach (string part in ((string)token).Split(new[] { ',', '|', ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "acc":
                    case "accuracy": mask |= 1; break;
                    case "tech": mask |= 2; break;
                    case "midspeed": mask |= 4; break;
                    case "speed": mask |= 8; break;
                    case "fitbeat": mask |= 16; break;
                    case "linear": mask |= 32; break;
                    case "bombreset": mask |= 64; break;
                }
            }

            return mask;
        }


        private static List<PlayerSearchResult> ParsePlayerSearch(string json)
        {
            var results = new List<PlayerSearchResult>();
            JObject root = JObject.Parse(json);
            JArray data = root["data"] as JArray;
            if (data == null) return results;
            foreach (JToken item in data)
            {
                string id = (string)item["id"];
                if (string.IsNullOrEmpty(id)) continue;
                results.Add(new PlayerSearchResult
                {
                    Id = id,
                    Name = (string)item["name"] ?? id,
                    Country = (string)item["country"] ?? string.Empty,
                    Pp = (float?)item["pp"] ?? 0f,
                    Rank = (int?)item["rank"] ?? 0,
                });
            }
            return results;
        }

        private static List<PlayerScore> ParseScores(string json)
        {
            var result = new List<PlayerScore>();
            JObject root = JObject.Parse(json);
            JArray data = root["data"] as JArray;
            if (data == null) return result;

            foreach (JToken item in data)
            {
                JObject leaderboard = item["leaderboard"] as JObject;
                JObject diff = leaderboard?["difficulty"] as JObject;
                JObject song = leaderboard?["song"] as JObject;
                if (diff == null || song == null) continue;

                result.Add(new PlayerScore
                {
                    ScoreId = (long?)item["id"] ?? 0,
                    PlayerId = (string)item["playerId"],
                    ReplayUrl = (string)item["replay"],
                    Modifiers = (string)item["modifiers"],
                    Accuracy = (float?)item["accuracy"] ?? 0f,
                    Pp = (float?)item["pp"] ?? 0f,
                    Hash = (string)song["hash"],
                    SongName = (string)song["name"],
                    Bpm = (float?)song["bpm"] ?? 0f,
                    Difficulty = (string)diff["difficultyName"],
                    Mode = (string)diff["modeName"],
                    Njs = (float?)diff["njs"] ?? 0f,
                    MapOffset = (float?)diff["noteJumpStartBeatOffset"] ?? 0f,
                    TypeMask = ReadTypeMask(diff["type"]),
                    Stars = (float?)diff["stars"] ?? 0f,
                    Nps = (float?)diff["nps"] ?? 0f,
                    MissedNotes = (int?)item["missedNotes"] ?? 0,
                    BadCuts = (int?)item["badCuts"] ?? 0,
                    FullCombo = (bool?)item["fullCombo"] ?? false,
                });
            }

            return result;
        }
    }
}
