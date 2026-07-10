using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using B83.Image.GIF;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace InSongLeaderboard
{
#pragma warning disable 0649 // Fields are assigned by BSML through UIComponent attributes.
    public class InSongBoard : MonoBehaviour
    {
        private const int VisibleRowCount = 5;
        private const int MaxGifFrames = 90;
        private const long MaxGifPixelBudget = 12000000L;

        private sealed class AvatarAsset
        {
            internal AvatarAsset(Sprite[] frames, float[] frameDelays)
            {
                Frames = frames;
                FrameDelays = frameDelays;
            }

            internal Sprite[] Frames { get; private set; }
            internal float[] FrameDelays { get; private set; }
            internal bool IsAnimated { get { return Frames != null && Frames.Length > 1; } }
        }

        private static readonly Dictionary<string, AvatarAsset> AvatarCache = new Dictionary<string, AvatarAsset>();
        private static readonly HashSet<string> FailedAvatarUrls = new HashSet<string>();

        private readonly List<LeaderboardInfo> _remoteScores = new List<LeaderboardInfo>();
        private readonly string[] _avatarUrls = new string[VisibleRowCount];
        private readonly Coroutine[] _avatarAnimations = new Coroutine[VisibleRowCount];
        private readonly HashSet<string> _avatarLoadsInProgress = new HashSet<string>();

        private BeatmapKey _beatmapKey;
        private PauseController _pauseController;
        private PanelDragHandle _panelDragHandle;
        private bool _hasBeatmapKey;
        private bool _init;
        private bool _leaderboardLoaded;
        private int _previousPlayerRank = -1;
        private int _lastLoggedPlayerRank = -1;
        private string _currentPlayerAvatar = string.Empty;
        private Coroutine _rankAnimation;
        private int _pendingRankGain;
        private int _accumulatedRankGain;
        private int _rankAnimationSlot = -1;

        private RectTransform[] _rows;
        private Image[] _rowBackgrounds;
        private Image[] _avatars;
        private Sprite[] _defaultAvatarSprites;
        private TextMeshProUGUI[] _ranks;
        private TextMeshProUGUI[] _names;
        private TextMeshProUGUI[] _scores;
        private TextMeshProUGUI[] _modifiers;
        private TextMeshProUGUI[] _movements;
        private Color[] _baseRowColors;

        [UIComponent("title")] private TextMeshProUGUI _title;
        [UIComponent("status")] private TextMeshProUGUI _status;
        [UIComponent("drag-handle")] private TextMeshProUGUI _dragHandleText;

        [UIComponent("row-0")] private RectTransform _row0;
        [UIComponent("row-1")] private RectTransform _row1;
        [UIComponent("row-2")] private RectTransform _row2;
        [UIComponent("row-3")] private RectTransform _row3;
        [UIComponent("row-4")] private RectTransform _row4;

        [UIComponent("row-0")] private Backgroundable _rowBackground0;
        [UIComponent("row-1")] private Backgroundable _rowBackground1;
        [UIComponent("row-2")] private Backgroundable _rowBackground2;
        [UIComponent("row-3")] private Backgroundable _rowBackground3;
        [UIComponent("row-4")] private Backgroundable _rowBackground4;

        [UIComponent("avatar-0")] private Image _avatar0;
        [UIComponent("avatar-1")] private Image _avatar1;
        [UIComponent("avatar-2")] private Image _avatar2;
        [UIComponent("avatar-3")] private Image _avatar3;
        [UIComponent("avatar-4")] private Image _avatar4;

        [UIComponent("rank-0")] private TextMeshProUGUI _rank0;
        [UIComponent("rank-1")] private TextMeshProUGUI _rank1;
        [UIComponent("rank-2")] private TextMeshProUGUI _rank2;
        [UIComponent("rank-3")] private TextMeshProUGUI _rank3;
        [UIComponent("rank-4")] private TextMeshProUGUI _rank4;

        [UIComponent("name-0")] private TextMeshProUGUI _name0;
        [UIComponent("name-1")] private TextMeshProUGUI _name1;
        [UIComponent("name-2")] private TextMeshProUGUI _name2;
        [UIComponent("name-3")] private TextMeshProUGUI _name3;
        [UIComponent("name-4")] private TextMeshProUGUI _name4;

        [UIComponent("score-0")] private TextMeshProUGUI _score0;
        [UIComponent("score-1")] private TextMeshProUGUI _score1;
        [UIComponent("score-2")] private TextMeshProUGUI _score2;
        [UIComponent("score-3")] private TextMeshProUGUI _score3;
        [UIComponent("score-4")] private TextMeshProUGUI _score4;

        [UIComponent("modifiers-0")] private TextMeshProUGUI _modifiers0;
        [UIComponent("modifiers-1")] private TextMeshProUGUI _modifiers1;
        [UIComponent("modifiers-2")] private TextMeshProUGUI _modifiers2;
        [UIComponent("modifiers-3")] private TextMeshProUGUI _modifiers3;
        [UIComponent("modifiers-4")] private TextMeshProUGUI _modifiers4;

        [UIComponent("movement-0")] private TextMeshProUGUI _movement0;
        [UIComponent("movement-1")] private TextMeshProUGUI _movement1;
        [UIComponent("movement-2")] private TextMeshProUGUI _movement2;
        [UIComponent("movement-3")] private TextMeshProUGUI _movement3;
        [UIComponent("movement-4")] private TextMeshProUGUI _movement4;

        internal void Configure(BeatmapKey beatmapKey, bool hasBeatmapKey)
        {
            _beatmapKey = beatmapKey;
            _hasBeatmapKey = hasBeatmapKey;
            Plugin.log.Debug(string.Format(
                "HUD configured: hasBeatmapKey={0}, levelId={1}, difficulty={2}, mode={3}",
                hasBeatmapKey,
                hasBeatmapKey ? beatmapKey.levelId : "n/a",
                hasBeatmapKey ? beatmapKey.difficulty.ToString() : "n/a",
                hasBeatmapKey ? beatmapKey.beatmapCharacteristic.serializedName : "n/a"));
        }

        [UIAction("#post-parse")]
        internal void InitializeBoard()
        {
            _rows = new[] { _row0, _row1, _row2, _row3, _row4 };
            _avatars = new[] { _avatar0, _avatar1, _avatar2, _avatar3, _avatar4 };
            _ranks = new[] { _rank0, _rank1, _rank2, _rank3, _rank4 };
            _names = new[] { _name0, _name1, _name2, _name3, _name4 };
            _scores = new[] { _score0, _score1, _score2, _score3, _score4 };
            _modifiers = new[] { _modifiers0, _modifiers1, _modifiers2, _modifiers3, _modifiers4 };
            _movements = new[] { _movement0, _movement1, _movement2, _movement3, _movement4 };
            var backgrounds = new[]
            {
                _rowBackground0,
                _rowBackground1,
                _rowBackground2,
                _rowBackground3,
                _rowBackground4
            };
            _rowBackgrounds = backgrounds
                .Select((background, index) => background == null
                    ? _rows[index].GetComponent<Image>()
                    : background.Background as Image)
                .ToArray();
            _defaultAvatarSprites = _avatars.Select(avatar => avatar.sprite).ToArray();
            _baseRowColors = new Color[VisibleRowCount];

            Plugin.log.Info(string.Format(
                "HUD post-parse initialized: rows={0}, avatars={1}, rankLabels={2}, nameLabels={3}, scoreLabels={4}, modifierLabels={5}",
                _rows.Count(row => row != null),
                _avatars.Count(avatar => avatar != null),
                _ranks.Count(label => label != null),
                _names.Count(label => label != null),
                _scores.Count(label => label != null),
                _modifiers.Count(label => label != null)));

            for (var i = 0; i < VisibleRowCount; i++)
                _movements[i].text = string.Empty;

            _init = true;
            BindPauseHandle();
            UpdateTitle();
            _status.text = _hasBeatmapKey ? "Loading scores..." : "Map data is unavailable";
            UpdateScores();

            if (_hasBeatmapKey)
                StartCoroutine(LoadLeaderboard());
            else
                Plugin.log.Warn("Leaderboard request was not started because no BeatmapKey was captured.");
        }

        private IEnumerator LoadLeaderboard()
        {
            var source = GameplaySetupView.ParseSource(PluginConfig.Instance.leaderboardSource);
            var request = new LeaderboardRequest
            {
                Hash = _beatmapKey.levelId.Replace("custom_level_", string.Empty),
                Difficulty = _beatmapKey.difficulty.ToString(),
                Mode = _beatmapKey.beatmapCharacteristic.serializedName,
                PlayerId = Plugin.currentPlayerId,
                Scope = GameplaySetupView.ParseScope(PluginConfig.Instance.leaderboardScope),
                Count = LeaderboardRequest.ClampCount(PluginConfig.Instance.leaderboardDepth)
            };

            Plugin.log.Info(string.Format(
                "Leaderboard load started: source={0}, scope={1}, hash={2}, difficulty={3}, mode={4}, count={5}, playerId={6}",
                source,
                request.Scope,
                request.Hash,
                request.Difficulty,
                request.Mode,
                request.Count,
                string.IsNullOrEmpty(request.PlayerId) ? "n/a" : request.PlayerId));

            List<LeaderboardInfo> loadedScores = null;
            string loadError = null;
            yield return LeaderboardApiClient.FetchScores(
                source,
                request,
                scores => loadedScores = scores,
                error => loadError = error);

            if (loadedScores == null)
            {
                _status.text = request.Scope == LeaderboardScope.Global
                    ? "Scores unavailable"
                    : request.Scope + " unavailable";
                Plugin.log.Warn("Leaderboard load failed: " + (loadError ?? "unknown error"));
                yield break;
            }

            _remoteScores.Clear();
            _remoteScores.AddRange(loadedScores);
            _leaderboardLoaded = true;
            _previousPlayerRank = -1;
            _lastLoggedPlayerRank = -1;

            Plugin.log.Info(string.Format(
                "Leaderboard load completed: source={0}, receivedScores={1}",
                source,
                _remoteScores.Count));

            var currentPlayerScore = _remoteScores.FirstOrDefault(IsCurrentPlatformPlayer);
            if (currentPlayerScore != null)
            {
                _currentPlayerAvatar = currentPlayerScore.avatarUrl;
                currentPlayerScore.isPersonalBest = true;
                Plugin.log.Info(string.Format(
                    "Personal Best loaded: rank={0}, score={1}, accuracy={2:0.0000}",
                    currentPlayerScore.playerPosition,
                    currentPlayerScore.playerScore,
                    currentPlayerScore.playerAccuracy));
            }

            _status.text = _remoteScores.Count == 0 ? "No scores yet" : string.Empty;
            UpdateScores();
            PreloadAvatars();

            if (string.IsNullOrEmpty(_currentPlayerAvatar))
            {
                yield return LeaderboardApiClient.FetchPlayerAvatar(
                    source,
                    Plugin.currentPlayerId,
                    avatar => _currentPlayerAvatar = avatar);
                Plugin.log.Debug("Current player avatar lookup completed: found=" +
                                 (!string.IsNullOrEmpty(_currentPlayerAvatar)));
                UpdateScores();
            }
        }

        private void UpdateTitle()
        {
            var source = GameplaySetupView.ParseSource(PluginConfig.Instance.leaderboardSource);
            var scope = GameplaySetupView.ParseScope(PluginConfig.Instance.leaderboardScope);
            _title.text = string.Format(
                "{0}  \u00b7  {1}  \u00b7  Top {2}",
                source == LeaderboardSource.BeatLeader ? "BeatLeader" : "ScoreSaber",
                scope,
                LeaderboardRequest.ClampCount(PluginConfig.Instance.leaderboardDepth));
        }

        internal void UpdateScores()
        {
            if (!_init)
                return;

            var ranking = _remoteScores
                .Select(CloneScore)
                .ToList();
            var personalBest = ranking.FirstOrDefault(score => score.isPersonalBest);

            var player = new LeaderboardInfo(
                Plugin.currentPlayerId,
                string.IsNullOrEmpty(Plugin.currentPlayerName) ? "Player" : Plugin.currentPlayerName,
                Plugin.currentPlayerScore,
                0,
                _currentPlayerAvatar,
                true,
                Plugin.currentPlayerAccuracy,
                Plugin.currentPlayerModifiers);
            ranking.Add(player);
            ranking.Sort((left, right) => right.playerScore.CompareTo(left.playerScore));

            for (var i = 0; i < ranking.Count; i++)
                ranking[i].playerPosition = i + 1;

            var playerIndex = ranking.IndexOf(player);
            var playerRank = playerIndex + 1;

            if (_leaderboardLoaded && playerRank != _lastLoggedPlayerRank)
            {
                Plugin.log.Debug(string.Format(
                    "Live rank updated: rank={0}, score={1}, accuracy={2:0.0000}, loadedOpponents={3}",
                    playerRank,
                    Plugin.currentPlayerScore,
                    Plugin.currentPlayerAccuracy,
                    _remoteScores.Count));
                _lastLoggedPlayerRank = playerRank;
            }
            var firstVisibleIndex = Mathf.Clamp(
                playerIndex - 2,
                0,
                Math.Max(0, ranking.Count - VisibleRowCount));
            var visible = ranking.Skip(firstVisibleIndex).Take(VisibleRowCount).ToList();
            PinPersonalBest(visible, personalBest, player);

            RenderRows(visible);

            if (_leaderboardLoaded &&
                _previousPlayerRank > 0 &&
                playerRank < _previousPlayerRank &&
                PluginConfig.Instance.animateRankUp)
            {
                var visiblePlayerSlot = visible.FindIndex(score => score.isCurrentPlayer);
                if (visiblePlayerSlot >= 0)
                    PlayRankUpAnimation(visiblePlayerSlot, _previousPlayerRank - playerRank);
            }

            _previousPlayerRank = playerRank;
        }

        private void RenderRows(IList<LeaderboardInfo> visible)
        {
            for (var i = 0; i < VisibleRowCount; i++)
            {
                if (i >= visible.Count)
                {
                    StopAvatarAnimation(i);
                    _avatarUrls[i] = string.Empty;
                    _rows[i].gameObject.SetActive(false);
                    continue;
                }

                _rows[i].gameObject.SetActive(true);
                var entry = visible[i];
                _ranks[i].text = "#" + entry.playerPosition;
                _names[i].text = entry.playerName + (entry.isPersonalBest ? " (PB)" : string.Empty);
                _scores[i].text = PluginConfig.Instance.showScoreAndAccuracy
                    ? FormatScore(entry.playerScore) + "  \u00b7  " + FormatAccuracy(entry)
                    : FormatScore(entry.playerScore);
                _modifiers[i].gameObject.SetActive(PluginConfig.Instance.showModifiers);
                _modifiers[i].text = FormatModifiers(entry.modifiers);

                var textColor = entry.isCurrentPlayer
                    ? new Color(0.40f, 0.88f, 1f, 1f)
                    : entry.isPersonalBest
                        ? new Color(1f, 0.78f, 0.24f, 1f)
                    : Color.white;
                _ranks[i].color = textColor;
                _names[i].color = textColor;
                _scores[i].color = textColor;
                _modifiers[i].color = string.IsNullOrEmpty(entry.modifiers)
                    ? new Color(textColor.r, textColor.g, textColor.b, 0.35f)
                    : new Color(1f, 0.72f, 0.24f, 1f);

                _baseRowColors[i] = entry.isCurrentPlayer
                    ? new Color(0.05f, 0.32f, 0.50f, 0.72f)
                    : entry.isPersonalBest
                        ? new Color(0.38f, 0.24f, 0.03f, 0.72f)
                    : new Color(0.05f, 0.05f, 0.05f, 0.52f);
                if (_rowBackgrounds[i] != null)
                    _rowBackgrounds[i].color = _baseRowColors[i];

                SetAvatar(i, entry.avatarUrl);
            }
        }

        private void SetAvatar(int slot, string url)
        {
            var normalizedUrl = url ?? string.Empty;
            if (_avatarUrls[slot] != normalizedUrl)
                StopAvatarAnimation(slot);
            _avatarUrls[slot] = normalizedUrl;
            var avatar = _avatars[slot];

            if (!PluginConfig.Instance.showAvatars)
            {
                StopAvatarAnimation(slot);
                avatar.gameObject.SetActive(false);
                return;
            }

            avatar.gameObject.SetActive(true);
            AvatarAsset cached;
            if (!string.IsNullOrEmpty(url) && AvatarCache.TryGetValue(url, out cached))
            {
                ApplyAvatarAsset(slot, url, cached);
                return;
            }

            avatar.sprite = _defaultAvatarSprites[slot];
            avatar.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            EnsureAvatarLoad(url);
        }

        private void EnsureAvatarLoad(string url)
        {
            if (string.IsNullOrEmpty(url) ||
                AvatarCache.ContainsKey(url) ||
                FailedAvatarUrls.Contains(url) ||
                !_avatarLoadsInProgress.Add(url))
                return;

            StartCoroutine(LoadAvatar(url));
        }

        private void PreloadAvatars()
        {
            if (!PluginConfig.Instance.showAvatars)
                return;

            var urls = _remoteScores
                .Select(score => score.avatarUrl)
                .Where(url => !string.IsNullOrEmpty(url))
                .Distinct()
                .Take(12)
                .ToList();
            Plugin.log.Debug("Avatar preload queued: count=" + urls.Count);
            foreach (var url in urls)
                EnsureAvatarLoad(url);
        }

        private IEnumerator LoadAvatar(string url)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = 15;
            request.SetRequestHeader("Accept", "image/gif,image/png,image/jpeg,image/webp,image/*");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Plugin.log.Debug(string.Format(
                    "Avatar request failed: status={0}, error={1}, url={2}",
                    request.responseCode,
                    request.error,
                    url));
                request.Dispose();
                _avatarLoadsInProgress.Remove(url);
                FailedAvatarUrls.Add(url);
                yield break;
            }

            var bytes = request.downloadHandler == null ? null : request.downloadHandler.data;
            var contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;
            request.Dispose();
            if (bytes == null || bytes.Length == 0)
            {
                _avatarLoadsInProgress.Remove(url);
                FailedAvatarUrls.Add(url);
                yield break;
            }

            AvatarAsset asset = null;
            var isGif = IsGif(bytes);
            try
            {
                asset = isGif ? DecodeGifAvatar(bytes, url) : DecodeStaticAvatar(bytes, url);
            }
            catch (Exception ex)
            {
                Plugin.log.Warn(string.Format(
                    "Avatar decode failed: gif={0}, contentType={1}, url={2}, error={3}",
                    isGif,
                    contentType,
                    url,
                    ex.Message));
            }

            if (asset == null || asset.Frames == null || asset.Frames.Length == 0)
            {
                _avatarLoadsInProgress.Remove(url);
                FailedAvatarUrls.Add(url);
                yield break;
            }

            AvatarCache[url] = asset;
            _avatarLoadsInProgress.Remove(url);
            var firstTexture = asset.Frames[0].texture;
            Plugin.log.Debug(string.Format(
                "Avatar loaded: size={0}x{1}, frames={2}, animated={3}, contentType={4}, url={5}",
                firstTexture.width,
                firstTexture.height,
                asset.Frames.Length,
                asset.IsAnimated,
                contentType,
                url));

            for (var slot = 0; slot < VisibleRowCount; slot++)
            {
                if (_avatarUrls[slot] == url && _avatars[slot] != null)
                    ApplyAvatarAsset(slot, url, asset);
            }
        }

        private static AvatarAsset DecodeStaticAvatar(byte[] bytes, string url)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = "InSongLeaderboard avatar " + url;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (!texture.LoadImage(bytes, true))
            {
                Destroy(texture);
                return null;
            }

            return new AvatarAsset(new[] { CreateAvatarSprite(texture) }, new[] { 0f });
        }

        private static AvatarAsset DecodeGifAvatar(byte[] bytes, string url)
        {
            GIFImage gif;
            using (var stream = new MemoryStream(bytes, false))
                gif = new GIFLoader().Load(stream);

            if (gif == null || gif.screen.width == 0 || gif.screen.height == 0 || gif.imageData.Count == 0)
                return null;

            var width = (int)gif.screen.width;
            var height = (int)gif.screen.height;
            var framePixelCount = (long)width * height;
            var memoryLimitedFrames = framePixelCount <= 0
                ? 1
                : (int)Math.Max(1L, Math.Min(MaxGifFrames, MaxGifPixelBudget / framePixelCount));
            var frameStep = Math.Max(1, (int)Math.Ceiling((double)gif.imageData.Count / memoryLimitedFrames));
            var pixels = new Color32[width * height];
            var frames = new List<Sprite>();
            var delays = new List<float>();
            var accumulatedDelay = 0f;

            for (var index = 0; index < gif.imageData.Count; index++)
            {
                var block = gif.imageData[index];
                if (block.graphicControl == null)
                    block.graphicControl = new GIFGraphicControlExt { Parent = gif };

                block.DrawTo(pixels, width, height);
                var delay = block.graphicControl.fdelay;
                accumulatedDelay += Mathf.Clamp(delay <= 0f ? 0.10f : delay, 0.02f, 1.0f);

                var captureFrame = (index + 1) % frameStep == 0 || index == gif.imageData.Count - 1;
                if (captureFrame)
                {
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.name = string.Format("InSongLeaderboard GIF {0} frame {1}", url, frames.Count);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.SetPixels32(pixels);
                    texture.Apply(false, true);
                    frames.Add(CreateAvatarSprite(texture));
                    delays.Add(Mathf.Max(0.02f, accumulatedDelay));
                    accumulatedDelay = 0f;
                }

                block.Dispose(pixels, width, height);
            }

            return frames.Count == 0 ? null : new AvatarAsset(frames.ToArray(), delays.ToArray());
        }

        private static Sprite CreateAvatarSprite(Texture2D texture)
        {
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private static bool IsGif(byte[] bytes)
        {
            return bytes != null &&
                   bytes.Length >= 6 &&
                   bytes[0] == (byte)'G' &&
                   bytes[1] == (byte)'I' &&
                   bytes[2] == (byte)'F' &&
                   bytes[3] == (byte)'8' &&
                   (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') &&
                   bytes[5] == (byte)'a';
        }

        private void ApplyAvatarAsset(int slot, string url, AvatarAsset asset)
        {
            if (asset == null || asset.Frames == null || asset.Frames.Length == 0)
                return;

            var avatar = _avatars[slot];
            avatar.sprite = asset.Frames[0];
            avatar.color = Color.white;
            if (asset.IsAnimated && _avatarAnimations[slot] == null)
                _avatarAnimations[slot] = StartCoroutine(AnimateAvatar(slot, url, asset));
        }

        private IEnumerator AnimateAvatar(int slot, string url, AvatarAsset asset)
        {
            var frame = 0;
            while (_avatarUrls[slot] == url &&
                   PluginConfig.Instance.showAvatars &&
                   asset.Frames != null &&
                   asset.Frames.Length > 1)
            {
                _avatars[slot].sprite = asset.Frames[frame];
                _avatars[slot].color = Color.white;
                var delay = frame < asset.FrameDelays.Length
                    ? asset.FrameDelays[frame]
                    : 0.10f;
                var elapsed = 0f;
                while (elapsed < delay)
                {
                    if (_avatarUrls[slot] != url || !PluginConfig.Instance.showAvatars)
                    {
                        _avatarAnimations[slot] = null;
                        yield break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                frame = (frame + 1) % asset.Frames.Length;
            }

            _avatarAnimations[slot] = null;
        }

        private void StopAvatarAnimation(int slot)
        {
            if (_avatarAnimations[slot] == null)
                return;

            StopCoroutine(_avatarAnimations[slot]);
            _avatarAnimations[slot] = null;
        }

        private void PlayRankUpAnimation(int slot, int places)
        {
            places = Math.Max(1, places);
            _pendingRankGain += places;
            _accumulatedRankGain += places;
            _rankAnimationSlot = slot;
            Plugin.log.Info(string.Format(
                "Rank-up animation queued: slot={0}, placesGained={1}, accumulated={2}",
                slot,
                places,
                _accumulatedRankGain));

            if (_rankAnimation == null)
                _rankAnimation = StartCoroutine(AnimateRankUpQueue());
        }

        private IEnumerator AnimateRankUpQueue()
        {
            while (_pendingRankGain > 0)
            {
                var places = _pendingRankGain;
                _pendingRankGain = 0;
                yield return AnimateRankUpPulse(_rankAnimationSlot, places);
            }

            ResetAnimatedRows();
            _accumulatedRankGain = 0;
            _rankAnimationSlot = -1;
            _rankAnimation = null;
        }

        private IEnumerator AnimateRankUpPulse(int slot, int places)
        {
            if (slot < 0 || slot >= VisibleRowCount || !_rows[slot].gameObject.activeInHierarchy)
                yield break;

            var row = _rows[slot];
            var movement = _movements[slot];
            var background = _rowBackgrounds[slot];
            var baseScale = row.localScale;
            var basePosition = row.localPosition;
            var baseColor = _baseRowColors[slot];
            var highlight = new Color(0.12f, 0.85f, 0.35f, 0.90f);

            movement.text = "\u25B2" + _accumulatedRankGain;
            movement.color = new Color(0.35f, 1f, 0.50f, 1f);

            var duration = Mathf.Clamp(0.58f + places * 0.025f, 0.58f, 1.15f);
            var liftDistance = Mathf.Clamp(1.5f + places * 0.10f, 1.5f, 3.5f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var linearProgress = Mathf.Clamp01(elapsed / duration);
                var progress = Mathf.SmoothStep(0f, 1f, linearProgress);
                var wave = Mathf.Sin(progress * Mathf.PI);
                var pulse = 1f + wave * Mathf.Lerp(0.09f, 0.16f, Mathf.Clamp01(places / 20f));
                row.localScale = baseScale * pulse;
                row.localPosition = basePosition + Vector3.up * (wave * liftDistance);
                if (background != null)
                    background.color = Color.Lerp(baseColor, highlight, wave * 0.90f);
                yield return null;
            }

            row.localScale = baseScale;
            row.localPosition = basePosition;
            if (background != null)
                background.color = baseColor;
        }

        private void ResetAnimatedRows()
        {
            for (var i = 0; i < VisibleRowCount; i++)
            {
                _rows[i].localScale = Vector3.one;
                _movements[i].text = string.Empty;
                if (_rowBackgrounds[i] != null)
                    _rowBackgrounds[i].color = _baseRowColors[i];
            }
        }

        private static LeaderboardInfo CloneScore(LeaderboardInfo source)
        {
            return new LeaderboardInfo(
                source.playerId,
                source.playerName,
                source.playerScore,
                source.playerPosition,
                source.avatarUrl,
                false,
                source.playerAccuracy,
                source.modifiers,
                source.isPersonalBest || IsCurrentPlatformPlayer(source),
                source.playerBaseScore);
        }

        private static void PinPersonalBest(
            List<LeaderboardInfo> visible,
            LeaderboardInfo personalBest,
            LeaderboardInfo livePlayer)
        {
            if (personalBest == null || visible.Contains(personalBest))
                return;

            if (visible.Count >= VisibleRowCount)
            {
                var removable = personalBest.playerScore >= livePlayer.playerScore
                    ? visible.AsEnumerable().Reverse().FirstOrDefault(score => !score.isCurrentPlayer)
                    : visible.FirstOrDefault(score => !score.isCurrentPlayer);
                if (removable != null)
                    visible.Remove(removable);
            }

            visible.Add(personalBest);
            visible.Sort((left, right) => left.playerPosition.CompareTo(right.playerPosition));
        }

        private static string FormatScore(int score)
        {
            return score.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");
        }

        private static string FormatAccuracy(LeaderboardInfo entry)
        {
            var accuracy = entry.isCurrentPlayer
                ? Plugin.currentPlayerAccuracy
                : entry.playerAccuracy;

            if (accuracy > 1.5f)
                accuracy /= 100f;

            if ((accuracy < 0f || (accuracy <= 0f && entry.playerScore > 0)) &&
                Plugin.maxPossibleScore > 0)
            {
                accuracy = (float)entry.playerScore / Plugin.maxPossibleScore;
            }

            if (accuracy < 0f)
                return "--.--%";

            return (Mathf.Clamp01(accuracy) * 100f)
                .ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatModifiers(string modifiers)
        {
            return string.IsNullOrWhiteSpace(modifiers)
                ? "--"
                : modifiers.Replace(",", " ");
        }

        private void BindPauseHandle()
        {
            if (_dragHandleText == null)
            {
                Plugin.log.Warn("The UI panel drag handle was not created by BSML.");
                return;
            }

            _dragHandleText.raycastTarget = true;
            _panelDragHandle = _dragHandleText.gameObject.GetComponent<PanelDragHandle>() ??
                               _dragHandleText.gameObject.AddComponent<PanelDragHandle>();
            _panelDragHandle.Initialize(transform, HandlePanelReleased);
            _pauseController = Resources.FindObjectsOfTypeAll<PauseController>()
                .FirstOrDefault(controller => controller.isActiveAndEnabled);
            if (_pauseController == null)
            {
                Plugin.log.Warn("PauseController was not found; the panel drag handle is unavailable.");
                return;
            }

            _pauseController.didPauseEvent += HandlePaused;
            _pauseController.didStartToResumeEvent += HandleResumeStarted;
            _pauseController.didResumeEvent += HandleResumed;
            Plugin.log.Info("Pause-only panel drag handle initialized.");
        }

        private void HandlePaused()
        {
            if (_panelDragHandle == null)
                return;

            _panelDragHandle.SetInteractable(true);
            if (_status != null && string.IsNullOrEmpty(_status.text))
                _status.text = "Drag the top handle";
            Plugin.log.Debug("Panel drag handle shown for pause.");
        }

        private void HandleResumeStarted()
        {
            HidePauseHandle();
        }

        private void HandleResumed()
        {
            HidePauseHandle();
        }

        private void HidePauseHandle()
        {
            if (_panelDragHandle != null)
                _panelDragHandle.SetInteractable(false);
            if (_status != null && _status.text == "Drag the top handle")
                _status.text = string.Empty;
        }

        private void HandlePanelReleased()
        {
            var localPosition = transform.localPosition;
            var localRotation = transform.localEulerAngles;
            PluginConfig.Instance.panelLocalPosition = localPosition;
            PluginConfig.Instance.panelLocalRotation = localRotation;
            PluginConfig.Instance.position = new Vector2(localPosition.x, localPosition.y);
            PluginConfig.Instance.hasSavedPanelTransform = true;
            Plugin.log.Info(string.Format(
                "Panel transform saved: localPosition=({0:0.000}, {1:0.000}, {2:0.000}), localRotation=({3:0.0}, {4:0.0}, {5:0.0})",
                localPosition.x,
                localPosition.y,
                localPosition.z,
                localRotation.x,
                localRotation.y,
                localRotation.z));
        }

        private void OnDestroy()
        {
            for (var slot = 0; slot < VisibleRowCount; slot++)
                StopAvatarAnimation(slot);

            if (_pauseController != null)
            {
                _pauseController.didPauseEvent -= HandlePaused;
                _pauseController.didStartToResumeEvent -= HandleResumeStarted;
                _pauseController.didResumeEvent -= HandleResumed;
            }
        }

        private static bool IsCurrentPlatformPlayer(LeaderboardInfo score)
        {
            if (!string.IsNullOrEmpty(Plugin.currentPlayerId) &&
                string.Equals(score.playerId, Plugin.currentPlayerId, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.IsNullOrEmpty(Plugin.currentPlayerId) &&
                   !string.IsNullOrEmpty(Plugin.currentPlayerName) &&
                   string.Equals(score.playerName, Plugin.currentPlayerName, StringComparison.OrdinalIgnoreCase);
        }
    }
#pragma warning restore 0649
}
