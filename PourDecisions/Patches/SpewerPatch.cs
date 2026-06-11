using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // The spewer (vanilla EnemySlowMouth) latches onto a Semibot's face and then hoses the crew in front of
    // them. Its state machine networks through UpdateStateRPC (RpcTarget.All, runs on the host too), and the
    // target is synced via UpdatePlayerTargetRPC, so playerTarget is reliable on every client. Same shape as
    // the headman/Hidden patches: host-gated, level/arena only, drunk-text reaction only (no drink).
    //   Attached -> the mouth just clamped onto playerTarget's face.
    //   Puke     -> the mouth on playerTarget's face just unloaded on everyone.
    [HarmonyPatch(typeof(EnemySlowMouth), "UpdateStateRPC")]
    internal static class SpewerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemySlowMouth __instance, EnemySlowMouth.State newState)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.RunIsLevel() && !SemiFunc.RunIsArena()) return;

            var target = __instance.playerTarget;
            if (target == null) return;

            if (newState == EnemySlowMouth.State.Attached) DrunkTexts.ReactSpewerFace(target);
            else if (newState == EnemySlowMouth.State.Puke) DrunkTexts.ReactSpewerPuke(target);
        }
    }
}
