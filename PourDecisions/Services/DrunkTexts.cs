using PourDecisions.Configuration;
using PourDecisions.Models;
using PourDecisions.UI;
using System.Collections.Generic;
using UnityEngine;
using Where = PourDecisions.Services.DrunkLines.Where;
using Theme = PourDecisions.Services.DrunkLines.Theme;
using ReactKind = PourDecisions.Services.DrunkLines.ReactKind;

namespace PourDecisions.Services
{
    // ENGINE for the drunk-text system. Decides WHEN a Semibot speaks; the WHAT (every line, every pool,
    // every selector) lives in DrunkLines.cs. Host-driven - only the master client runs this, builds each
    // message, and fans it out (truck terminal + on-screen feed + RPC to other clients).
    //
    // Two sources, one shared throttle:
    //   - Ambient (Tick): per-player timers; the drunker you are, the more often you ramble. Lines are
    //     context-aware - location (level/shop/truck), alive vs dead, biome, and live monsters/valuables.
    //   - Reactions (React): fired the instant something happens to a drunk Semibot (death, grab, smash...).
    //
    // The global MinGap throttle guarantees the feed can never flood no matter how much happens at once.
    internal static class DrunkTexts
    {
        // ---- frequency dials (turn these to taste) ----
        private const float MinGap = 10f;       // hard floor between PRIMARY lines, globally (replies bypass)
        private const float ReactChance = 0.45f; // odds a qualifying event actually produces a reaction
        private const float ReplyChance = 0.30f; // odds a line draws a crewmate clapback
        private const float ReplyDelayMin = 1.0f; // a clapback lands a beat after the line it answers
        private const float ReplyDelayMax = 2.6f;

        private static float _lastPrimary = -999f;
        private static bool TryClaimSlot()
        {
            if (Time.time - _lastPrimary < MinGap) return false;
            _lastPrimary = Time.time;
            return true;
        }

        // ---- per-level state ----
        private static readonly Dictionary<string, float> _nextTime = new();        // steamID -> next ambient line time
        private static readonly Dictionary<string[], HashSet<string>> _used = new(); // pool -> lines already used this level

        // Replies are deferred a beat (so they read as a reaction, not an echo). Computed at decision time,
        // broadcast when due. Drained every Tick; cleared on level change so none bleed across scenes.
        private struct PendingReply { public float At; public string Name; public string Line; }
        private static readonly List<PendingReply> _pendingReplies = new();

        public static void ResetLevel()
        {
            _used.Clear();
            _nextTime.Clear();
            _pendingReplies.Clear();
            _cartChecked = false;   // recheck for a cart in the new level before offering cart callouts
        }

        // Is there actually a cart in this level? Cart-directive lines ("get off the cart") read as nonsense
        // when there isn't one. One FindObjectOfType per level, cached - never per-frame (only hit when a
        // reply is being built, which is already throttled). Cleared in ResetLevel.
        private static bool _cartChecked;
        private static bool _cartPresent;
        private static bool CartPresent()
        {
            if (!_cartChecked)
            {
                _cartChecked = true;
                _cartPresent = Object.FindObjectOfType<PhysGrabCart>() != null;
            }
            return _cartPresent;
        }

        // ================= ambient =================

        // Host only. Call each frame; it self-throttles per player and globally.
        public static void Tick()
        {
            if (!PluginConfig.DrunkTexts.Value || !SemiFunc.IsMasterClientOrSingleplayer()) return;

            DrainReplies();   // fire any clapbacks whose delay has elapsed

            Where where;
            if (SemiFunc.RunIsShop()) where = Where.Shop;
            else if (SemiFunc.RunIsLobby()) where = Where.Truck;
            else if (SemiFunc.RunIsArena()) where = Where.Arena;
            else if (SemiFunc.RunIsLevel()) where = Where.Level;
            else return;   // menu / nowhere

            float now = Time.time;
            foreach (var p in SemiFunc.PlayerGetAll())
            {
                if (p == null) continue;
                string id = SemiFunc.PlayerGetSteamID(p);
                if (string.IsNullOrEmpty(id)) continue;

                int stage = DrunkLines.Stage(BarTabStats.TabFor(id));
                if (stage <= 0) continue;

                if (!_nextTime.TryGetValue(id, out float next)) { Schedule(id, now, stage); continue; }
                if (now < next) continue;
                if (!TryClaimSlot()) continue;   // someone else just spoke - wait out the gap, retry next frame

                Broadcast(NameOf(p), PickLine(p, stage, where));
                MaybeReply(p, NameOf(p), stage);
                Schedule(id, now, stage);
            }
        }

