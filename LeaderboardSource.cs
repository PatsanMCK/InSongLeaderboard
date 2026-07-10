using System;

namespace InSongLeaderboard
{
    internal enum LeaderboardSource
    {
        ScoreSaber,
        BeatLeader
    }

    internal enum LeaderboardScope
    {
        Global,
        Country,
        Following
    }

    internal sealed class LeaderboardRequest
    {
        public string Hash { get; set; }
        public string Difficulty { get; set; }
        public string Mode { get; set; }
        public string PlayerId { get; set; }
        public LeaderboardScope Scope { get; set; }
        public int Count { get; set; }

        public int ScoreSaberDifficulty
        {
            get
            {
                switch (Difficulty)
                {
                    case "Easy": return 1;
                    case "Normal": return 3;
                    case "Hard": return 5;
                    case "Expert": return 7;
                    case "ExpertPlus": return 9;
                    default: return 0;
                }
            }
        }

        public string ScoreSaberMode
        {
            get
            {
                if (string.IsNullOrEmpty(Mode) || Mode.StartsWith("Solo", StringComparison.Ordinal))
                    return Mode;

                return "Solo" + Mode;
            }
        }

        public static int ClampCount(int value)
        {
            return Math.Max(10, Math.Min(100, value));
        }
    }
}
