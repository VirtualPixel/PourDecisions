using PourDecisions.Configuration;
using UnityEngine;

namespace PourDecisions.Services
{
    // The team's drunkenness scalar, [0,1], derived from the shared run tab. Because it reads
    // BarTabStats (identical on every client) it's identical everywhere - so Drunkify produces
    // the same slurred terminal on all clients with nothing to network.
    internal static class ProofService
    {
        private static int _soberCredit;   // units the team has "slept off" across levels

        // Test-command override (`pd proof <0..1>`); null = compute normally.
        public static float? DebugOverride;

        public static float Proof
        {
            get
            {
                if (DebugOverride.HasValue) return Mathf.Clamp01(DebugOverride.Value);
                int threshold = Mathf.Max(1, PluginConfig.ProofThresholdValue);
                int net = BarTabStats.TeamRunUnits() - _soberCredit;
                return Mathf.Clamp01(net / (float)threshold);
            }
        }

        // A deterministic per-run seed so Drunkify slurs identically across clients. Derived from
        // shared state (sober credit + threshold), not from time or RNG.
        public static int RunSeed => (_soberCredit * 31) ^ (PluginConfig.ProofThresholdValue * 7);

        // Sober off a configurable amount each level.
        public static void SoberUp() => _soberCredit += PluginConfig.SoberUpPerLevel.Value;

        public static void Reset() => _soberCredit = 0;
    }
}
