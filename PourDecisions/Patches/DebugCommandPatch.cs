using HarmonyLib;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // Registers the `pd ...` test commands into the game's own dev console once it's up. The command
    // is always registered but only runs while EnableTestCommands is on (its IsEnabled gate), so it
    // stays invisible to players and toggles live.
    [HarmonyPatch(typeof(DebugCommandHandler), "Awake")]
    internal static class DebugCommandPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => TestCommands.RegisterAll();
    }
}
