namespace PourDecisions.Models
{
    // Every trigger the rule engine understands. Order is also the leaderboard/recap order.
    public enum DrinkEvent
    {
        Flatlined,      // a Semibot died
        FirstBlood,     // first death of the run (bonus, stacks on Flatlined)
        DownBad,        // tumbled/ragdolled by something
        InTheBag,       // grabbed by the head-grabber enemy
        Butterfingers,  // smashed a valuable (amount scales with $ lost)
        TaxmanCometh,   // an enemy woke up / spawned
        CashOut,        // extraction reached the goal (team toast)
        Surplus,        // extraction went over the goal (bigger toast)
        QuotaMissed,    // whole team wiped (finish your drink)
        WelcomeBack,    // revived
        Waterfall,      // level-start ceremony
        FriendlyFire,   // you hit a crewmate (or knocked them over)
        Backstabber     // you KILLED a crewmate (finish your drink)
    }
}
