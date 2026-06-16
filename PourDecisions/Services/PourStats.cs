using StatsCore;

namespace PourDecisions.Services
{
    // The networked "downed" count: how many drinks each player has actually CONFIRMED this run. BarTabStats
    // tracks the local player's confirmations, but only locally; this publishes them to the whole lobby
    // through StatsCore's client-authoritative sync, so everyone's nametag and the Taxman can tell who's
    // keeping up from who's stacking up a debt. Each client owns its own slot and StatsCore rejects anyone
    // trying to write another player's. Run-scoped, so it clears with the tab when a new run starts.
    internal static class PourStats
    {
        public const string K_Downed = "vippy.pourdecisions.downed";
        private static Stat? _downed;

        public static void Init() =>
            _downed ??= Stats.Define(K_Downed, StatScope.PerPlayer, StatLifetime.Run).ClientAuthoritative();

        // Push the local player's confirmed-drink count to everyone.
        public static void PublishLocalDowned(int count) => _downed?.Set(BarTabStats.LocalSteamID(), count);

        // How many drinks a given player has confirmed (0 if unknown or not yet synced).
        public static int Downed(string steamID) => _downed != null ? _downed.Get(steamID) : 0;
    }
}
