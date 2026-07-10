namespace InSongLeaderboard
{
    public class LeaderboardInfo
    {
        public string playerId;
        public string playerName;
        public string avatarUrl;
        public int playerPosition;
        public int playerScore;
        public int playerBaseScore;
        public float playerAccuracy;
        public string modifiers;
        public bool isCurrentPlayer;
        public bool isPersonalBest;

        public LeaderboardInfo(string name, int score, int position)
            : this(string.Empty, name, score, position, string.Empty, false, -1f, string.Empty)
        {
        }

        public LeaderboardInfo(
            string id,
            string name,
            int score,
            int position,
            string avatar,
            bool currentPlayer,
            float accuracy = -1f,
            string scoreModifiers = "",
            bool personalBest = false,
            int baseScore = -1)
        {
            playerId = id ?? string.Empty;
            playerName = name;
            avatarUrl = avatar ?? string.Empty;
            playerScore = score;
            playerBaseScore = baseScore;
            playerPosition = position;
            playerAccuracy = accuracy;
            modifiers = scoreModifiers ?? string.Empty;
            isCurrentPlayer = currentPlayer;
            isPersonalBest = personalBest;
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
