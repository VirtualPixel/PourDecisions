using PourDecisions.Configuration;
using PourDecisions.Models;
using PourDecisions.UI;
using System.Collections.Generic;
using UnityEngine;

namespace PourDecisions.Services
{
    // Host-authoritative. The vanilla event patches fire on every client, but OnEvent only proceeds
    // on the host: the host applies its own ruleset, builds one DrinkOrder, applies it locally, and
    // broadcasts it. Every client (host included) renders and tallies the SAME order via ApplyOrder,
    // so everyone counts the same thing, dedupe happens in one spot, and nothing config-y needs syncing.
    internal static class RuleEngine
    {
        private static bool _firstBloodUsed;
        private static bool _enemySpawnedThisLevel;

        // Some events rapid-fire (repeated tumbles, a valuable taking hit after hit). A short per-player,
        // per-kind cooldown keeps one sustained beating from racking up a drink every second.
        private static readonly Dictionary<string, float> _cooldown = new();   // "steamID:kind" -> time it clears

        public static void OnNewLevel()
        {
            _enemySpawnedThisLevel = false;
            _cooldown.Clear();
            Patches.ExtractionPointPatch.ClearLevel();
        }

        private static float CooldownFor(DrinkEvent kind) => kind switch
        {
            DrinkEvent.DownBad => 5f,         // repeated tumbles
            DrinkEvent.InTheBag => 6f,        // headman re-grabbing
            DrinkEvent.Butterfingers => 4f,   // a valuable getting wailed on
            DrinkEvent.FriendlyFire => 5f,    // one scuffle isn't a drink per hit
            _ => 0f,
        };

        // True if this (player, kind) is still cooling down; otherwise stamps a fresh cooldown and returns false.
        private static bool OnCooldown(DrinkEvent kind, PlayerAvatar subject)
        {
            float cd = CooldownFor(kind);
            if (cd <= 0f) return false;
            string key = SteamID(subject) + ":" + (int)kind;
            float now = Time.time;
            if (_cooldown.TryGetValue(key, out float until) && now < until) return true;
            _cooldown[key] = now + cd;
            return false;
        }

        // Only when back in the real lobby (new game / new players) - the tab is run-long and persists
        // across levels, the shop, and the truck.
        public static void OnRunReset()
        {
            _firstBloodUsed = false;
            _enemySpawnedThisLevel = false;
            BarTabStats.ResetRun();
            ProofService.Reset();
            TaxmanBartender.ResetRun();
            Patches.GameDirectorResultPatch.Shown = false;
        }

        // True during actual gameplay scenes - keeps prompts out of the main menu.
        public static bool InRun() => RunManager.instance != null &&
            (SemiFunc.RunIsLevel() || SemiFunc.RunIsArena() || SemiFunc.RunIsLobby() || SemiFunc.RunIsShop());

        // subject == null for team events. valueLost only used by Butterfingers.
        // Host-only: only the master client turns events into orders.
        public static void OnEvent(DrinkEvent kind, PlayerAvatar? subject, int valueLost = 0)
        {
            if (!PluginConfig.Enabled.Value || !InRun()) return;
            // The shop AND the truck are downtime - horseplay between levels (jostling, tumbles,
            // smashing) shouldn't rack up drinks that then show as a "starting" tab. Tab still shows
            // there; nothing new accrues. Only actual levels (and the arena) count.
            if (SemiFunc.RunIsShop() || SemiFunc.RunIsLobby()) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            switch (kind)
            {
                case DrinkEvent.Flatlined:
                {
                    if (!PluginConfig.RuleEnabled(DrinkEvent.Flatlined) || subject == null) return;
                    int amount = PluginConfig.RuleAmount(DrinkEvent.Flatlined);
                    if (!_firstBloodUsed && PluginConfig.RuleEnabled(DrinkEvent.FirstBlood))
                    {
                        amount += PluginConfig.RuleAmount(DrinkEvent.FirstBlood);
                        _firstBloodUsed = true;
                    }
                    Personal(kind, subject, amount, $"{Name(subject)} flatlined.");
                    return;
                }

                case DrinkEvent.DownBad:
                    if (PluginConfig.RuleEnabled(kind) && subject != null && !OnCooldown(kind, subject))
                        Personal(kind, subject, PluginConfig.RuleAmount(kind), $"{Name(subject)} ate dirt.");
                    return;

                case DrinkEvent.InTheBag:
                    if (PluginConfig.RuleEnabled(kind) && subject != null && !OnCooldown(kind, subject))
                        Personal(kind, subject, PluginConfig.RuleAmount(kind), $"The headman's got {Name(subject)}.");
                    return;

                case DrinkEvent.WelcomeBack:
                    if (PluginConfig.RuleEnabled(kind) && subject != null)
                        Personal(kind, subject, PluginConfig.RuleAmount(kind), $"{Name(subject)} is back from the dead.");
                    return;

                case DrinkEvent.Butterfingers:
                {
                    if (!PluginConfig.RuleEnabled(kind) || subject == null || valueLost <= 0) return;
                    // One "Smashed a valuable" unit per DollarsPerSmashDrink of value lost. Minor bumps
                    // (below one threshold) don't count, so it's not spammy.
                    int per = Mathf.Max(1, PluginConfig.DollarsPerSmashDrink.Value);
                    int units = (valueLost / per) * Mathf.Max(1, PluginConfig.RuleAmount(kind));
                    if (units < 1) return;
                    if (OnCooldown(kind, subject)) return;   // one continuous smash isn't a drink per hit
                    Personal(kind, subject, units, $"{Name(subject)} smashed ${valueLost}.");
                    return;
                }

                case DrinkEvent.TaxmanCometh:
                {
                    if (!PluginConfig.RuleEnabled(kind)) return;
                    if (!PluginConfig.EffectiveEnemyEveryTime)
                    {
                        if (_enemySpawnedThisLevel) return;
                        _enemySpawnedThisLevel = true;
                    }
                    Team(kind, PluginConfig.RuleAmount(kind), false, "Something woke up. Everyone sips.");
                    return;
                }

                case DrinkEvent.CashOut:
                    if (PluginConfig.RuleEnabled(kind))
                        Team(kind, PluginConfig.RuleAmount(kind), false, "Haul secured. Cheers.");
                    return;

                case DrinkEvent.Surplus:
                    if (PluginConfig.RuleEnabled(kind))
                        Team(kind, PluginConfig.RuleAmount(kind), false, "Surplus! Drinks on the house.");
                    return;

                case DrinkEvent.QuotaMissed:
                    if (PluginConfig.RuleEnabled(kind))
                        Team(kind, 0, true, "Run's dead. Finish 'em.");
                    return;

                case DrinkEvent.Waterfall:
                    if (PluginConfig.RuleEnabled(kind))
                        Team(kind, PluginConfig.RuleAmount(kind), false, "Waterfall!");
                    return;
            }
        }

