using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using BeatSaberMarkupLanguage.GameplaySetup;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using HarmonyLib;
using HMUI;
using IPA;
using IPA.Config.Stores;
using UnityEngine;
using Config = IPA.Config.Config;
using IPALogger = IPA.Logging.Logger;

namespace InSongLeaderboard
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        private const string HarmonyId = "com.patsanmck.BeatSaber.InSongLeaderboard";
        private const string GameplayTabName = "InSong LB";
        private const string GameplayTabResource = "InSongLeaderboard.settings.bsml";

        public static string currentPlayerId = string.Empty;
        public static string currentPlayerName = string.Empty;
        public static int currentPlayerScore;
        public static int currentMaxPossibleScore;
        public static float currentPlayerAccuracy;
        public static string currentPlayerModifiers = string.Empty;
        public static int maxPossibleScore;

        private readonly GameplaySetupView _gameplaySetupView = new GameplaySetupView();
        private Harmony _harmony;
        private bool _gameplayTabRegistered;

        internal static BeatmapKey CurrentBeatmapKey { get; private set; }
        internal static bool HasCurrentBeatmapKey { get; private set; }
        internal static Plugin Instance { get; private set; }
        internal static IPALogger log { get; private set; }

        [Init]
        public Plugin(IPALogger logger, Config config)
        {
            Instance = this;
            log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();
            PluginConfig.Instance.leaderboardDepth =
                LeaderboardRequest.ClampCount(PluginConfig.Instance.leaderboardDepth);
            log.Info(string.Format(
                "Initialized v1.3.0: enabled={0}, source={1}, scope={2}, depth={3}, avatars={4}, animation={5}, scoreAndAcc={6}, modifiers={7}",
                PluginConfig.Instance.enabled,
                PluginConfig.Instance.leaderboardSource,
                PluginConfig.Instance.leaderboardScope,
                PluginConfig.Instance.leaderboardDepth,
                PluginConfig.Instance.showAvatars,
                PluginConfig.Instance.animateRankUp,
                PluginConfig.Instance.showScoreAndAccuracy,
                PluginConfig.Instance.showModifiers));
        }

        [OnStart]
        public void OnApplicationStart()
        {
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();
            log.Info("Harmony patches applied; subscribing to menu and game scene events.");

            BSEvents.gameSceneLoaded += BSEvents_GameSceneLoaded;
            BSEvents.lateMenuSceneLoadedFresh += BSEvents_lateMenuSceneLoadedFresh;
        }

        internal static void CaptureBeatmap(BeatmapKey beatmapKey)
        {
            CurrentBeatmapKey = beatmapKey;
            HasCurrentBeatmapKey = true;
            log.Info(string.Format(
                "Captured beatmap: {0} / {1} / {2}",
                beatmapKey.levelId,
                beatmapKey.difficulty,
                beatmapKey.beatmapCharacteristic.serializedName));
        }

        private async void BSEvents_lateMenuSceneLoadedFresh(ScenesTransitionSetupDataSO transition)
        {
            log.Debug("Late menu scene loaded; registering gameplay setup tab and resolving platform user.");
            EnsureGameplayTabRegistered();

            try
            {
                var userInfo = await GetUserInfo.GetUserAsync();
                currentPlayerId = userInfo.platformUserId;
                currentPlayerName = userInfo.userName;
                log.Debug(string.Format(
                    "Platform user resolved: id={0}, name={1}",
                    currentPlayerId,
                    currentPlayerName));
            }
            catch (Exception ex)
            {
                log.Warn("Could not read the current platform user: " + ex.Message);
            }
        }

        private void EnsureGameplayTabRegistered()
        {
            if (_gameplayTabRegistered)
            {
                log.Debug("Gameplay setup tab is already registered.");
                return;
            }

            try
            {
                GameplaySetup.Instance.AddTab(
                    GameplayTabName,
                    GameplayTabResource,
                    _gameplaySetupView);
                _gameplayTabRegistered = true;
                log.Info(string.Format(
                    "Gameplay setup tab registered: name={0}, resource={1}",
                    GameplayTabName,
                    GameplayTabResource));
            }
            catch (Exception ex)
            {
                log.Error("Failed to register the gameplay setup tab after menu load: " + ex);
            }
        }

        private InSongBoard SetupLeaderboardObject()
        {
            log.Debug(string.Format(
                "Creating HUD: hasBeatmapKey={0}, position=({1}, {2}), scale={3}",
                HasCurrentBeatmapKey,
                PluginConfig.Instance.position.x,
                PluginConfig.Instance.position.y,
                PluginConfig.Instance.scale));
            var coreGameHud = Resources.FindObjectsOfTypeAll<CoreGameHUDController>()
                .FirstOrDefault(controller => controller.isActiveAndEnabled);
            var flyingGameHud = Resources.FindObjectsOfTypeAll<FlyingGameHUDRotation>()
                .FirstOrDefault(controller => controller.isActiveAndEnabled);

            var depth = coreGameHud != null && coreGameHud.transform.childCount > 1
                ? coreGameHud.transform.GetChild(1).position.z
                : 9f;

            if (flyingGameHud != null && flyingGameHud.transform.childCount > 0)
            {
                depth = flyingGameHud.transform.GetChild(0).position.z / 2f;
            }

            var floatingScreen = FloatingScreen.CreateFloatingScreen(
                new Vector2(86f, 52f),
                false,
                Vector3.zero,
                Quaternion.identity,
                0f,
                false);
            var leaderboard = floatingScreen.gameObject;
            leaderboard.name = "InSongLeaderboard";

            if (coreGameHud != null)
                leaderboard.transform.SetParent(coreGameHud.transform, true);

            if (PluginConfig.Instance.hasSavedPanelTransform)
            {
                leaderboard.transform.localPosition = PluginConfig.Instance.panelLocalPosition;
                leaderboard.transform.localEulerAngles = PluginConfig.Instance.panelLocalRotation;
            }
            else
            {
                leaderboard.transform.localPosition = new Vector3(
                    PluginConfig.Instance.position.x,
                    PluginConfig.Instance.position.y,
                    depth);
                leaderboard.transform.localRotation = flyingGameHud == null
                    ? Quaternion.identity
                    : Quaternion.Euler(345f, 0f, 0f);
            }

            leaderboard.transform.localScale = PluginConfig.Instance.scale *
                                               new Vector3(0.06f, 0.06f, 0.06f);

            var board = leaderboard.AddComponent<InSongBoard>();
            board.Configure(CurrentBeatmapKey, HasCurrentBeatmapKey);

            try
            {
                var markup = Utilities.GetResourceContent(
                    Assembly.GetExecutingAssembly(),
                    "InSongLeaderboard.board.bsml");
                log.Debug("Parsing HUD BSML; characters=" + (markup == null ? 0 : markup.Length));
                BSMLParser.Instance.Parse(markup, leaderboard, board);
                log.Info("HUD BSML parsed successfully.");
            }
            catch (Exception ex)
            {
                log.Error("Failed to parse HUD BSML: " + ex);
                UnityEngine.Object.Destroy(leaderboard);
                return null;
            }

            return board;
        }

        private void BSEvents_GameSceneLoaded()
        {
            log.Info(string.Format(
                "Game scene loaded: levelDataSet={0}, mode={1}, enabled={2}, beatmapCaptured={3}",
                BS_Utils.Plugin.LevelData.IsSet,
                BS_Utils.Plugin.LevelData.IsSet ? BS_Utils.Plugin.LevelData.Mode.ToString() : "n/a",
                PluginConfig.Instance.enabled,
                HasCurrentBeatmapKey));

            if (!BS_Utils.Plugin.LevelData.IsSet ||
                BS_Utils.Plugin.LevelData.Mode != BS_Utils.Gameplay.Mode.Standard ||
                !PluginConfig.Instance.enabled)
            {
                log.Info("HUD creation skipped because level data, game mode, or enabled setting did not match.");
                return;
            }

            var scoreController = Resources.FindObjectsOfTypeAll<ScoreController>().LastOrDefault();
            if (scoreController == null)
            {
                log.Warn("HUD creation skipped: ScoreController was not found.");
                return;
            }

            currentPlayerScore = 0;
            currentMaxPossibleScore = 0;
            currentPlayerAccuracy = 0f;
            currentPlayerModifiers = FormatGameplayModifiers(
                BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.gameplayModifiers);
            maxPossibleScore = ScoreModel.ComputeQuickInaccurateMaxMultipliedScoreForBeatmap(
                BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.beatmapBasicData);

            var board = SetupLeaderboardObject();
            if (board == null)
                return;

            scoreController.scoreDidChangeEvent += delegate(int score, int modifiedScore)
            {
                currentPlayerScore = modifiedScore > 0 ? modifiedScore : score;
                currentMaxPossibleScore = scoreController.immediateMaxPossibleModifiedScore;
                currentPlayerAccuracy = currentMaxPossibleScore > 0
                    ? (float)currentPlayerScore / currentMaxPossibleScore
                    : 0f;
                board.UpdateScores();
            };
        }

        private static string FormatGameplayModifiers(GameplayModifiers modifiers)
        {
            if (modifiers == null)
                return string.Empty;

            var values = new List<string>();
            if (modifiers.energyType == GameplayModifiers.EnergyType.Battery) values.Add("BE");
            if (modifiers.noFailOn0Energy) values.Add("NF");
            if (modifiers.instaFail) values.Add("IF");
            if (modifiers.failOnSaberClash) values.Add("CS");
            if (modifiers.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles) values.Add("NO");
            if (modifiers.noBombs) values.Add("NB");
            if (modifiers.strictAngles) values.Add("SA");
            if (modifiers.disappearingArrows) values.Add("DA");
            if (modifiers.ghostNotes) values.Add("GN");
            if (modifiers.songSpeed == GameplayModifiers.SongSpeed.Slower) values.Add("SS");
            if (modifiers.songSpeed == GameplayModifiers.SongSpeed.Faster) values.Add("FS");
            if (modifiers.songSpeed == GameplayModifiers.SongSpeed.SuperFast) values.Add("SF");
            if (modifiers.smallCubes) values.Add("SC");
            if (modifiers.proMode) values.Add("PM");
            if (modifiers.noArrows) values.Add("NA");
            if (modifiers.zenMode) values.Add("ZM");
            return string.Join(",", values);
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            BSEvents.gameSceneLoaded -= BSEvents_GameSceneLoaded;
            BSEvents.lateMenuSceneLoadedFresh -= BSEvents_lateMenuSceneLoadedFresh;

            try
            {
                if (_gameplayTabRegistered)
                    GameplaySetup.Instance.RemoveTab(GameplayTabName);
            }
            catch (Exception)
            {
                // GameplaySetup can already be destroyed during application exit.
            }

            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }
}
