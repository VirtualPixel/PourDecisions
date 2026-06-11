using PourDecisions.Configuration;
using PourDecisions.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PourDecisions.Services
{
    // Registers `pd ...` commands into the game's own dev console so every trigger is testable in
    // singleplayer - for yourself, for a synthetic "other" Semibot, or for everyone. Gated behind
    // the EnableTestCommands config and the console's debug-only flag, so players never see them.
    internal static class TestCommands
    {
        private const string TestId = "PD_TEST";
        private const string TestName = "Test Subject";

        private static readonly string[] Triggers =
            { "death", "revive", "damage", "smash", "tumble", "grab", "firstblood", "extract", "surplus", "wipe", "enemyspawn", "waterfall" };
        private static readonly string[] States =
            { "proof", "slur", "taxman", "recap", "splash", "streak", "tab", "career", "preset" };
        private static readonly string[] TargetWords = { "self", "other", "all" };

        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered || DebugCommandHandler.instance == null) return;
            _registered = true;

            // debugOnly:false so it also runs from normal chat as "/pd ...", not just the ` console.
            // Still gated by the EnableTestCommands config, so players never trip it.
            DebugCommandHandler.instance.Register(new DebugCommandHandler.ChatCommand(
                "pd",
                "Pour Decisions test triggers. e.g. 'pd death other', 'pd extract surplus', 'pd proof 0.8'.",
                Execute,
                Suggest,
                () => PluginConfig.EnableTestCommands.Value,
                debugOnly: false));

            PourDecisions.LogAlways("[Test] registered 'pd' dev-console command.");
        }

        private static void Execute(bool _, string[] args)
        {
            if (args.Length == 0) { Say("pd <trigger|state> ... - see suggestions"); return; }
            string cmd = args[0].ToLowerInvariant();
            string a1 = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            int n = args.Length > 2 && int.TryParse(args[2], out var v) ? v : -1;

            switch (cmd)
            {
                case "death": Personal(DrinkEvent.Flatlined, a1, n, "{0} flatlined."); break;
                case "revive": Personal(DrinkEvent.WelcomeBack, a1, n, "{0} is back from the dead."); break;
                case "damage": Personal(DrinkEvent.DownBad, a1, n, "{0} took a hit."); break;
                case "smash": Personal(DrinkEvent.Butterfingers, a1, n < 0 ? 2 : n, "{0} smashed something."); break;
                case "tumble": Personal(DrinkEvent.DownBad, a1, n, "{0} ate dirt."); break;
                case "grab": Personal(DrinkEvent.InTheBag, a1, n, "The headman's got {0}."); break;
                case "firstblood": Personal(DrinkEvent.Flatlined, a1, n, "{0} drew first blood."); break;

                case "extract": Team(DrinkEvent.CashOut, n, false, "Haul secured. Cheers."); break;
                case "surplus": Team(DrinkEvent.Surplus, n, false, "Surplus! Drinks on the house."); break;
                case "wipe": Team(DrinkEvent.QuotaMissed, 0, true, "Run's dead. Finish 'em."); break;
                case "enemyspawn": Team(DrinkEvent.TaxmanCometh, n, false, "Something woke up."); break;
                case "waterfall": Team(DrinkEvent.Waterfall, n, false, "Waterfall!"); break;

                case "proof":
                    if (a1 == "reset") { ProofService.DebugOverride = null; Say("Proof: auto"); }
                    else if (float.TryParse(a1, out var p)) { ProofService.DebugOverride = p; Say($"Proof forced to {p:0.00}"); }
                    break;
                case "slur":
                    string sample = string.Join(" ", args.Skip(1));
                    if (sample.Length == 0) sample = "Extraction complete. Get to the truck.";
                    Say(Drunkify.Apply(sample, ProofService.Proof, ProofService.RunSeed));
                    break;
                case "taxman": TaxmanBartender.TestSay(a1.Length == 0 ? "chaos" : a1); break;
                case "recap": TaxmanBartender.Recap(); break;
                case "splash":
                    if (a1 == "excessive")
                        UI.UsageSplash.ShowForced("WELLNESS CHECK",
                            $"Company records show {Math.Max(3, BarTabStats.ConsecutiveDayStreak())} shifts in a row on the bottle. Management recommends a night off. The quota will keep.", true);
                    else
                        UI.UsageSplash.ShowForced("SAFETY NOTICE",
                            "Operating salvage equipment while intoxicated is your liability, not the company's. Pour Decisions is active - pace yourselves and mind your crew.", false);
                    break;
                case "streak": Say($"Play-day streak: {BarTabStats.ConsecutiveDayStreak()} (career units {BarTabStats.CareerUnits})"); break;
                case "tab":
                    if (a1 == "clear") { BarTabStats.ResetRun(); Say("Run tab cleared."); }
                    else Say(LeaderboardText());
                    break;
                case "career": Say($"Career: {BarTabStats.CareerUnits} units over {BarTabStats.CareerRuns} runs (record {BarTabStats.CareerRecord})"); break;
                case "preset": if (a1.Length > 0) { PluginConfig.Ruleset.Value = TitleCase(args[1]); Say($"Preset: {PluginConfig.Ruleset.Value}"); } break;

                default: Say($"unknown: {cmd}"); break;
            }
        }

        private static void Personal(DrinkEvent kind, string target, int n, string reasonFmt)
        {
            var targets = Resolve(target);
            if (targets.Count == 0) return;
            int amount = n >= 0 ? n : Math.Max(1, PluginConfig.RuleAmount(kind));
            var order = new DrinkOrder { Kind = kind, Amount = amount, Reason = string.Format(reasonFmt, targets[0].Value) };
            foreach (var t in targets) order.Add(t.Key, t.Value);
            RuleEngine.Dispatch(order);
        }

        private static void Team(DrinkEvent kind, int n, bool finish, string reason)
        {
            int amount = finish ? 0 : (n >= 0 ? n : Math.Max(1, PluginConfig.RuleAmount(kind)));
            var order = new DrinkOrder { Kind = kind, Amount = amount, FinishDrink = finish, IsTeam = true, Reason = reason };
            foreach (var t in AllTargets()) order.Add(t.Key, t.Value);
            RuleEngine.Dispatch(order);
        }

        private static List<KeyValuePair<string, string>> Resolve(string target)
        {
            switch (target)
            {
                case "other": return new() { new(TestId, TestName) };
                case "all": return AllTargets();
                default:
                    var local = SemiFunc.PlayerAvatarLocal();
                    return local != null
                        ? new() { new(SemiFunc.PlayerGetSteamID(local), Display(local)) }
                        : new();
            }
        }

        private static List<KeyValuePair<string, string>> AllTargets()
        {
            var list = SemiFunc.PlayerGetAll()
                .Select(p => new KeyValuePair<string, string>(SemiFunc.PlayerGetSteamID(p), Display(p)))
                .ToList();
            list.Add(new(TestId, TestName));   // synthetic row so SP shows a multi-player leaderboard
            return list;
        }

        private static string Display(PlayerAvatar p) => string.IsNullOrEmpty(p.playerName) ? "Semibot" : p.playerName;

        private static string LeaderboardText()
        {
            var board = BarTabStats.Leaderboard();
            if (board.Count == 0) return "Tab is empty.";
            return string.Join("   ", board.Select(kv => $"{kv.Value.Name}:{kv.Value.Total}"));
        }

        private static string TitleCase(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static void Say(string msg)
        {
            PourDecisions.LogAlways("[pd] " + msg);
            if (BigMessageUI.instance != null)
                BigMessageUI.instance.BigMessage(msg, "", 50f, new UnityEngine.Color(0.9f, 0.9f, 0.9f), UnityEngine.Color.white);
        }

        private static List<string> Suggest(bool _, string partial, string[] args)
        {
            // arg0 = trigger/state; arg1 = target (for triggers).
            if (args.Length <= 1)
            {
                var all = Triggers.Concat(States);
                return all.Where(s => s.StartsWith(partial ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (args.Length == 2 && Triggers.Contains(args[0].ToLowerInvariant()))
                return TargetWords.Where(s => s.StartsWith(partial ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            return new List<string>();
        }
    }
}