        // Player-on-player. Host-only, like OnEvent. The attacker is the one who drinks: a real hit on a
        // crewmate is a FriendlyFire drink, actually killing one is a Backstabber finish-your-drink.
        public static void OnBetrayal(PlayerAvatar attacker, PlayerAvatar victim, int damage, bool tumbled, bool killed)
        {
            if (!PluginConfig.Enabled.Value || !InRun()) return;
            if (SemiFunc.RunIsShop() || SemiFunc.RunIsLobby()) return;   // downtime jostling doesn't count
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            if (killed)
            {
                if (!PluginConfig.RuleEnabled(DrinkEvent.Backstabber)) return;
                var kill = new DrinkOrder { Kind = DrinkEvent.Backstabber, FinishDrink = true, Reason = $"{Name(attacker)} put down {Name(victim)}." };
                kill.Add(SteamID(attacker), Name(attacker));
                Dispatch(kill);
                DrunkTexts.ReactBetrayal(attacker);
                return;
            }

            if (!PluginConfig.RuleEnabled(DrinkEvent.FriendlyFire)) return;
            if (!tumbled && damage < 10) return;                 // a shove or chip damage isn't a drink
            if (OnCooldown(DrinkEvent.FriendlyFire, attacker)) return;
            int amount = PluginConfig.RuleAmount(DrinkEvent.FriendlyFire);
            if (amount <= 0) return;

            var order = new DrinkOrder { Kind = DrinkEvent.FriendlyFire, Amount = amount, Reason = $"{Name(attacker)} smacked {Name(victim)}." };
            order.Add(SteamID(attacker), Name(attacker));
            Dispatch(order);
            DrunkTexts.ReactBetrayal(attacker);
        }

        private static void Personal(DrinkEvent kind, PlayerAvatar subject, int amount, string reason)
        {
            if (amount <= 0) return;
            var order = new DrinkOrder { Kind = kind, Amount = amount, Reason = reason };
            order.Add(SteamID(subject), Name(subject));
            Dispatch(order);
            DrunkTexts.React(subject, kind);   // a drunk Semibot may blurt a reaction to what just happened
        }

        private static void Team(DrinkEvent kind, int amount, bool finishDrink, string reason)
        {
            var order = new DrinkOrder { Kind = kind, Amount = amount, FinishDrink = finishDrink, IsTeam = true, Reason = reason };
            foreach (var p in SemiFunc.PlayerGetAll())
                order.Add(SteamID(p), Name(p));
            Dispatch(order);
            DrunkTexts.React(null, kind);   // team moment - a random drunk Semibot voices it
        }

        // Host side: apply the order to our own screen, then broadcast it to everyone else.
        // Also the entry point for the dev test commands.
        public static void Dispatch(DrinkOrder order)
        {
            ApplyOrder(order);
            PourNet.SendOrder(order);
            PourDecisions.LogInfo($"[Order] sent {order.Kind} amount={order.Amount} team={order.IsTeam} targets={order.Targets.Count}");
        }

        // Runs on every client (host directly, clients from the received broadcast): tally, render,
        // and - host only - the Taxman's terminal reaction. Never re-broadcasts.
        public static void ApplyOrder(DrinkOrder order)
        {
            int units = order.FinishDrink ? 0 : order.Amount;
            foreach (var kv in order.Targets)
                BarTabStats.AddDrinks(kv.Key, kv.Value, order.Kind, units);

            DrinkHud.Instance?.ShowOrder(order);

            if (SemiFunc.IsMasterClientOrSingleplayer() && PluginConfig.TaxmanReactions.Value)
                TaxmanBartender.React(order);
        }

        private static string SteamID(PlayerAvatar p) => SemiFunc.PlayerGetSteamID(p);
        private static string Name(PlayerAvatar p) => string.IsNullOrEmpty(p.playerName) ? "Semibot" : p.playerName;
    }
}
