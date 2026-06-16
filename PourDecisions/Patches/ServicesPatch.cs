using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;
using PourDecisions.UI;
using UnityEngine;

namespace PourDecisions.Patches
{
    // Spawns our DontDestroyOnLoad services when the game's StatsManager comes up.
    [HarmonyPatch(typeof(StatsManager), "Start")]
    internal static class StatsManagerStartPatch
    {
        private static GameObject? _services;

        [HarmonyPostfix]
        public static void Postfix()
        {
            BarTabStats.EnsureLoaded();
            StatMilestones.Wire();   // subscribe to StatsCore's lifetime stats (push, no polling)
            PourStats.Init();        // define the networked confirmed-drinks stat
            if (_services != null) return;

            _services = new GameObject("PourDecisions_Services");
            _services.AddComponent<DrinkHud>();
            Object.DontDestroyOnLoad(_services);
            PourDecisions.LogVerbose("[Boot] services spawned.");
        }
    }

    // Attach our RPC component to PunManager's GameObject so our [PunRPC]s ride its stable, shared
    // PhotonView (the one vanilla code already RPCs through). Proper RPCs, no RaiseEvent codes.
    [HarmonyPatch(typeof(PunManager), "Awake")]
    internal static class PunManagerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PunManager __instance)
        {
            if (__instance.GetComponent<PourNet>() == null)
                __instance.gameObject.AddComponent<PourNet>();
        }
    }

    // (Run reset moved to RuleEngine.OnRunReset, fired when you return to the real lobby - the tab is
    // run-long and persists across levels, the shop, and the truck.)

    // The results screen: show the run's Bar Tab leaderboard once, in the native popup.
    [HarmonyPatch(typeof(GameDirector), "gameStateResult")]
    internal static class GameDirectorResultPatch
    {
        internal static bool Shown;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Shown || !PluginConfig.Enabled.Value) return;
            var board = BarTabStats.Leaderboard();
            if (board.Count == 0) return;

            Shown = true;
            var top = board[0].Value;
            string unit = PluginConfig.EffectiveUnitWord;
            string word = top.Total == 1 ? unit : unit + "s";
            string msg = $"BAR TAB\n{top.Name} - {top.Total} {word}";

            if (BigMessageUI.instance != null)
                BigMessageUI.instance.BigMessage(msg, "", 60f, new Color(0.33f, 0.55f, 0.18f), Color.white);
        }
    }
}
