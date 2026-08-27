using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // King of the losers - the post-wipe arena. All three hooks are vanilla [PunRPC]s that fan out to every
    // client; we host-gate and react in the drunk-text layer only (no drinks). Reading Arena.cs:
    //
    //   PlayerKilledRPC(count)   - sent on every elimination where the survivor count lands on >1 (the drop
    //                              to exactly 1 is NOT sent here). So this is our "someone got yeeted" jeer.
    //   DestroyCrownCageRPC()    - fired the moment only one Semibot is left (cage opens, crown grabbable).
    //                              That IS last-one-standing; the survivor is the lone non-disabled player.
    //   CrownGrabRPC(viewID)     - the survivor grabbed the crown. Vanilla sets winnerPlayer + the
    //                              SessionManager crown that rides into regular runs. That's the coronation.

    [HarmonyPatch(typeof(Arena), "PlayerKilledRPC")]
    internal static class ArenaPlayerKilledPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int _playersAlive)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (_playersAlive >= 2) DrunkTexts.ReactArenaDeath();
        }
    }

    [HarmonyPatch(typeof(Arena), "DestroyCrownCageRPC")]
    internal static class ArenaLastStandingPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            var survivor = SoleSurvivor();
            if (survivor != null) DrunkTexts.ReactLastStanding(survivor);
        }

        // The one Semibot still standing when the cage opens.
        private static PlayerAvatar? SoleSurvivor()
        {
            PlayerAvatar? alive = null;
            foreach (var p in SemiFunc.PlayerGetAll())
            {
                if (p == null || p.isDisabled) continue;
                if (alive != null) return null;   // more than one up - shouldn't happen here, bail safe
                alive = p;
            }
            return alive;
        }
    }

    [HarmonyPatch(typeof(Arena), "CrownGrabRPC")]
    internal static class ArenaCrownGrabPatch
    {
        private static string _lastCrowned = "";

        // A new arena is a new coronation, even for the same king. Cleared on every level change.
        internal static void ResetLevel() => _lastCrowned = "";

        [HarmonyPostfix]
        public static void Postfix(Arena __instance)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            var winner = __instance.winnerPlayer;
            if (winner == null) return;
            if (winner.steamID == _lastCrowned) return;   // CrownGrabRPC can re-arrive; crown each king once
            _lastCrowned = winner.steamID;

            DrunkTexts.ReactCrownWin(winner);
        }
    }
}
