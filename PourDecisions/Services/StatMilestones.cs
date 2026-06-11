using PourDecisions.Configuration;
using StatsCore;
using UnityEngine;

namespace PourDecisions.Services
{
    // PourDecisions reacting to StatsCore's permanent record - the mini-game layer on top of the tracking.
    // We SUBSCRIBE to the local player's lifetime stats (push, no polling - StatsCore fires Stat.Changed the
    // instant a value moves) and mark the milestones. First rule: every 25th career death earns a dry nod
    // from the Taxman. Add more by subscribing to other GameStats here, or use stat.OnReach(id, n, ...).
    internal static class StatMilestones
    {
        private static bool _wired;

        public static void Wire()
        {
            if (_wired) return;
            _wired = true;
            GameStats.Deaths.Changed += OnDeath;
        }

        private static void OnDeath(string steamID, int careerTotal)
        {
            if (careerTotal <= 0 || careerTotal % 25 != 0) return;          // every 25 lifetime deaths
            if (!PluginConfig.Enabled.Value) return;
            if (!SemiFunc.RunIsLevel() && !SemiFunc.RunIsArena()) return;   // never on the main menu

            if (BigMessageUI.instance != null)
                BigMessageUI.instance.BigMessage($"DEATH #{careerTotal}\nthe taxman is keeping count",
                    "", 50f, new Color(0.8f, 0.2f, 0.2f), Color.white);
            PourDecisions.LogInfo($"[Milestone] {careerTotal} career deaths");
        }
    }
}
