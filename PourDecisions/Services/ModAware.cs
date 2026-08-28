using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;

namespace PourDecisions.Services
{
    // Detects which mods are loaded ON THE HOST - drunk texts are host-generated, so only the host's set
    // matters (clients never factor in). Substring match against the live BepInEx GUID registry, cached on
    // first use (mods don't load mid-session). A miss just means a line pool isn't offered - never a crash.
    //
    // To wire a mod's jokes: add a pool in DrunkLines + one line in DrunkLines.ApplicableModPools() using
    // ModAware.Has("guid-fragment"). Keep fragments lowercase-able and specific.
    internal static class ModAware
    {
        private static string[]? _guids;
        private static string[] Guids
        {
            get
            {
                if (_guids != null) return _guids;
                var keys = Chainloader.PluginInfos.Keys;
                var arr = new string[keys.Count];
                keys.CopyTo(arr, 0);
                _guids = arr;
                return arr;
            }
        }

        // True if any loaded plugin GUID contains any of these fragments (case-insensitive).
        internal static bool Has(params string[] fragments)
        {
            foreach (var g in Guids)
                foreach (var f in fragments)
                    if (g.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // Convenience group used by the line/reaction layer.
        internal static bool CartOrExtraction => Has("cart", "extraction", "confirmbutton");

        // Any mod that makes the run easier / cheatier - drives the self-aware "are we even playing" pool.
        internal static bool EasyMode => Has(
            "moreupgrades", "playerspeed", "noitemspawnlimit", "spawnlimit", "moreplayers",
            "sharedupgrades", "resellyourupgrades", "shopdiscount", "infinitestamina", "godmode");

        // Frameworks/libraries - not fun to shout out, so the procedural fallback skips them.
        private static readonly string[] _libraries =
        {
            "bepinex", "repolib", "menulib", "repoconfig", "keybindlib", "monodetour", "monomod",
            "hookgen", "odinserializer", "runtimedetour", "detourcontext", "asyncloggers", "soundapi",
            "configurationmanager", "unityexplorer",
        };

        // Loaded mods we DIDN'T hand-write a pool for (and that aren't libraries or us) - cleaned display
        // names for the procedural shout-out pool. Cached; lets newly-installed mods get referenced for
        // free, no code changes. Returns an empty list if everything loaded is already covered.
        private static List<string>? _unaccounted;
        internal static List<string> UnaccountedMods()
        {
            if (_unaccounted != null) return _unaccounted;
            var accounted = new List<string>(DrunkLines.AccountedFragments());
            var result = new List<string>();
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                string? guid = info?.Metadata?.GUID;
                if (string.IsNullOrEmpty(guid)) continue;
                if (guid!.IndexOf("pourdecisions", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (MatchesAny(guid, _libraries) || MatchesAny(guid, accounted)) continue;
                string name = CleanName(info!.Metadata.Name);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            _unaccounted = result;
            return result;
        }

        private static bool MatchesAny(string guid, IEnumerable<string> fragments)
        {
            foreach (var f in fragments)
                if (guid.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // "LobbyImprovements_REPO" -> "LobbyImprovements". Light, display-friendly cleanup.
        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            name = name.Replace("_", " ").Trim();
            if (name.EndsWith(" REPO", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 5).Trim();
            return name;
        }
    }
}
