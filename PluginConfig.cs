using UnityEngine;

namespace InSongLeaderboard
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }
        public bool enabled { get; set; } = true;
        public bool sortByAcc { get; set; } = false;
        public string leaderboardSource { get; set; } = "ScoreSaber";
        public string leaderboardScope { get; set; } = "Global";
        public int leaderboardDepth { get; set; } = 10;
        public bool showAvatars { get; set; } = true;
        public bool animateRankUp { get; set; } = true;
        public bool showScoreAndAccuracy { get; set; } = true;
        public bool showModifiers { get; set; } = true;
        public Vector2 position { get; set; } = new Vector2(-6f, 2.5f);
        public bool hasSavedPanelTransform { get; set; } = false;
        public Vector3 panelLocalPosition { get; set; } = Vector3.zero;
        public Vector3 panelLocalRotation { get; set; } = Vector3.zero;
        public float scale { get; set; } = 1f;
        public float refreshTime { get; set; } = 0.5f;
    }
}
