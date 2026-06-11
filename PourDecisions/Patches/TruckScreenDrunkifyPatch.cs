using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // Slurs the truck terminal as Proof rises - vanilla Taxman dialogue included. After the screen
    // composes its lines we rewrite each TextLine.text through the tag-aware Drunkify, so the
    // typewriter then reveals the slurred line while [haul]/[goal] tokens and <sprite>/<color> tags
    // pass through untouched and still substitute correctly.
    [HarmonyPatch(typeof(TruckScreenText), "InitializeTextTypingLogic")]
    internal static class TruckScreenDrunkifyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TruckScreenText __instance)
        {
            if (!PluginConfig.DrunkTerminal.Value || !PluginConfig.Enabled.Value) return;

            float proof = ProofService.Proof;
            if (proof <= 0f || __instance.pages == null) return;

            int seed = ProofService.RunSeed;
            foreach (var page in __instance.pages)
            {
                if (page?.textLines == null) continue;
                foreach (var line in page.textLines)
                {
                    if (line != null && !string.IsNullOrEmpty(line.text))
                        line.text = Drunkify.Apply(line.text, proof, seed);
                }
            }
        }
    }
}
