using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // King of the losers - the RACE variant (ArenaRace: a drunk vehicle dash to the crown). Both hooks are
    // vanilla [PunRPC]s to RpcTarget.All, so they run on the host too. SetWinnerRPC crowns the racer who
    // reaches the crown (same SessionManager crown that rides into regular runs); PlayerEliminatedRPC fires
    // as racers drop out. Host-gated, drunk-text reaction only.
    [HarmonyPatch(typeof(ArenaRace), "PlayerEliminatedRPC")]
    internal static class ArenaRaceEliminatedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int _playersAlive)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (_playersAlive >= 2) DrunkTexts.ReactRaceOut();
        }
    }

    [HarmonyPatch(typeof(ArenaRace), "SetWinnerRPC")]
    internal static class ArenaRaceWinnerPatch
    {
        private static string _lastCrowned = "";

        internal static void ResetLevel() => _lastCrowned = "";

        [HarmonyPostfix]
        public static void Postfix(ArenaRace __instance)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            var winner = __instance.winnerPlayer;
            if (winner == null || winner.steamID == _lastCrowned) return;   // crown each racing king once
            _lastCrowned = winner.steamID;
            DrunkTexts.ReactRaceWin(winner);
        }
    }
}
