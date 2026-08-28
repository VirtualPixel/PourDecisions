using HarmonyLib;
using PourDecisions.Configuration;
using PourDecisions.Services;
using System.Collections.Generic;
using UnityEngine;

namespace PourDecisions.Patches
{
    // Tags each Semibot's floating name with their running drink tally, e.g. "Bilbo (12 sips)". The tab is
    // already host-authoritative and synced to every client by BarTabStats (the host broadcasts every order
    // and seeds late joiners with a snapshot), so every player sees everyone's count with no extra
    // networking - this is a pure local render.
    //
    // WorldSpaceUIPlayerName.Update never assigns text.text after the nametag is built (it only touches
    // colour and fontSize), so the suffix is ours to own. The finished string is cached per player and
    // only rebuilt when one of the four things it is made of moves, so the steady state is a dictionary
    // lookup and a reference compare - no garbage, and TMP isn't regenerating the mesh for a number that
    // barely changes. The cache is bounded by the lobby size and keyed by steamID, so nametags coming and
    // going with each level don't grow it.
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
            int owed = total <= 0 ? 0 : Mathf.Max(0, total - PourStats.Downed(id));
            string desired = Render(id, name, total, owed, PluginConfig.EffectiveUnitWord);

            if (text.text == desired) return;
            text.richText = true;   // the suffix carries a colour tag; the vanilla name is plain, so this is safe
            text.text = desired;
        }

        private struct Rendered
        {
            public string Name;
            public int Total;
            public int Owed;
            public string Unit;
            public string Text;
        }

        private static readonly Dictionary<string, Rendered> _cache = new();

        private static string Render(string id, string name, int total, int owed, string unit)
        {
            if (_cache.TryGetValue(id, out var r) && r.Total == total && r.Owed == owed
                && r.Name == name && r.Unit == unit)
                return r.Text;

            string text;
            if (total <= 0) text = name;
            // "(12 sips)" when caught up; "(12 sips, 4 owed)" with the debt in red when they're behind.
            else if (owed > 0)
                text = $"{name} <color=#e0a040>({total} {Plural(unit, total)}</color><color=#d05050>, {owed} owed</color><color=#e0a040>)</color>";
            else
                text = $"{name} <color=#e0a040>({total} {Plural(unit, total)})</color>";

            _cache[id] = new Rendered { Name = name, Total = total, Owed = owed, Unit = unit, Text = text };
            return text;
        }

        private static string Plural(string word, int count) => count == 1 ? word : word + "s";
    }
}
