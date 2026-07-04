using System.Collections.Generic;
using HarmonyLib;

namespace InSongLeaderboard
{
    [HarmonyPatch(typeof(LeaderboardTableView))]
    [HarmonyPatch("SetScores", MethodType.Normal)]
    internal class LeaderboardTableViewSetScores
    {
        private static void Postfix(List<LeaderboardTableView.ScoreData> scores, int specialScorePos)
        {
            Plugin.GrabScores();
        }
    }
}