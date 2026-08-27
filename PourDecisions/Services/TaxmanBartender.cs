using Photon.Pun;
using PourDecisions.Configuration;
using PourDecisions.Models;
using StatsCore;
using System.Collections.Generic;
using System.Text;

namespace PourDecisions.Services
{
    // The Taxman runs the tab. He speaks only in emoji (his established voice), posting to the
    // truck terminal through the vanilla MessageSendCustom RPC. Emoji names are all confirmed
    // present in the game's sprite asset (read out of ShopKeeper/TruckScreenText/ExtractionPoint).
    // Plain text (recap names) is Drunkified; {emoji} tokens are tag-skipped by Drunkify.
    internal static class TaxmanBartender
    {
        private const string Taxman = "Taxman";

        private static string Emoji(DrinkEvent kind) => kind switch
        {
            DrinkEvent.Flatlined    => "{skull}{:(}",
            DrinkEvent.QuotaMissed  => "{skull}{deathhead}{:'(}",
            DrinkEvent.DownBad      => "{fedup}{thumbsdown}",
            DrinkEvent.InTheBag     => "{weird_eye}{handcuffs}",
            DrinkEvent.Butterfingers=> "{heartbreak}{:o}",
            DrinkEvent.TaxmanCometh => "{weird_eye}{!}",
            DrinkEvent.CashOut      => "{$$}{:D}",
            DrinkEvent.Surplus      => "{$$}{sparkle}{heart}",
            DrinkEvent.WelcomeBack  => "{heart}{:)}",
            DrinkEvent.Waterfall    => "{:P}{:D}{eyes}",
            DrinkEvent.FriendlyFire => "{fedup}{thumbsdown}",
            DrinkEvent.Backstabber  => "{skull}{weird_eye}",
            _ => "{eyes}",
        };

        private static string ChaosTail()
        {
            // Appended as Proof climbs - the Taxman's had a few too.
            float p = ProofService.Proof;
            if (p > 0.75f) return "{weird_eye}{:o}{fedup}";
            if (p > 0.45f) return "{dry}";
            return "";
        }

        // A live reaction on whatever truck screen is around. Orders are only ever made in a level or
        // the arena (RuleEngine bails in the shop and the truck), so gating this on the truck meant it
        // never ran. The screen drops posts while it is typing, which is fine for a reaction.
        public static void React(DrinkOrder order) => Post(Emoji(order.Kind) + ChaosTail());

        // The per-Semibot tab, read out on the terminal. Host-only; call when arriving at the truck.
        public static void Recap()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer() || !PluginConfig.TaxmanReactions.Value) return;
            var board = BarTabStats.Leaderboard();
            if (board.Count == 0) return;

            int seed = ProofService.RunSeed;
            float proof = PluginConfig.DrunkTerminal.Value ? ProofService.Proof : 0f;

            var sb = new StringBuilder();
            sb.Append("{register} ").Append(Drunkify.Apply("the tab", proof, seed)).Append(' ').Append("{register}");
            Post(sb.ToString());

            string? slacker = null; int worstOwed = 0;
            for (int i = 0; i < board.Count; i++)
            {
                var tab = board[i].Value;
                string crown = i == 0 && board.Count > 1 ? " {fire}" : "";
                string name = Drunkify.Apply(tab.Name, proof, seed + i);
                Post($"{name} {Mood(tab.Total)} x{tab.Total}{crown}");

                int owed = tab.Total - PourStats.Downed(board[i].Key);
                if (owed > worstOwed) { worstOwed = owed; slacker = tab.Name; }
            }

            // Name and shame whoever's furthest behind on actually drinking what they owe.
            if (slacker != null && worstOwed >= 3)
                Post($"{Drunkify.Apply(slacker, proof, seed + 91)} {{fedup}}{{!}} owes {worstOwed}");

            HallOfShame(proof, seed);   // once per run: career body count pulled from the whole crew
        }

        // Career standings across the actual crew, pulled live from every client via StatsCore's host-pull.
        // Career stats live only on each player's own machine, so this is the only way to read the room's
        // totals at once. Once per run, multiplayer only, host only. The callback posts when everyone replies.
        private static bool _hallShown;
        public static void ResetRun() => _hallShown = false;

        private static void HallOfShame(float proof, int seed)
        {
            if (_hallShown || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount < 2) return;
            _hallShown = true;

            Stats.RequestFromClients(GameStats.K_Deaths, byPlayer =>
            {
                var ranked = new List<KeyValuePair<string, int>>(byPlayer);
                ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
                if (ranked.Count == 0 || ranked[0].Value <= 0) return;

                Post("{register} " + Drunkify.Apply("career body count", proof, seed + 200) + " {register}");
                int shown = 0;
                foreach (var entry in ranked)
                {
                    if (entry.Value <= 0 || shown >= 4) break;
                    Post($"{Drunkify.Apply(NameForSteamID(entry.Key), proof, seed + 201 + shown)} {{skull}} x{entry.Value}");
                    shown++;
                }
            });
        }

        private static string NameForSteamID(string steamID)
        {
            var avatar = SemiFunc.PlayerAvatarGetFromSteamID(steamID);
            string name = avatar != null ? avatar.playerName : "";
            return string.IsNullOrEmpty(name) ? "Semibot" : name;
        }

        private static string Mood(int total) => total >= 12 ? "{skull}" : total >= 6 ? "{fedup}" : total >= 1 ? "{mellow}" : "{:)}";

        // Test command `pd taxman <mood>` - posts an emoji telegram ignoring the scene gate.
        public static void TestSay(string mood) => Post(mood switch
        {
            "cheers" => "{$$}{:D}{sparkle}",
            "drinkup" => "{skull}{:(}{fedup}",
            "chaos" => "{weird_eye}{:o}{fedup}{bang}{dry}",
            _ => "{eyes}{?}",
        });

        private static void Post(string message)
        {
            var screen = TruckScreenText.instance;
            if (screen == null) return;
            screen.MessageSendCustom(Taxman, message, 0);
        }
    }
}