        private static void Schedule(string id, float now, int stage)
        {
            var range = DrunkLines.Interval(stage);
            _nextTime[id] = now + Random.Range(range.x, range.y);
        }

        // Living, in-level Semibots get a shot at a monster- or valuable-specific line - but ONLY for things
        // actually present right now, filled with the real display name. Then mod nods, then biome flavor,
        // then the generic stage pool. Dead players, the shop, and the truck always use their plain pools.
        private static string PickLine(PlayerAvatar p, int stage, Where where)
        {
            bool dead = p.isDisabled;

            // A rare nod to the mods actually installed (or a soft plug for ShrinkerGun when it isn't).
            if (!dead && Random.value < 0.08f) { var ml = ModLine(); if (ml != null) return ml; }

            // While someone wears the king-of-the-losers crown (vanilla SessionManager state), occasionally
            // bow to them - or brag, if you ARE the king. Regular levels only.
            if (!dead && where == Where.Level)
            {
                var king = SessionManager.instance != null ? SessionManager.instance.CrownedPlayerGet() : null;
                if (king != null && Random.value < 0.10f)
                {
                    bool isSelf = SemiFunc.PlayerGetSteamID(king) == SemiFunc.PlayerGetSteamID(p);
                    return string.Format(PickUnused(DrunkLines.KingPool(isSelf)), NameOf(king));
                }
            }

            if (!dead && where == Where.Level)
            {
                // Your run nemesis: occasionally gripe about whatever's killed you the most this run.
                var nem = BarTabStats.TopNemesis(SemiFunc.PlayerGetSteamID(p));
                if (nem != null && Random.value < 0.12f)
                    return string.Format(PickUnused(DrunkLines.NemesisPool(stage >= 3)), nem.Value.Name, nem.Value.Count);

                var enemies = PresentEnemies();
                var valuables = PresentValuables();
                bool wild = stage >= 3;

                float r = Random.value;
                if (enemies.Count > 0 && r < 0.30f)
                    return string.Format(PickUnused(DrunkLines.EnemyPool(wild)), enemies[Random.Range(0, enemies.Count)]);
                if (valuables.Count > 0 && r < 0.55f)
                    return string.Format(PickUnused(DrunkLines.ValuablePool(wild)), valuables[Random.Range(0, valuables.Count)]);

                var themePool = DrunkLines.ThemePool(CurrentTheme());
                if (themePool != null && r < 0.72f) return PickUnused(themePool);

                // Cart banter only when there's actually a cart in the level to ramble about.
                if (CartPresent() && r < 0.85f) return PickUnused(DrunkLines.CartLines);
            }

            // In the arena's RACE variant, living Semibots ramble about the vehicle dash instead of the
            // platform brawl. Dead/eliminated racers fall through to the shared eliminated pool.
            if (where == Where.Arena && !dead && ArenaRace.instance != null)
                return PickUnused(stage >= 3 ? DrunkLines.RaceWild : DrunkLines.RaceLines);

            return PickUnused(DrunkLines.Pool(stage, dead, where));
        }

        // ================= reactions =================

        // Event-driven (host-only; RuleEngine and the grab patches are already host-gated). Fires the
        // instant something happens TO a drunk Semibot - a reflex, not a timer.
        internal static void React(PlayerAvatar? subject, DrinkEvent kind)
        {
            switch (kind)
            {
                case DrinkEvent.Flatlined: ReactInternal(subject, ReactKind.Death); break;
                case DrinkEvent.DownBad: ReactInternal(subject, ReactKind.Down); break;
                case DrinkEvent.InTheBag: ReactInternal(subject, ReactKind.HeadmanGrab); break;
                case DrinkEvent.Butterfingers: ReactInternal(subject, ReactKind.Smash); break;
                case DrinkEvent.TaxmanCometh: ReactInternal(subject, ReactKind.Scared); break;
                case DrinkEvent.CashOut:
                case DrinkEvent.Surplus: ReactInternal(subject, ReactKind.Cheer); break;
            }
        }

