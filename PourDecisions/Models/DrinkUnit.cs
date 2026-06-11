namespace PourDecisions.Models
{
    // The sip/drink/shot economy. Weights are the defaults; the actual amounts come from config.
    // Amounts render as plain numbers - the game's pixel font has no emoji glyphs, so unicode pips
    // would show up as tofu squares.
    internal static class DrinkUnit
    {
        public const int Sip = 1;
        public const int Drink = 2;
        public const int Shot = 3;
    }
}
