using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;
using PourDecisions.UI;
using UnityEngine;

namespace PourDecisions.Patches
{
    // Shows our vanilla-styled drink-responsibly notice once, right as the boot splash sequence ends, so it
    // sits alongside the game's own warning. The line is picked at random each launch and escalates with the
    // play-day streak. Always signs off with the same reminder.
    [HarmonyPatch(typeof(SplashScreen), "StateDone")]
    internal static class SplashWarningPatch
    {
        private static bool _done;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_done) return;
            _done = true;

            // Disabled means fully dark: no warning, and tonight doesn't count toward the launch-day streak.
            if (!PluginConfig.Enabled.Value) return;
            if (!PluginConfig.UsageWarning.Value) return;

            int streak = BarTabStats.RecordLaunchDay();
            if (streak >= PluginConfig.ExcessiveUsageDays.Value)
                UsageSplash.Show("INTERVENTION", SignOff(string.Format(Pick(Interventions), streak)), true);
            else
                UsageSplash.Show("FRIENDLY REMINDER", SignOff(Pick(Reminders)), false);
        }

        private static string Pick(string[] pool) => pool[Random.Range(0, pool.Length)];
        private static string SignOff(string body) => body + "\n\n(drink responsibly)";

        // First few launches: a light nudge before the chaos.
        private static readonly string[] Reminders =
        {
            "You and your crew are about to make some Pour Decisions. The monsters don't care that you're drunk. Neither does the quota. Good luck out there.",
            "The quota doesn't shrink because you've had a few. Pace yourself, and get some water in between extractions.",
            "Tonight's job: haul the valuables, dodge the monsters, owe the drinks. The cart can carry the loot. It can't carry you.",
            "The head-grabber has never once cared how drunk you are. Water is free. Use it.",
            "Every valuable you drop is a drink and a small tragedy. Maybe sip some water between the expensive mistakes.",
            "You can print a new body. You can't print a new liver. Have some water, hit the quota, get some sleep.",
            "Reminder: extraction needs you conscious. The Taxman would rather you made it back than made the leaderboard.",
        };

        // After several days in a row: lean on them a little. "{0}" = the streak.
        private static readonly string[] Interventions =
        {
            "{0} nights in a row. The cart misses the old you. Have some water. The quota isn't going anywhere. Unfortunately.",
            "{0} runs deep and counting. Even the Taxman thinks you should sit one out. Get some water in you.",
            "{0} nights straight and the monsters know your name now. Take one off, the quota will still be here.",
            "That's {0} in a row. Your Semibot needs a break and so do you. Water, sleep, then back to the grind.",
            "{0} days on the bottle. The extraction point isn't going to miss you for one night. Rest up.",
        };
    }
}
