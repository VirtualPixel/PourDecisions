using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // The Hidden - invisible, wheezing, universally known as the pervert / the asthmatic - drags players
    // off. Its state machine networks transitions through UpdateStateRPC (RpcTarget.All, so it runs on the
    // host too), exactly like the headman. We react on the PlayerPickup transition: the grab. Reaction
    // only (no drink) - host-gated, level-only, and the drunk-text layer self-throttles.
    [HarmonyPatch(typeof(EnemyHidden), "UpdateStateRPC")]
    internal static class HiddenGrabPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemyHidden __instance, EnemyHidden.State _state)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (_state != EnemyHidden.State.PlayerPickup) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.RunIsLevel() && !SemiFunc.RunIsArena()) return;

            var target = __instance.playerTarget;
            if (target != null) DrunkTexts.ReactHiddenGrab(target);
        }
    }
}
