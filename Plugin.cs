using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BeatSaberMarkupLanguage;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using HarmonyLib;
using HMUI;
using IPA;
using IPA.Config.Stores;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Config = IPA.Config.Config;
using IPALogger = IPA.Logging.Logger;

namespace InSongLeaderboard
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        public static List<LeaderboardInfo> storedScores = new List<LeaderboardInfo>();
        public static string currentPlayerName = "";
        public static int currentPlayerScore;
        public static int currentMaxPossibleScore;
        public static int maxPossibleScore;
        private Timer timer;

        [Init]
        public Plugin(IPALogger logger, Config config)
        {
            Instance = this;
            log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();
        }

        internal static Plugin Instance { get; private set; }
        internal static IPALogger log { get; set; }

        [OnStart]
        public void OnApplicationStart()
        {
            var harmony = new Harmony("com.patsanmck.BeatSaber.InSongLeaderboard");
            harmony.PatchAll();
            BSEvents.gameSceneLoaded += BSEvents_GameSceneLoaded;
            BSEvents.lateMenuSceneLoadedFresh += BSEvents_lateMenuSceneLoadedFresh;
            BSEvents.difficultySelected += BSEvents_difficultySelected;
            BSEvents.levelSelected += BSEvents_levelSelected;
        }
        
        private void BSEvents_levelSelected(LevelCollectionViewController arg1, BeatmapLevel arg2)
        {
            storedScores.Clear();
        }

        private async void BSEvents_lateMenuSceneLoadedFresh(ScenesTransitionSetupDataSO obj)
        {
            var userInfo = await GetUserInfo.GetUserAsync();
            currentPlayerName = userInfo.userName;
            storedScores.Clear();
            TimedGrabbing();
        }

        private void BSEvents_difficultySelected(StandardLevelDetailViewController arg1)
        { 
            storedScores.Clear();
        }

        public void TimedGrabbing()
        {
            timer = new Timer(_ => GrabScores(), null, 2000, 2000);
        }
        private InSongBoard SetupLeaderboardObject()
        {
            var Leaderboard = new GameObject("InSongLeaderboard");
            var canvas = Leaderboard.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var cs = Leaderboard.AddComponent<CanvasScaler>();
            cs.scaleFactor = 1.0f;
            cs.dynamicPixelsPerUnit = 10f;
            var gr = Leaderboard.AddComponent<GraphicRaycaster>();

            var coreGameHUD = Resources.FindObjectsOfTypeAll<CoreGameHUDController>()
                ?.FirstOrDefault(x => x.isActiveAndEnabled)?.gameObject ?? null;
            var flyingGameHUD = Resources.FindObjectsOfTypeAll<FlyingGameHUDRotation>()
                .FirstOrDefault(x => x.isActiveAndEnabled);
            if (coreGameHUD != null)
                Leaderboard.transform.SetParent(coreGameHUD.transform, true);
            var depth = coreGameHUD != null ? coreGameHUD.transform.GetChild(1).transform.position.z : 9f;
            if (flyingGameHUD != null)
            {
                depth = flyingGameHUD.transform.GetChild(0).transform.position.z / 2;
                Leaderboard.transform.eulerAngles = new Vector3(345f, 0f, 0f);
            }

            Leaderboard.transform.localPosition = new Vector3(PluginConfig.Instance.position.x,
                PluginConfig.Instance.position.y, depth);
            Leaderboard.transform.localRotation = Quaternion.identity;
            Leaderboard.transform.localScale = PluginConfig.Instance.scale * new Vector3(0.06f, 0.06f, 0.06f);
            var canvasSettings = Leaderboard.AddComponent<CurvedCanvasSettings>();
            canvasSettings.SetRadius(0);

            var boardHandler = Leaderboard.AddComponent<InSongBoard>();

            BSMLParser.Instance.Parse(
                Utilities.GetResourceContent(Assembly.GetExecutingAssembly(),
                    "InSongLeaderboard.board.bsml"), Leaderboard, boardHandler);
            return boardHandler;
        }

        private void BSEvents_GameSceneLoaded()
        {
            if (!BS_Utils.Plugin.LevelData.IsSet || BS_Utils.Plugin.LevelData.Mode != BS_Utils.Gameplay.Mode.Standard || !PluginConfig.Instance.enabled)
                return;
            var scoreController = Resources.FindObjectsOfTypeAll<ScoreController>().LastOrDefault();
            if (scoreController == null) return;
            
            //Reset score values
            currentPlayerScore = 0;
            currentMaxPossibleScore = 0;
            maxPossibleScore = ScoreModel.ComputeQuickInaccurateMaxMultipliedScoreForBeatmap(BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.beatmapBasicData);
            //Create board
            var boardHandler = SetupLeaderboardObject();
            //Setup events
            scoreController.scoreDidChangeEvent += delegate(int score,int modifiedScore) { ScoreController_scoreDidChangeEvent(score, modifiedScore, boardHandler); };
            if (maxPossibleScore !=
                ScoreModel.ComputeQuickInaccurateMaxMultipliedScoreForBeatmap(BS_Utils.Plugin.LevelData
                    .GameplayCoreSceneSetupData.beatmapBasicData))
            {
                ScoreController_immediateMaxPossibleScoreDidChangeEvent(ScoreModel.ComputeQuickInaccurateMaxMultipliedScoreForBeatmap(BS_Utils.Plugin.LevelData
                    .GameplayCoreSceneSetupData.beatmapBasicData), maxPossibleScore);
            }
        }

        private void ScoreController_scoreDidChangeEvent(int score, int modifiedScore, InSongBoard leaderboard)
        {
            currentPlayerScore = score;
            storedScores.RemoveAll(x => x.playerPosition == 0);
            storedScores.Add(new LeaderboardInfo(currentPlayerName, currentPlayerScore, 0));
            leaderboard.UpdateScores();
        }
        private void ScoreController_immediateMaxPossibleScoreDidChangeEvent(int arg1, int arg2)
        {
            currentMaxPossibleScore = arg1;
        }
        
        [OnExit]
        public void OnApplicationQuit()
        {
            Harmony.UnpatchAll();
            timer.Dispose();
        }

        public static void GrabScores()
        {
            var leaderboardBSML = GameObject.Find("BSMLLeaderboard");
            if (leaderboardBSML != null && leaderboardBSML.activeInHierarchy)
            {
                if (SceneManager.GetActiveScene().name == "GameCore") return;
                storedScores.Clear();
                var boards = leaderboardBSML.transform.Find("Viewport").Find("Content")
                    .GetComponentsInChildren<LeaderboardTableCell>();
                if (boards != null)
                    try
                    {
                        foreach (var cell in boards)
                        {
                            var cellTexts = cell.GetComponentsInChildren<TextMeshProUGUI>();
                            var playerName = "";
                            var pos = -1;
                            var score = -1;
                            foreach (var text in cellTexts)
                            {
                                if (text.name == "PlayerName")
                                {
                                    playerName = text.text; 
                                    if (text.text.Contains("<size=80%>"))
                                    {
                                        //log.Info("1 " + playerName);
                                        var splitText = text.text.Split('>', '<');
                                        playerName = splitText[2];
                                        if (string.IsNullOrWhiteSpace(playerName) && splitText.Length >= 5)
                                            playerName = splitText[4];
                                        if (!string.IsNullOrWhiteSpace(playerName) && playerName.Contains(" - "))
                                            playerName = playerName.Substring(0, playerName.Length - 2);
                                    }
                                    else if (text.text.Contains("<size=70%>"))
                                    {
                                        playerName = text.text.Split('<')[0];
                                        //  Plugin.log.Info("2 " + playerName);
                                        if (!string.IsNullOrWhiteSpace(playerName) && playerName.Contains(" - "))
                                            playerName = playerName.Substring(0, playerName.Length - 2);
                                    }
                                }

                                if (text.name == "Rank") pos = int.Parse(text.text);
                                if (text.name == "Score") score = int.Parse(text.text.Replace(" ", ""));
                            }
                            var entry = new LeaderboardInfo(playerName, score, pos);
                            if (!storedScores.Any(x =>
                                    x.playerName == entry.playerName && x.playerScore == entry.playerScore))
                                storedScores.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to grab scores from Leaderboard {ex}");
                    }
            }
        }
    }
}