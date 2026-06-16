using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;

namespace PourDecisions.Patches
{
    // Tags each Semibot's floating name with their running drink tally, e.g. "Bilbo (12 sips)". The tab is
    // already host-authoritative and synced to every client by BarTabStats (the host broadcasts every order
    // and seeds late joiners with a snapshot), so every player sees everyone's count with no extra
    // networking - this is a pure local render.
    //
    // WorldSpaceUIPlayerName.Update never assigns text.text after the nametag is built (it only touches
    // colour and fontSize), so the suffix is ours to own. We rebuild the base name from playerAvatar every
    // frame (cheap) but only assign when the string actually changes, so TMP isn't regenerating the mesh
    // every frame for a number that rarely moves.
    [HarmonyPatch(typeof(WorldSpaceUIPlayerName), "Update")]
    internal static class NameTagPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WorldSpaceUIPlayerName __instance)
        {
            var text = __instance.text;
            var pa = __instance.playerAvatar;
            // The original Update destroys the GameObject and returns when playerAvatar is gone; the postfix
            // still runs, so guard both before touching them.
            if (text == null || pa == null) return;

            string name = pa.playerName;
            if (string.IsNullOrEmpty(name)) return;   // name not assigned yet; leave the vanilla value alone

            // Feature (or the whole mod) off: make sure we're not leaving a stale suffix behind.
            if (!PluginConfig.Enabled.Value || !PluginConfig.NameTagDrinks.Value)
            {
                if (text.text != name) text.text = name;
                return;
            }

            string id = SemiFunc.PlayerGetSteamID(pa);
            int total = BarTabStats.TabFor(id);
            string desired;
            if (total <= 0)
            {
                desired = name;
            }
            else
            {
                int owed = total - PourStats.Downed(id);
                if (owed < 0) owed = 0;
                // "(12 sips)" when caught up; "(12 sips, 4 owed)" with the debt in red when they're behind.
                desired = owed > 0
                    ? $"{name} <color=#e0a040>({total} {Unit(total)}</color><color=#d05050>, {owed} owed</color><color=#e0a040>)</color>"
                    : $"{name} <color=#e0a040>({total} {Unit(total)})</color>";
            }

            if (text.text == desired) return;
            text.richText = true;   // the suffix carries a colour tag; the vanilla name is plain, so this is safe
            text.text = desired;
        }

        private static string Unit(int count)
        {
            string word = PluginConfig.EffectiveUnitWord;
            return count == 1 ? word : word + "s";
        }
    }
}