        internal static void ReactHiddenGrab(PlayerAvatar subject) => ReactInternal(subject, ReactKind.HiddenGrab);

        // First-person reactions (the subject voices their own misfortune).
        internal static void ReactSpewerFace(PlayerAvatar subject) => ReactInternal(subject, ReactKind.SpewerFace);
        internal static void ReactShadow(PlayerAvatar subject) => ReactInternal(subject, ReactKind.Shadow);

        // A crewmate just hit or killed one of their own - someone else calls out the traitor by name.
        internal static void ReactBetrayal(PlayerAvatar traitor) => ReactAbout(traitor, ReactKind.Betrayal, includeDead: true, chance: 0.7f);

        // Reactions ABOUT a player, voiced by someone else (the lines are "{0}"-templated with the subject's
        // name). The spewer hosing the crew, and the whole king-of-the-losers arena layer.
        internal static void ReactSpewerPuke(PlayerAvatar faceHost) => ReactAbout(faceHost, ReactKind.SpewerPuke, includeDead: false, chance: ReactChance);
        internal static void ReactLastStanding(PlayerAvatar survivor) => ReactAbout(survivor, ReactKind.LastStanding, includeDead: true, chance: 0.9f);
        internal static void ReactCrownWin(PlayerAvatar winner) => ReactAbout(winner, ReactKind.CrownWin, includeDead: true, chance: 1f);
        internal static void ReactRaceWin(PlayerAvatar winner) => ReactAbout(winner, ReactKind.RaceWin, includeDead: true, chance: 1f);
        internal static void ReactRaceOut() => ReactGeneric(ReactKind.RaceOut, includeDead: true, chance: 0.6f);

        // An arena elimination - we don't get who fell from the RPC, so this is a generic (un-templated) jeer
        // voiced by any drunk Semibot (most are eliminated by now, so dead speakers count).
        internal static void ReactArenaDeath() => ReactGeneric(ReactKind.ArenaDeath, includeDead: true, chance: 0.6f);

        // A generic reaction with no subject - picks any drunk Semibot to voice an un-templated line.
        private static void ReactGeneric(ReactKind kind, bool includeDead, float chance)
        {
            if (!PluginConfig.DrunkTexts.Value || !SemiFunc.IsMasterClientOrSingleplayer()) return;

            PlayerAvatar? speaker = includeDead ? AnyDrunk() : RandomDrunkLiving();
            if (speaker == null) return;

            int stage = DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(speaker)));
            if (Random.value > chance) return;
            if (!TryClaimSlot()) return;

