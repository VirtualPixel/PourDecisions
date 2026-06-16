using HarmonyLib;
using PourDecisions.Models;
using PourDecisions.Services;
using System.Collections.Generic;
using UnityEngine;

namespace PourDecisions.Patches
{
    // Every drink trigger observes a vanilla RpcTarget.All RPC as a Harmony postfix - so each
    // client reacts to the same event simultaneously with no networking of our own. The rule
    // engine handles config gating and scene gating.

    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.PlayerDeathRPC))]
    internal static class PlayerDeathPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerAvatar __instance, int enemyIndex)
        {
            RuleEngine.OnEvent(DrinkEvent.Flatlined, __instance);

            // Host tracks who killed whom this run, so drunk Semibots can gripe about their nemesis.
            if (!SemiFunc.IsMasterClientOrSingleplayer() || enemyIndex < 0) return;
            var enemy = SemiFunc.EnemyGetFromIndex(enemyIndex);
            string? name = enemy != null && enemy.EnemyParent != null ? enemy.EnemyParent.enemyName : null;
            if (!string.IsNullOrEmpty(name))
                BarTabStats.RecordDeathByEnemy(SemiFunc.PlayerGetSteamID(__instance), name!);
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.ReviveRPC))]
    internal static class PlayerRevivePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerAvatar __instance) => RuleEngine.OnEvent(DrinkEvent.WelcomeBack, __instance);
    }

    [HarmonyPatch(typeof(PlayerTumble), nameof(PlayerTumble.TumbleSetRPC))]
    internal static class PlayerTumblePatch
    {
        // _playerInput == false means something knocked them down (not a self-tumble).
        [HarmonyPostfix]
        public static void Postfix(PlayerTumble __instance, bool _isTumbling, bool _playerInput)
        {
            if (_isTumbling && !_playerInput && __instance.playerAvatar != null)
                RuleEngine.OnEvent(DrinkEvent.DownBad, __instance.playerAvatar);
        }
    }

    // Fires only when the headman ACTUALLY grabs a head (the GrabHead state), not on every lunge at
    // whoever it's chasing. AttackSetRPC fired on the approach/attack against playerTarget, so it
    // spammed "got X" at people it was only chasing. UpdateStateRPC broadcasts the real state to all
    // clients; we act on the GrabHead transition (UpdateState only sends it on a state change).
    [HarmonyPatch(typeof(EnemyHeadGrabber), "UpdateStateRPC")]
    internal static class EnemyHeadGrabberPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnemyHeadGrabber __instance, EnemyHeadGrabber.State _state)
        {
            if (_state == EnemyHeadGrabber.State.GrabHead && __instance.playerTarget != null)
                RuleEngine.OnEvent(DrinkEvent.InTheBag, __instance.playerTarget);
        }
    }

    // Butterfingers fires whenever a valuable LOSES VALUE on impact - not just when it's fully
    // destroyed. Smacking a friend with a held valuable devalues it (e.g. -$20k) without breaking it,
    // which the old destroy-only hook missed entirely. BreakRPC carries the exact dollars lost and
    // fans out to all clients; we blame the last holder (the person who was swinging it).
    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "BreakRPC")]
    internal static class ValuableBreakPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PhysGrabObjectImpactDetector __instance, float valueLost, bool _loseValue)
        {
            if (!_loseValue || valueLost <= 0f) return;
            if (__instance.GetComponent<ValuableObject>() == null) return;

            var pgo = __instance.GetComponent<PhysGrabObject>();
            if (pgo == null) return;

            // Prefer whoever is ACTIVELY holding it when it breaks (break-while-holding), then fall back to
            // the last player who grabbed it (smashed-it-then-it-broke).
            PlayerAvatar? holder = null;
            if (pgo.playerGrabbing != null)
                foreach (var grabber in pgo.playerGrabbing)
                    if (grabber != null && grabber.playerAvatar != null) { holder = grabber.playerAvatar; break; }
            holder ??= pgo.lastPlayerGrabbing;

            if (holder != null)
                RuleEngine.OnEvent(DrinkEvent.Butterfingers, holder, Mathf.RoundToInt(valueLost));
        }
    }

    // Exactly ONE drink per extraction point, fired once on Complete and deduped by instance id.
    // Surplus is folded into that single fire (over-goal -> Surplus drink, otherwise -> CashOut),
    // read from RoundDirector's haul (set before Complete, unlike the point's own haulSurplus). So
    // a normal extraction = one CashOut, a quota-beating one = one Surplus - never both. Cleared each
    // level. Host-gating lives in RuleEngine.OnEvent.
    [HarmonyPatch(typeof(ExtractionPoint), nameof(ExtractionPoint.StateSetRPC))]
    internal static class ExtractionPointPatch
    {
        private static readonly HashSet<int> _fired = new();

        public static void ClearLevel() => _fired.Clear();

        [HarmonyPostfix]
        public static void Postfix(ExtractionPoint __instance, ExtractionPoint.State state)
        {
            if (state != ExtractionPoint.State.Complete) return;
            if (!_fired.Add(__instance.GetInstanceID())) return;

            var rd = RoundDirector.instance;
            bool surplus = rd != null && rd.currentHaul > rd.haulGoal;
            RuleEngine.OnEvent(surplus ? DrinkEvent.Surplus : DrinkEvent.CashOut, null);
        }
    }

    [HarmonyPatch(typeof(RunManagerPUN), "AllPlayersDeadRPC")]
    internal static class RunManagerWipePatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool _set)
        {
            if (_set) RuleEngine.OnEvent(DrinkEvent.QuotaMissed, null);
        }
    }

    [HarmonyPatch(typeof(EnemyParent), "SpawnRPC")]
    internal static class EnemySpawnPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => RuleEngine.OnEvent(DrinkEvent.TaxmanCometh, null);
    }
}
