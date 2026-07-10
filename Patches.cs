using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace InSongLeaderboard
{
    [HarmonyPatch]
    internal static class LevelLaunchPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(StandardLevelScenesTransitionSetupDataSO)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == nameof(StandardLevelScenesTransitionSetupDataSO.Init))
                .Where(method => method.GetParameters().Any(parameter =>
                    parameter.Name == "beatmapKey" &&
                    (parameter.ParameterType == typeof(BeatmapKey) ||
                     parameter.ParameterType.GetElementType() == typeof(BeatmapKey))));
        }

        private static void Postfix(in BeatmapKey beatmapKey)
        {
            Plugin.CaptureBeatmap(beatmapKey);
        }
    }
}
