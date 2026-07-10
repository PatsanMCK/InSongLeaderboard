using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BeatSaberMarkupLanguage.Attributes;
using UnityEngine;

namespace InSongLeaderboard
{
    internal sealed class GameplaySetupView : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        [UIValue("enabled")]
        public bool Enabled
        {
            get { return PluginConfig.Instance.enabled; }
            set
            {
                PluginConfig.Instance.enabled = value;
                Plugin.log.Info("Setting changed: enabled=" + value);
            }
        }

        [UIValue("source-button-text")]
        public string SourceButtonText
        {
            get { return PluginConfig.Instance.leaderboardSource; }
        }

        [UIAction("toggle-source")]
        private void ToggleSource()
        {
            PluginConfig.Instance.leaderboardSource =
                ParseSource(PluginConfig.Instance.leaderboardSource) == LeaderboardSource.ScoreSaber
                    ? LeaderboardSource.BeatLeader.ToString()
                    : LeaderboardSource.ScoreSaber.ToString();
            Plugin.log.Info("Setting changed: leaderboardSource=" + PluginConfig.Instance.leaderboardSource);
            NotifyPropertyChanged("SourceButtonText");
            NotifyPropertyChanged("source-button-text");
        }

        [UIValue("scope-button-text")]
        public string ScopeButtonText
        {
            get { return ParseScope(PluginConfig.Instance.leaderboardScope).ToString(); }
        }

        [UIAction("toggle-scope")]
        private void ToggleScope()
        {
            var scope = ParseScope(PluginConfig.Instance.leaderboardScope);
            switch (scope)
            {
                case LeaderboardScope.Global:
                    scope = LeaderboardScope.Country;
                    break;
                case LeaderboardScope.Country:
                    scope = LeaderboardScope.Following;
                    break;
                default:
                    scope = LeaderboardScope.Global;
                    break;
            }

            PluginConfig.Instance.leaderboardScope = scope.ToString();
            Plugin.log.Info("Setting changed: leaderboardScope=" + scope);
            NotifyPropertyChanged("ScopeButtonText");
            NotifyPropertyChanged("scope-button-text");
        }

        [UIValue("leaderboard-depth")]
        public float LeaderboardDepth
        {
            get { return LeaderboardRequest.ClampCount(PluginConfig.Instance.leaderboardDepth); }
            set
            {
                PluginConfig.Instance.leaderboardDepth = LeaderboardRequest.ClampCount(Mathf.RoundToInt(value));
                Plugin.log.Info("Setting changed: leaderboardDepth=" + PluginConfig.Instance.leaderboardDepth);
            }
        }

        [UIAction("format-depth")]
        private string FormatDepth(float value)
        {
            return "Top " + Mathf.RoundToInt(value);
        }

        [UIAction("reset-panel-position")]
        private void ResetPanelPosition()
        {
            PluginConfig.Instance.position = new Vector2(-6f, 2.5f);
            PluginConfig.Instance.panelLocalPosition = Vector3.zero;
            PluginConfig.Instance.panelLocalRotation = Vector3.zero;
            PluginConfig.Instance.hasSavedPanelTransform = false;
            Plugin.log.Info("Panel position reset; the default transform will be used on the next level.");
        }

        [UIValue("show-avatars")]
        public bool ShowAvatars
        {
            get { return PluginConfig.Instance.showAvatars; }
            set
            {
                PluginConfig.Instance.showAvatars = value;
                Plugin.log.Info("Setting changed: showAvatars=" + value);
            }
        }

        [UIValue("animate-rank-up")]
        public bool AnimateRankUp
        {
            get { return PluginConfig.Instance.animateRankUp; }
            set
            {
                PluginConfig.Instance.animateRankUp = value;
                Plugin.log.Info("Setting changed: animateRankUp=" + value);
            }
        }

        [UIValue("show-score-and-accuracy")]
        public bool ShowScoreAndAccuracy
        {
            get { return PluginConfig.Instance.showScoreAndAccuracy; }
            set
            {
                PluginConfig.Instance.showScoreAndAccuracy = value;
                Plugin.log.Info("Setting changed: showScoreAndAccuracy=" + value);
            }
        }

        [UIValue("show-modifiers")]
        public bool ShowModifiers
        {
            get { return PluginConfig.Instance.showModifiers; }
            set
            {
                PluginConfig.Instance.showModifiers = value;
                Plugin.log.Info("Setting changed: showModifiers=" + value);
            }
        }

        public static LeaderboardSource ParseSource(string value)
        {
            LeaderboardSource parsed;
            return Enum.TryParse(value, true, out parsed) ? parsed : LeaderboardSource.ScoreSaber;
        }

        public static LeaderboardScope ParseScope(string value)
        {
            LeaderboardScope parsed;
            return Enum.TryParse(value, true, out parsed) ? parsed : LeaderboardScope.Global;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