            Broadcast(NameOf(speaker), PickUnused(DrunkLines.ReactPool(kind, stage >= 3)));
        }

        // Any drunk Semibot, eliminated ones included (arena spectators still heckle).
        private static PlayerAvatar? AnyDrunk()
        {
            var list = new List<PlayerAvatar>();
            foreach (var p in SemiFunc.PlayerGetAll())
            {
                if (p == null) continue;
                if (DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(p))) >= 1) list.Add(p);
            }
            return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
        }

        // A drunk crewmate (not the subject, if avoidable) narrates what just happened to someone. In the
        // arena most Semibots are eliminated, so dead speakers are allowed there. Host-only.
        private static void ReactAbout(PlayerAvatar about, ReactKind kind, bool includeDead, float chance)
        {
            if (about == null || !PluginConfig.DrunkTexts.Value || !SemiFunc.IsMasterClientOrSingleplayer()) return;

            var speaker = PickSpeaker(about, includeDead);
            if (speaker == null) return;

            int stage = DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(speaker)));
            if (Random.value > chance) return;
            if (!TryClaimSlot()) return;

            string line = string.Format(PickUnused(DrunkLines.ReactPool(kind, stage >= 3)), NameOf(about));
            Broadcast(NameOf(speaker), line);
        }

        // A drunk (stage >= 1) Semibot to voice a line - preferring someone other than the subject. With
        // includeDead, eliminated/spectating Semibots can speak too (the arena, where most are down).
        private static PlayerAvatar? PickSpeaker(PlayerAvatar about, bool includeDead)
        {
            string aboutId = SemiFunc.PlayerGetSteamID(about);
            var others = new List<PlayerAvatar>();
            var any = new List<PlayerAvatar>();
            foreach (var p in SemiFunc.PlayerGetAll())
            {
                if (p == null) continue;
                if (!includeDead && p.isDisabled) continue;
                if (DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(p))) < 1) continue;
                any.Add(p);
                if (SemiFunc.PlayerGetSteamID(p) != aboutId) others.Add(p);
            }
            var pick = others.Count > 0 ? others : any;
            return pick.Count == 0 ? null : pick[Random.Range(0, pick.Count)];
        }

        private static void ReactInternal(PlayerAvatar? subject, ReactKind kind)
        {
            if (!PluginConfig.DrunkTexts.Value || !SemiFunc.IsMasterClientOrSingleplayer()) return;

            // Team moments (scared/cheer) have no subject - pick a drunk, living Semibot to voice it.
            var who = subject ?? RandomDrunkLiving();
            if (who == null) return;

            int stage = DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(who)));
            if (stage < 1) return;
            if (Random.value > ReactChance) return;   // a flavorful sometimes-thing, not every single event
            if (!TryClaimSlot()) return;              // and never past the global throttle

            // Death gets a cart/extraction-mod variant when one is present; otherwise the stage-tiered pool.
            string[] pool = (kind == ReactKind.Death && ModAware.CartOrExtraction && Random.value < 0.4f)
                ? DrunkLines.DeathExtraction
                : DrunkLines.ReactPool(kind, stage >= 3);

            Broadcast(NameOf(who), PickUnused(pool));
            MaybeReply(who, NameOf(who), stage);
        }

        private static PlayerAvatar? RandomDrunkLiving()
        {
            var list = new List<PlayerAvatar>();
            foreach (var p in SemiFunc.PlayerGetAll())
            {
                if (p == null || p.isDisabled) continue;
                if (DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(p))) >= 1) list.Add(p);
            }
            return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
        }

        // ================= delivery =================

        // Goes everywhere: the truck terminal (game RPC) AND the on-screen feed (host local + our RPC).
        private static void Broadcast(string name, string line)
        {
            // Unity null check on purpose: the screen's static instance is never cleared, so between
            // scenes it points at a destroyed object that ?. would happily call into.
            var screen = TruckScreenText.instance;
            if (screen != null) screen.MessageSendCustom(name, line, 0);
            DrinkHud.Instance?.AddDrunkFeed(name, line);
            PourNet.SendDrunkText(name, line);
        }

        // A less-intoxicated, living crewmate occasionally claps back at the unhinged ones. Bypasses the
        // global throttle on purpose: a reply is the immediate other half of an exchange.
        private static void MaybeReply(PlayerAvatar texter, string texterName, int stage)
        {
            if (stage < 2 || Random.value > ReplyChance) return;

            var candidates = new List<PlayerAvatar>();
            foreach (var q in SemiFunc.PlayerGetAll())
            {
                if (q == null || q == texter || q.isDisabled) continue;
                if (DrunkLines.Stage(BarTabStats.TabFor(SemiFunc.PlayerGetSteamID(q))) < stage) candidates.Add(q);
            }
            if (candidates.Count == 0) return;

            var replier = candidates[Random.Range(0, candidates.Count)];
            // When a cart's actually in the level, some clapbacks become cart callouts; otherwise stay general.
            string[] replyPool = (CartPresent() && Random.value < 0.25f) ? DrunkLines.CartReplies : DrunkLines.Replies;
            string line = string.Format(replyPool[Random.Range(0, replyPool.Length)], texterName);
            _pendingReplies.Add(new PendingReply
            {
                At = Time.time + Random.Range(ReplyDelayMin, ReplyDelayMax),
                Name = NameOf(replier),
                Line = line,
            });
        }

        // Broadcast any clapbacks whose beat has elapsed. Replies skip the global throttle on purpose - a
        // reply is the other half of an exchange - but the delay keeps a line + its reply from landing as one.
        private static void DrainReplies()
        {
            float now = Time.time;
            for (int i = _pendingReplies.Count - 1; i >= 0; i--)
            {
                if (now < _pendingReplies[i].At) continue;
                var r = _pendingReplies[i];
                _pendingReplies.RemoveAt(i);
                Broadcast(r.Name, r.Line);
            }
        }

        private static string NameOf(PlayerAvatar p) => string.IsNullOrEmpty(p.playerName) ? "Semibot" : p.playerName;

        // ================= live context (only read at the moment a line fires - never per frame) =================

        // Distinct display names of enemies currently spawned and active in the round.
        private static List<string> PresentEnemies()
        {
            var names = new List<string>();
            var dir = EnemyDirector.instance;
            if (dir == null || dir.enemiesSpawned == null) return names;
            foreach (var e in dir.enemiesSpawned)
            {
                if (e == null || !e.Spawned) continue;
                string n = e.enemyName;
                if (string.IsNullOrEmpty(n) || names.Contains(n)) continue;
                names.Add(n);
            }
            return names;
        }

        // Distinct display names of valuables currently in the level (cleaned from the object name).
        private static List<string> PresentValuables()
        {
            var names = new List<string>();
            var dir = ValuableDirector.instance;
            if (dir == null || dir.valuableList == null) return names;
            foreach (var v in dir.valuableList)
            {
                if (v == null) continue;
                string n = CleanValuableName(v.gameObject.name);
                if (string.IsNullOrEmpty(n) || names.Contains(n)) continue;
                names.Add(n);
            }
            return names;
        }

        // "Valuable Wizard Troll Finger(Clone)" -> "Wizard Troll Finger". Best-effort, display-friendly.
        private static string CleanValuableName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int clone = raw.IndexOf("(Clone)");
            if (clone >= 0) raw = raw.Substring(0, clone);
            raw = raw.Trim();
            if (raw.StartsWith("Valuable ")) raw = raw.Substring("Valuable ".Length);
            else if (raw.StartsWith("Valuable")) raw = raw.Substring("Valuable".Length);
            return raw.Trim();
        }

        // Recognize the current biome from its narrative name. Substring + graceful None fallback, so a map
        // we don't know just uses the generic level pools - never a wrong reference, never a crash.
        private static Theme CurrentTheme()
        {
            var lvl = RunManager.instance != null ? RunManager.instance.levelCurrent : null;
            string? n = lvl != null ? lvl.NarrativeName : null;
            if (string.IsNullOrEmpty(n)) return Theme.None;
            n = n.ToLowerInvariant();
            if (n.Contains("manor")) return Theme.Manor;
            if (n.Contains("wizard")) return Theme.Wizard;
            if (n.Contains("arctic") || n.Contains("snow") || n.Contains("ice")) return Theme.Arctic;
            return Theme.None;
        }

        // ================= mod-aware ambient =================

        private static List<string[]>? _modPools;       // cached: mods don't load mid-session
        private static List<string>? _unaccountedMods;  // installed mods we didn't hand-write a pool for
        private static string? ModLine()
        {
            _modPools ??= DrunkLines.ApplicableModPools();
            _unaccountedMods ??= ModAware.UnaccountedMods();

            // ~35% of mod nods go to a PROCEDURAL shout-out for an installed mod we didn't account for -
            // keeps it mod-aware even for your newest additions, without claiming what the mod does.
            if (_unaccountedMods.Count > 0 && Random.value < 0.35f)
                return string.Format(PickUnused(DrunkLines.ModProcedural), _unaccountedMods[Random.Range(0, _unaccountedMods.Count)]);

            if (_modPools.Count == 0) return null;
            return PickUnused(_modPools[Random.Range(0, _modPools.Count)]);
        }

        // ================= no-repeat picker =================

        // No line repeats within a pool for a whole level, across ALL players. When a pool is fully used it
        // clears and starts over. Keyed by the array reference, so every distinct pool tracks independently.
        private static string PickUnused(string[] pool)
        {
            if (!_used.TryGetValue(pool, out var used)) { used = new HashSet<string>(); _used[pool] = used; }
            if (used.Count >= pool.Length) used.Clear();

            for (int attempt = 0; attempt < 12; attempt++)
            {
                string candidate = pool[Random.Range(0, pool.Length)];
                if (used.Add(candidate)) return candidate;
            }
            string fresh = pool[Random.Range(0, pool.Length)];
            used.Add(fresh);
            return fresh;
        }
    }
}
