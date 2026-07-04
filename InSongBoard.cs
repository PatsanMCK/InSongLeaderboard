using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using BeatSaberMarkupLanguage.Attributes;
using UnityEngine;

namespace InSongLeaderboard
{
    public class InSongBoard : MonoBehaviour, INotifyPropertyChanged
    {
        private bool _init;
        private float _timer;
        internal List<LeaderboardTableView.ScoreData> currentScores = new List<LeaderboardTableView.ScoreData>();

        [UIComponent("songLeaderboard")] internal LeaderboardTableView leaderboardTableView;

        public void Update()
        {
            if (!_init) return;
            _timer += Time.deltaTime;
            if (_timer > PluginConfig.Instance.refreshTime)
            {
                UpdateScores();
                _timer = 0;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            catch (Exception ex)
            {
                Plugin.log?.Error($"Error Invoking PropertyChanged: {ex.Message}");
                Plugin.log?.Error(ex);
            }
        }

        [UIAction("#post-parse")]
        internal void InitializeBoard()
        {
            //Add Player score if one isn't present

            var playerScore = new LeaderboardInfo(Plugin.currentPlayerName, 0, 0);
            Plugin.storedScores.Add(playerScore);
            _init = true;

            UpdateScores();
        }

        internal void UpdateScores()
        {
            if (!_init) return;
            currentScores.Clear();
            var playerScore = Plugin.storedScores.FirstOrDefault(x => x.playerPosition == 0);
            if (playerScore == null) return;
            Plugin.storedScores.Sort(LeaderboardInfo.CompareScore);
            foreach (var score in Plugin.storedScores)
                if (score.playerPosition != 0)
                    score.playerPosition = Plugin.storedScores.IndexOf(score) + 1;
            var playerIndex = Plugin.storedScores.IndexOf(playerScore);
            var additionalScoreCount = 4;
            var playerEntry =
                new LeaderboardTableView.ScoreData(playerScore.playerScore, playerScore.playerName, 0, false);
            currentScores.Add(playerEntry);
            var belowScore = Plugin.storedScores.ElementAtOrDefault(playerIndex + 1);
            if (belowScore != null)
            {
                currentScores.Add(new LeaderboardTableView.ScoreData(belowScore.playerScore, belowScore.playerName,
                    belowScore.playerPosition, false));
                additionalScoreCount--;
            }

            for (var i = playerIndex - 1; i >= 0; i--)
            {
                if (additionalScoreCount == 0)
                    break;
                var score = Plugin.storedScores[i];
                currentScores.Add(new LeaderboardTableView.ScoreData(score.playerScore, score.playerName,
                    score.playerPosition, false));
                additionalScoreCount--;
            }

            for (var i = playerIndex + 2; i < Plugin.storedScores.Count; i++)
            {
                if (additionalScoreCount == 0)
                    break;
                var score = Plugin.storedScores[i];
                currentScores.Add(new LeaderboardTableView.ScoreData(score.playerScore, score.playerName,
                    score.playerPosition, false));
                additionalScoreCount--;
            }

            currentScores.Sort(CompareLeaderBoardData);


            leaderboardTableView.SetScores(currentScores, currentScores.IndexOf(playerEntry));
        }

        private static int CompareLeaderBoardData(LeaderboardTableView.ScoreData a, LeaderboardTableView.ScoreData b)
        {
            if (!PluginConfig.Instance.sortByAcc)
                return b.score.CompareTo(a.score);
            //       Plugin.Log("A: " + a.GetAcc() + " B: " + b.GetAcc());
            return GetAccScoreData(b).CompareTo(GetAccScoreData(a));
        }

        public static float GetAccScoreData(LeaderboardTableView.ScoreData score)
        {
            if (score.rank == 0)
                if (Plugin.currentMaxPossibleScore > 0)
                    return (float)score.score / Plugin.currentMaxPossibleScore;
                else
                    return 0;

            return (float)score.score / Plugin.maxPossibleScore;
        }
    }
}