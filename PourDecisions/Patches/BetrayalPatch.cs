using HarmonyLib;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // Player-on-player damage all funnels through one vanilla RPC carrying the attacker and victim view IDs.
    // We resolve both to avatars and hand them to RuleEngine, which (host-only) turns a smack into a
    // FriendlyFire drink and a kill into a Backstabber finish-your-drink. Host-gating and dedupe live in
    // RuleEngine, same as every other trigger.
    [HarmonyPatch(typeof(PunManager), nameof(PunManager.PlayerDamagingPlayerRPC))]
    internal static class BetrayalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int _attackerViewID, int _victimViewID, int _damage, bool _tumbled, bool _killed)
        {
            var attacker = SemiFunc.PlayerAvatarGetFromPhotonID(_attackerViewID);
            var victim = SemiFunc.PlayerAvatarGetFromPhotonID(_victimViewID);
            if (attacker == null || victim == null) return;
            if (SemiFunc.PlayerGetSteamID(attacker) == SemiFunc.PlayerGetSteamID(victim)) return;   // self-harm isn't betrayal

            RuleEngine.OnBetrayal(attacker, victim, _damage, _tumbled, _killed);
        }
    }
}
