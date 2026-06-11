using PourDecisions.Configuration;
using PourDecisions.Models;
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

        // A live reaction for an event that happens in the truck. In-level events are handled by
        // the HUD; the terminal stays for bookkeeping.
        public static void React(DrinkOrder order)
        {
            if (!SemiFunc.RunIsLobby()) return;
            Post(Emoji(order.Kind) + ChaosTail());
        }

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

            for (int i = 0; i < board.Count; i++)
            {
                var tab = board[i].Value;
                string crown = i == 0 && board.Count > 1 ? " {fire}" : "";
                string name = Drunkify.Apply(tab.Name, proof, seed + i);
                Post($"{name} {Mood(tab.Total)} x{tab.Total}{crown}");
            }
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
