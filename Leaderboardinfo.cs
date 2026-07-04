namespace InSongLeaderboard
{
    public class LeaderboardInfo
    {
        public string playerName;
        public int playerPosition;
        public int playerScore;

        public LeaderboardInfo(string name, int score, int position)
        {
            playerName = name;
            playerScore = score;
            playerPosition = position;
        }

        public static int CompareScore(LeaderboardInfo a, LeaderboardInfo b)
        {
            if (!PluginConfig.Instance.sortByAcc)
                return b.playerScore.CompareTo(a.playerScore);
            //       Plugin.Log("A: " + a.GetAcc() + " B: " + b.GetAcc());
            return b.GetAcc().CompareTo(a.GetAcc());
        }

        public float GetAcc()
        {
            if (playerPosition == 0)
                if (Plugin.currentMaxPossibleScore > 0)
                    return (float)playerScore / Plugin.currentMaxPossibleScore;
                else
                    return 0;

            return (float)playerScore / Plugin.maxPossibleScore;
        }
    }
}