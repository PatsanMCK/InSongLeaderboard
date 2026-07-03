using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using BeatSaberMarkupLanguage.Attributes;
namespace InSongLeaderboard
{
    public class InSongBoard : MonoBehaviour, INotifyPropertyChanged
    {

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
        [UIComponent("songLeaderboard")]
        internal LeaderboardTableView leaderboardTableView;
        internal List<LeaderboardTableView.ScoreData> currentScores = new List<LeaderboardTableView.ScoreData>();
        private bool _init = false;
        private float _timer = 0f;
        [UIAction("#post-parse")]
        internal void InitializeBoard()
        {
            //Add Player score if one isn't present

            var playerScore = new LeaderboardInfo(Plugin.currentPlayerName, 0, 0);
            Plugin.storedScores.Add(playerScore);
            _init = true;

            UpdateScores();
        }

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
        internal void UpdateScores()
        {
            if (!_init) return;
            currentScores.Clear();
            var playerScore = Plugin.storedScores.FirstOrDefault(x => x.playerPosition == 0);
            if (playerScore == null) return;
            Plugin.storedScores.Sort(LeaderboardInfo.CompareScore);
            foreach(var score in Plugin.storedScores)
            {
                if (score.playerPosition != 0)
                    score.playerPosition = Plugin.storedScores.IndexOf(score) + 1;
            }
            int playerIndex = Plugin.storedScores.IndexOf(playerScore);
            int additionalScoreCount = 4;
            var playerEntry = new LeaderboardTableView.ScoreData(playerScore.playerScore, playerScore.playerName, 0, false);
            currentScores.Add(playerEntry);
            var belowScore = Plugin.storedScores.ElementAtOrDefault(playerIndex + 1);
            if (belowScore != null)
            {
                currentScores.Add(new LeaderboardTableView.ScoreData(belowScore.playerScore, belowScore.playerName, belowScore.playerPosition, false));
                additionalScoreCount--;
            }
            for (int i = playerIndex - 1; i >= 0; i--)
            {
                if (additionalScoreCount == 0)
                    break;
                var score = Plugin.storedScores[i];
                currentScores.Add(new LeaderboardTableView.ScoreData(score.playerScore, score.playerName, score.playerPosition, false));
                additionalScoreCount--;
            }
            for (int i = playerIndex + 2; i < Plugin.storedScores.Count; i++)
            {
                if (additionalScoreCount == 0)
                    break;
                var score = Plugin.storedScores[i];
                currentScores.Add(new LeaderboardTableView.ScoreData(score.playerScore, score.playerName, score.playerPosition, false));
                additionalScoreCount--;
            }
            currentScores.Sort(CompareLeaderBoardData);


            leaderboardTableView.SetScores(currentScores, currentScores.IndexOf(playerEntry));
        }
        static int CompareLeaderBoardData(LeaderboardTableView.ScoreData a, LeaderboardTableView.ScoreData b)
        {
            if (!PluginConfig.Instance.sortByAcc)
                return b.score.CompareTo(a.score);
            else
            {
                //       Plugin.Log("A: " + a.GetAcc() + " B: " + b.GetAcc());
                return GetAccScoreData(b).CompareTo(GetAccScoreData(a));
            }
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
