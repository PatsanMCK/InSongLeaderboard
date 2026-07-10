using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace InSongLeaderboard
{
    internal static class LeaderboardApiClient
    {
        private const string ScoreSaberBaseUrl = "https://scoresaber.com";
        private const string BeatLeaderBaseUrl = "https://api.beatleader.xyz";

        public static IEnumerator FetchScores(
            LeaderboardSource source,
            LeaderboardRequest request,
            Action<List<LeaderboardInfo>> onSuccess,
            Action<string> onFailure)
        {
            if (source == LeaderboardSource.BeatLeader)
                yield return FetchBeatLeaderScores(request, onSuccess, onFailure);
            else
                yield return FetchScoreSaberScores(request, onSuccess, onFailure);
        }

        public static IEnumerator FetchPlayerAvatar(
            LeaderboardSource source,
            string playerId,
            Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                onComplete(string.Empty);
                yield break;
            }

            var escapedPlayerId = Uri.EscapeDataString(playerId);
            var url = source == LeaderboardSource.BeatLeader
                ? BeatLeaderBaseUrl + "/player/" + escapedPlayerId
                : ScoreSaberBaseUrl + "/api/v2/players/" + escapedPlayerId + "/basic";
            Plugin.log.Debug(string.Format(
                "Player avatar request prepared: source={0}, playerId={1}",
                source,
                playerId));

            string json = null;
            yield return DownloadJson(url, value => json = value, error => Plugin.log.Warn(error));

            if (string.IsNullOrEmpty(json))
            {
                onComplete(string.Empty);
                yield break;
            }

            try
            {
                var root = JToken.Parse(json);
                var player = root["player"] ?? root["data"] ?? root;
                var avatar = ReadString(player, "avatar", "profilePicture", "profilePictureUrl");
                Plugin.log.Debug("Player avatar response parsed: hasUrl=" + !string.IsNullOrEmpty(avatar));
                onComplete(NormalizeAvatarUrl(avatar, source));
            }
            catch (Exception ex)
            {
                Plugin.log.Warn("Failed to parse player avatar: " + ex.Message);
                onComplete(string.Empty);
            }
        }

        private static IEnumerator FetchBeatLeaderScores(
            LeaderboardRequest request,
            Action<List<LeaderboardInfo>> onSuccess,
            Action<string> onFailure)
        {
            if (request.Scope != LeaderboardScope.Global && string.IsNullOrEmpty(request.PlayerId))
            {
                onFailure("BeatLeader needs the current player ID for Country or Following scope.");
                yield break;
            }

            var scope = request.Scope == LeaderboardScope.Country
                ? "country"
                : request.Scope == LeaderboardScope.Following ? "friends" : "global";
            var playerQuery = string.IsNullOrEmpty(request.PlayerId)
                ? string.Empty
                : "&player=" + Uri.EscapeDataString(request.PlayerId);
            var count = LeaderboardRequest.ClampCount(request.Count);
            var pageSize = Math.Min(50, count);
            var pageCount = Math.Max(1, (count + pageSize - 1) / pageSize);
            var allScores = new List<LeaderboardInfo>();
            string requestError = null;
            for (var page = 1; page <= pageCount && allScores.Count < count; page++)
            {
                var url = string.Format(
                    "{0}/v3/scores/{1}/{2}/{3}/general/{4}/page?page={5}&count={6}{7}",
                    BeatLeaderBaseUrl,
                    Uri.EscapeDataString(request.Hash),
                    Uri.EscapeDataString(request.Difficulty),
                    Uri.EscapeDataString(request.Mode),
                    scope,
                    page,
                    pageSize,
                    playerQuery);
                string json = null;
                yield return DownloadJson(url, value => json = value, error => requestError = error);

                List<LeaderboardInfo> pageScores;
                if (string.IsNullOrEmpty(json) ||
                    !TryParseScores(json, LeaderboardSource.BeatLeader, out pageScores) ||
                    pageScores.Count == 0)
                    break;

                allScores.AddRange(pageScores);
                if (pageScores.Count < pageSize)
                    break;
            }

            if (allScores.Count > 0)
            {
                Plugin.log.Info("BeatLeader response parsed: scores=" + allScores.Count);
                onSuccess(LimitScores(DeduplicateScores(allScores), count));
                yield break;
            }

            onFailure(requestError ?? "BeatLeader returned an unsupported response.");
        }

        private static IEnumerator FetchScoreSaberScores(
            LeaderboardRequest request,
            Action<List<LeaderboardInfo>> onSuccess,
            Action<string> onFailure)
        {
            var count = LeaderboardRequest.ClampCount(request.Count);
            var mode = Uri.EscapeDataString(request.ScoreSaberMode);
            var hash = Uri.EscapeDataString(request.Hash);

            // ScoreSaber v2 accepts up to 50 entries in one request. Some early
            // deployments accepted the serialized difficulty name instead of its
            // numeric value, so both documented route shapes are supported.
            var routeDifficulties = new[]
            {
                request.ScoreSaberDifficulty.ToString(),
                Uri.EscapeDataString(request.Difficulty)
            };

            string lastError = null;

            if (request.Scope != LeaderboardScope.Global)
            {
                foreach (var difficulty in routeDifficulties.Distinct())
                {
                    var filter = request.Scope == LeaderboardScope.Following
                        ? "&pivot=friends&includePlayerScore=true"
                        : "&scope=country&includePlayerScore=true";
                    var scopedScores = new List<LeaderboardInfo>();
                    var pageSize = Math.Min(50, count);
                    var pageCount = Math.Max(1, (count + pageSize - 1) / pageSize);
                    for (var page = 1; page <= pageCount && scopedScores.Count < count; page++)
                    {
                        var relativeUrl = string.Format(
                            "/v2/leaderboards/hash/{0}/{1}/{2}/scores?page={3}&limit={4}{5}",
                            hash,
                            mode,
                            difficulty,
                            page,
                            pageSize,
                            filter);

                        string scopedJson = null;
                        string scopedError = null;
                        yield return DownloadScoreSaberAuthenticatedJson(
                            relativeUrl,
                            value => scopedJson = value,
                            error => scopedError = error);
                        lastError = scopedError ?? lastError;

                        List<LeaderboardInfo> pageScores;
                        if (string.IsNullOrEmpty(scopedJson) ||
                            !TryParseScores(scopedJson, LeaderboardSource.ScoreSaber, out pageScores) ||
                            pageScores.Count == 0)
                            break;

                        scopedScores.AddRange(pageScores);
                        if (pageScores.Count < pageSize)
                            break;
                    }

                    if (scopedScores.Count > 0)
                    {
                        Plugin.log.Info(string.Format(
                            "ScoreSaber scoped response parsed: scope={0}, difficultyRoute={1}, scores={2}",
                            request.Scope,
                            difficulty,
                            scopedScores.Count));
                        onSuccess(LimitScores(DeduplicateScores(scopedScores), count));
                        yield break;
                    }
                }

                // Older ScoreSaber PC builds expose the same scopes through the
                // authenticated game endpoints. Keep this fallback for 1.40.x.
                var scopedLeaderboardMaxScore = 0;
                yield return FetchScoreSaberMaxScore(
                    request,
                    value => scopedLeaderboardMaxScore = value);
                var legacyScopedScores = new List<LeaderboardInfo>();
                var scopeRoute = request.Scope == LeaderboardScope.Following
                    ? "around-friends"
                    : "around-country";
                var maxLegacyPages = Math.Max(1, (count + 11) / 12);
                for (var page = 1; page <= maxLegacyPages && legacyScopedScores.Count < count; page++)
                {
                    var relativeUrl = string.Format(
                        "/game/leaderboard/{0}/{1}/mode/{2}/difficulty/{3}?page={4}",
                        scopeRoute,
                        hash,
                        mode,
                        request.ScoreSaberDifficulty,
                        page);
                    string pageJson = null;
                    string pageError = null;
                    yield return DownloadScoreSaberAuthenticatedJson(
                        relativeUrl,
                        value => pageJson = value,
                        error => pageError = error);
                    lastError = pageError ?? lastError;

                    List<LeaderboardInfo> pageScores;
                    if (string.IsNullOrEmpty(pageJson) ||
                        !TryParseScores(pageJson, LeaderboardSource.ScoreSaber, out pageScores) ||
                        pageScores.Count == 0)
                        break;

                    legacyScopedScores.AddRange(pageScores);
                }

                if (legacyScopedScores.Count > 0)
                {
                    if (scopedLeaderboardMaxScore > 0)
                    {
                        foreach (var score in legacyScopedScores.Where(score => score.playerAccuracy < 0f))
                        {
                            var rawScore = score.playerBaseScore >= 0
                                ? score.playerBaseScore
                                : score.playerScore;
                            score.playerAccuracy = (float)rawScore / scopedLeaderboardMaxScore;
                        }
                    }

                    LeaderboardInfo personalBest = null;
                    yield return FetchScoreSaberPersonalBest(
                        request,
                        request.ScoreSaberDifficulty.ToString(),
                        value => personalBest = value);
                    MergePersonalBest(legacyScopedScores, personalBest);
                    onSuccess(LimitScores(DeduplicateScores(legacyScopedScores), count));
                    yield break;
                }

                onFailure(lastError ??
                    "ScoreSaber scope requires an authenticated ScoreSaber PC mod session.");
                yield break;
            }

            foreach (var difficulty in routeDifficulties.Distinct())
            {
                var scores = new List<LeaderboardInfo>();
                var pageSize = Math.Min(50, count);
                var pageCount = Math.Max(1, (count + pageSize - 1) / pageSize);
                for (var page = 1; page <= pageCount && scores.Count < count; page++)
                {
                    var url = string.Format(
                        "{0}/api/v2/leaderboards/hash/{1}/{2}/{3}/scores?page={4}&limit={5}",
                        ScoreSaberBaseUrl, hash, mode, difficulty, page, pageSize);

                    string json = null;
                    string requestError = null;
                    yield return DownloadJson(url, value => json = value, error => requestError = error);
                    lastError = requestError;

                    List<LeaderboardInfo> pageScores;
                    if (string.IsNullOrEmpty(json) ||
                        !TryParseScores(json, LeaderboardSource.ScoreSaber, out pageScores) ||
                        pageScores.Count == 0)
                        break;

                    scores.AddRange(pageScores);
                    if (pageScores.Count < pageSize)
                        break;
                }

                if (scores.Count > 0)
                {
                    Plugin.log.Info(string.Format(
                        "ScoreSaber v2 response parsed: difficultyRoute={0}, scores={1}",
                        difficulty,
                        scores.Count));
                    LeaderboardInfo personalBest = null;
                    yield return FetchScoreSaberPersonalBest(
                        request,
                        difficulty,
                        value => personalBest = value);
                    MergePersonalBest(scores, personalBest);
                    onSuccess(LimitScores(DeduplicateScores(scores), count));
                    yield break;
                }
            }

            // Compatibility fallback for the public v1 endpoint used by older
            // ScoreSaber installations. That endpoint is paged, normally by 12.
            Plugin.log.Warn("ScoreSaber v2 routes failed; trying legacy public API fallback.");
            var leaderboardMaxScore = 0;
            yield return FetchScoreSaberMaxScore(
                request,
                value => leaderboardMaxScore = value);
            var legacyScores = new List<LeaderboardInfo>();
            var maxPublicLegacyPages = Math.Max(1, (count + 11) / 12);
            for (var page = 1; page <= maxPublicLegacyPages && legacyScores.Count < count; page++)
            {
                var legacyUrl = string.Format(
                    "{0}/api/leaderboard/by-hash/{1}/scores?difficulty={2}&gameMode={3}&page={4}&withMetadata=false",
                    ScoreSaberBaseUrl, hash, request.ScoreSaberDifficulty, mode, page);

                string legacyJson = null;
                string legacyError = null;
                yield return DownloadJson(legacyUrl, value => legacyJson = value, error => legacyError = error);
                lastError = legacyError ?? lastError;

                List<LeaderboardInfo> pageScores;
                if (string.IsNullOrEmpty(legacyJson) ||
                    !TryParseScores(legacyJson, LeaderboardSource.ScoreSaber, out pageScores))
                    break;

                if (pageScores.Count == 0)
                    break;

                legacyScores.AddRange(pageScores);
            }

            if (legacyScores.Count > 0)
            {
                if (leaderboardMaxScore > 0)
                {
                    foreach (var score in legacyScores.Where(score => score.playerAccuracy < 0f))
                    {
                        var rawScore = score.playerBaseScore >= 0
                            ? score.playerBaseScore
                            : score.playerScore;
                        score.playerAccuracy = (float)rawScore / leaderboardMaxScore;
                    }
                }

                LeaderboardInfo personalBest = null;
                yield return FetchScoreSaberPersonalBest(
                    request,
                    request.ScoreSaberDifficulty.ToString(),
                    value => personalBest = value);
                MergePersonalBest(legacyScores, personalBest);
                Plugin.log.Info("ScoreSaber legacy response parsed: scores=" + legacyScores.Count);
                onSuccess(LimitScores(DeduplicateScores(legacyScores), count));
                yield break;
            }

            onFailure(lastError ?? "ScoreSaber returned an unsupported response.");
        }

        private static IEnumerator FetchScoreSaberPersonalBest(
            LeaderboardRequest request,
            string difficulty,
            Action<LeaderboardInfo> onComplete)
        {
            if (string.IsNullOrEmpty(request.PlayerId))
            {
                onComplete(null);
                yield break;
            }

            var url = string.Format(
                "{0}/api/v2/players/{1}/scores/hash/{2}/{3}/{4}",
                ScoreSaberBaseUrl,
                Uri.EscapeDataString(request.PlayerId),
                Uri.EscapeDataString(request.Hash),
                Uri.EscapeDataString(request.ScoreSaberMode),
                Uri.EscapeDataString(difficulty));
            string json = null;
            yield return DownloadJson(
                url,
                value => json = value,
                error => Plugin.log.Debug("ScoreSaber PB is unavailable: " + error),
                false);

            if (string.IsNullOrEmpty(json))
            {
                onComplete(null);
                yield break;
            }

            try
            {
                var root = JToken.Parse(json);
                var objectRoot = root as JObject;
                var scoreToken = objectRoot == null ? root : objectRoot["data"] ?? root;
                var personalBest = ParseScoreToken(
                    scoreToken,
                    LeaderboardSource.ScoreSaber,
                    1,
                    true);
                if (personalBest != null)
                {
                    personalBest.isPersonalBest = true;
                    Plugin.log.Debug(string.Format(
                        "ScoreSaber PB parsed: rank={0}, score={1}, accuracy={2:0.0000}",
                        personalBest.playerPosition,
                        personalBest.playerScore,
                        personalBest.playerAccuracy));
                }
                onComplete(personalBest);
            }
            catch (Exception ex)
            {
                Plugin.log.Debug("ScoreSaber PB parse failed: " + ex.Message);
                onComplete(null);
            }
        }

        private static IEnumerator FetchScoreSaberMaxScore(
            LeaderboardRequest request,
            Action<int> onComplete)
        {
            var url = string.Format(
                "{0}/api/leaderboard/by-hash/{1}/info?difficulty={2}&gameMode={3}",
                ScoreSaberBaseUrl,
                Uri.EscapeDataString(request.Hash),
                request.ScoreSaberDifficulty,
                Uri.EscapeDataString(request.ScoreSaberMode));
            string json = null;
            yield return DownloadJson(
                url,
                value => json = value,
                error => Plugin.log.Debug("ScoreSaber maxScore is unavailable: " + error),
                false);

            var maxScore = 0;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    maxScore = ReadInt(JToken.Parse(json), "maxScore");
                }
                catch (Exception ex)
                {
                    Plugin.log.Debug("ScoreSaber maxScore parse failed: " + ex.Message);
                }
            }

            Plugin.log.Debug("ScoreSaber leaderboard maxScore=" + maxScore);
            onComplete(maxScore);
        }

        private static void MergePersonalBest(
            IList<LeaderboardInfo> scores,
            LeaderboardInfo personalBest)
        {
            if (personalBest == null)
                return;

            var identity = ScoreIdentity(personalBest);
            var existing = scores.FirstOrDefault(score => ScoreIdentity(score) == identity);
            if (existing == null)
            {
                scores.Add(personalBest);
                return;
            }

            existing.isPersonalBest = true;
            if (existing.playerAccuracy < 0f && personalBest.playerAccuracy >= 0f)
                existing.playerAccuracy = personalBest.playerAccuracy;
        }

        private static IEnumerator DownloadScoreSaberAuthenticatedJson(
            string relativeUrl,
            Action<string> onSuccess,
            Action<string> onFailure)
        {
            Task<string> task;
            try
            {
                var pluginType = Type.GetType("ScoreSaber.Plugin, ScoreSaber", false);
                var instanceProperty = pluginType == null
                    ? null
                    : pluginType.GetProperty(
                        "Instance",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var pluginInstance = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
                var httpProperty = pluginType == null
                    ? null
                    : pluginType.GetProperty(
                        "HttpInstance",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var http = httpProperty == null || pluginInstance == null
                    ? null
                    : httpProperty.GetValue(pluginInstance, null);
                var getAsync = http == null
                    ? null
                    : http.GetType().GetMethod(
                        "GetAsync",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (getAsync == null)
                {
                    onFailure("Authenticated ScoreSaber HTTP client is unavailable.");
                    yield break;
                }

                Plugin.log.Debug("ScoreSaber authenticated GET " + relativeUrl);
                task = getAsync.Invoke(http, new object[] { relativeUrl }) as Task<string>;
                if (task == null)
                {
                    onFailure("ScoreSaber returned an unsupported HTTP task.");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                onFailure("Could not start authenticated ScoreSaber request: " + ex.Message);
                yield break;
            }

            while (!task.IsCompleted)
                yield return null;

            if (task.IsCanceled || task.IsFaulted)
            {
                var error = task.Exception == null
                    ? "request was cancelled"
                    : task.Exception.GetBaseException().Message;
                onFailure("Authenticated ScoreSaber request failed: " + error);
                yield break;
            }

            onSuccess(task.Result);
        }

        private static IEnumerable<LeaderboardInfo> DeduplicateScores(
            IEnumerable<LeaderboardInfo> scores)
        {
            return scores.GroupBy(ScoreIdentity).Select(group => group.First());
        }

        private static List<LeaderboardInfo> LimitScores(
            IEnumerable<LeaderboardInfo> scores,
            int count)
        {
            var allScores = scores
                .OrderBy(score => score.playerPosition <= 0 ? int.MaxValue : score.playerPosition)
                .ToList();
            var limited = allScores.Take(count).ToList();
            var personalBest = allScores.FirstOrDefault(score => score.isPersonalBest);
            if (personalBest != null && !limited.Contains(personalBest))
                limited.Add(personalBest);
            return limited;
        }

        private static string ScoreIdentity(LeaderboardInfo score)
        {
            return string.IsNullOrEmpty(score.playerId)
                ? score.playerName + "\n" + score.playerScore
                : score.playerId;
        }

        private static IEnumerator DownloadJson(
            string url,
            Action<string> onSuccess,
            Action<string> onFailure,
            bool warnOnFailure = true)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = 20;
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("User-Agent", "InSongLeaderboard/1.3.0 BeatSaber/1.40.8");

            Plugin.log.Debug("HTTP GET " + url);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var message = string.Format(
                    "Leaderboard request failed ({0}): {1}",
                    request.responseCode,
                    string.IsNullOrEmpty(request.error) ? request.url : request.error);
                if (warnOnFailure)
                    Plugin.log.Warn(message);
                else
                    Plugin.log.Debug(message);
                request.Dispose();
                onFailure(message);
                yield break;
            }

            var text = request.downloadHandler == null ? null : request.downloadHandler.text;
            Plugin.log.Debug(string.Format(
                "HTTP response: status={0}, characters={1}, url={2}",
                request.responseCode,
                text == null ? 0 : text.Length,
                url));
            request.Dispose();
            onSuccess(text);
        }

        private static bool TryParseScores(string json, LeaderboardSource source, out List<LeaderboardInfo> result)
        {
            result = new List<LeaderboardInfo>();
            try
            {
                var root = JToken.Parse(json);
                var array = FindScoresArray(root);
                if (array == null)
                {
                    Plugin.log.Warn(string.Format(
                        "Leaderboard JSON has no score array: source={0}, rootType={1}, characters={2}",
                        source,
                        root.Type,
                        json.Length));
                    return false;
                }

                var fallbackRank = 1;
                foreach (var token in array)
                {
                    var parsed = ParseScoreToken(token, source, fallbackRank, false);
                    if (parsed != null)
                        result.Add(parsed);
                    fallbackRank++;
                }

                var objectRoot = root as JObject;
                var supplementalToken = objectRoot == null
                    ? null
                    : objectRoot["selection"] ??
                      objectRoot["playerScore"] ??
                      objectRoot["leaderboardInfo"]?["playerScore"];
                var supplemental = ParseScoreToken(
                    supplementalToken,
                    source,
                    fallbackRank,
                    true);
                if (supplemental != null)
                {
                    var identity = ScoreIdentity(supplemental);
                    var existing = result.FirstOrDefault(score => ScoreIdentity(score) == identity);
                    if (existing == null)
                        result.Add(supplemental);
                    else
                        existing.isPersonalBest = true;
                }

                result.Sort((left, right) => left.playerPosition.CompareTo(right.playerPosition));
                Plugin.log.Debug(string.Format(
                    "Leaderboard JSON parsed: source={0}, rawItems={1}, acceptedItems={2}, personalBest={3}",
                    source,
                    array.Count,
                    result.Count,
                    result.Any(score => score.isPersonalBest)));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.log.Warn("Failed to parse leaderboard response: " + ex.Message);
                return false;
            }
        }

        private static LeaderboardInfo ParseScoreToken(
            JToken token,
            LeaderboardSource source,
            int fallbackRank,
            bool personalBest)
        {
            var score = token as JObject;
            if (score == null)
                return null;

            var player = score["player"] as JObject
                         ?? score["leaderboardPlayerInfo"] as JObject
                         ?? score["playerInfo"] as JObject;
            var name = ReadString(player, "name", "playerNameInGame", "username");
            if (string.IsNullOrEmpty(name))
                name = ReadString(score, "playerName", "name");
            if (personalBest && string.IsNullOrEmpty(name))
                name = string.IsNullOrEmpty(Plugin.currentPlayerName)
                    ? "Player"
                    : Plugin.currentPlayerName;

            var scoreValue = ReadInt(score, "modifiedScore", "baseScore", "score");
            var baseScore = ReadInt(score, "unmodifiedScore", "baseScore");
            if (string.IsNullOrEmpty(name) || scoreValue < 0)
                return null;

            var rank = ReadInt(score, "rank", "responseRank");
            if (rank <= 0)
                rank = fallbackRank;

            var playerId = ReadString(player, "id", "playerId");
            if (string.IsNullOrEmpty(playerId))
                playerId = ReadString(score, "playerId");
            if (personalBest && string.IsNullOrEmpty(playerId))
                playerId = Plugin.currentPlayerId;
            var avatar = ReadString(player, "avatar", "profilePicture", "profilePictureUrl");
            return new LeaderboardInfo(
                playerId,
                name,
                scoreValue,
                rank,
                NormalizeAvatarUrl(avatar, source),
                false,
                ReadFloat(score, "accuracy", "acc"),
                ReadModifiers(score),
                personalBest,
                baseScore);
        }

        private static JArray FindScoresArray(JToken root)
        {
            var directArray = root as JArray;
            if (directArray != null)
                return directArray;

            var directNames = new[] { "data", "scores", "items", "results" };
            foreach (var name in directNames)
            {
                var value = root[name];
                var array = value as JArray;
                if (array != null)
                    return array;

                var nested = value as JObject;
                if (nested != null)
                {
                    foreach (var nestedName in directNames)
                    {
                        array = nested[nestedName] as JArray;
                        if (array != null)
                            return array;
                    }
                }
            }

            return null;
        }

        private static string ReadString(JToken token, params string[] names)
        {
            if (token == null)
                return string.Empty;

            foreach (var name in names)
            {
                var value = token[name];
                if (value != null && value.Type != JTokenType.Null)
                    return value.ToString();
            }

            return string.Empty;
        }

        private static int ReadInt(JToken token, params string[] names)
        {
            var text = ReadString(token, names);
            int value;
            return int.TryParse(text, out value) ? value : -1;
        }

        private static float ReadFloat(JToken token, params string[] names)
        {
            var text = ReadString(token, names);
            float value;
            if (float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
                return value;

            // JValue.ToString() follows the game process culture. On Russian
            // Windows a valid JSON number can therefore become "0,975" here.
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value)
                ? value
                : -1f;
        }

        private static string ReadModifiers(JToken score)
        {
            if (score == null)
                return string.Empty;

            var token = score["mods"] ?? score["modifiers"];
            var array = token as JArray;
            if (array != null)
                return string.Join(",", array.Values<string>().Where(value => !string.IsNullOrEmpty(value)));

            return token == null || token.Type == JTokenType.Null
                ? string.Empty
                : token.ToString();
        }

        private static string NormalizeAvatarUrl(string avatar, LeaderboardSource source)
        {
            if (string.IsNullOrWhiteSpace(avatar))
                return string.Empty;

            if (avatar.StartsWith("//"))
                return "https:" + avatar;

            if (avatar.StartsWith("/"))
                return (source == LeaderboardSource.BeatLeader ? BeatLeaderBaseUrl : ScoreSaberBaseUrl) + avatar;

            return avatar;
        }
    }
}
