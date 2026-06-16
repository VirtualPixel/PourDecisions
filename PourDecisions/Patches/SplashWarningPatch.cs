using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;
using PourDecisions.UI;

namespace PourDecisions.Patches
{
    // Shows our vanilla-styled usage warning once, right as the boot splash sequence ends - so it
    // sits alongside the game's own warning. Copy escalates with the play-date streak.
    [HarmonyPatch(typeof(SplashScreen), "StateDone")]
    internal static class SplashWarningPatch
    {
        private static bool _done;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_done) return;
            _done = true;

            // Disabled means fully dark: no warning, and tonight doesn't count
            // toward the launch-day streak either
            if (!PluginConfig.Enabled.Value) return;
            if (!PluginConfig.UsageWarning.Value) return;

            int streak = BarTabStats.RecordLaunchDay();
            if (streak >= PluginConfig.ExcessiveUsageDays.Value)
                UsageSplash.Show("INTERVENTION",
                    $"{streak} nights in a row. The cart misses the old you. Drink some water maybe. The quota isn't going anywhere. Unfortunately.", true);
            else
                UsageSplash.Show("FRIENDLY REMINDER",
                    "You and your crew are about to make some Pour Decisions. The monsters don't care that you're drunk. Neither does the quota. Good luck out there.", false);
        }
    }
}
