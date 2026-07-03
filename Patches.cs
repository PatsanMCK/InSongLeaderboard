using System.Collections.Generic;
using HarmonyLib;
namespace InSongLeaderboard
{
    [HarmonyPatch(typeof(LeaderboardTableView))]
    [HarmonyPatch("SetScores", MethodType.Normal)]
    class LeaderboardTableViewSetScores
    {
        static void Postfix(List<LeaderboardTableView.ScoreData> scores, int specialScorePos)
        {
            Plugin.GrabScores();
        }
    }
}
