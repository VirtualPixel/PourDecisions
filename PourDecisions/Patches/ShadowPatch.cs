using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // The Shadow (EnemyShadow) reaches/stretches out and slaps you flat. Unlike the headman/Hidden/spewer,
    // its state transitions network to RpcTarget.Others, so UpdateStateRPC never runs on the HOST - the host
    // sets state in UpdateState itself. Our reaction is host-gated, so we patch UpdateState (the host-side
    // setter) and fire on the Slap transition. playerTarget is the Semibot getting slapped. Reaction only.
    [HarmonyPatch(typeof(EnemyShadow), "UpdateState")]
    internal static class ShadowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemyShadow __instance, EnemyShadow.State _nextState)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (_nextState != EnemyShadow.State.Slap) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.RunIsLevel() && !SemiFunc.RunIsArena()) return;

            var target = __instance.playerTarget;
            if (target != null) DrunkTexts.ReactShadow(target);
        }
    }
}
