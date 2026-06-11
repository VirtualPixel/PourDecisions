using System.Collections.Generic;

namespace PourDecisions.Models
{
    // A resolved "who drinks, how much, why". The HOST builds it, applies it locally, and broadcasts
    // it to every client via a real [PunRPC] (PourNet); clients render + tally from the received order.
    internal sealed class DrinkOrder
    {
        public DrinkEvent Kind;
        public string Reason = string.Empty;          // player-facing line, e.g. "{name} flatlined."
        public int Amount;                            // drink units owed (0 when FinishDrink is set)
        public bool FinishDrink;                      // team wipe - "finish 'em"
        public bool IsTeam;                           // everyone drinks (toast / wipe)

        // steamID -> display name for each target.
        public readonly Dictionary<string, string> Targets = new();

        public DrinkOrder Add(string steamID, string name)
        {
            if (!string.IsNullOrEmpty(steamID)) Targets[steamID] = name ?? steamID;
            return this;
        }
    }
}
