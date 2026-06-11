using PourDecisions.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PourDecisions.Services
{
    // Tracks who's had what. Keyed by steamID like StatsManager, but written to our own file, never
    // the game save. The run tab comes straight from deterministic vanilla events, so it lines up on
    // every client without us networking anything. The host pushes a small snapshot to seed late joiners.
    internal static class BarTabStats
    {
        internal sealed class Tab
        {
            public string Name = "";
            public int Total;     // drinks assigned (host-authoritative, synced via orders)
            public int Drunk;     // drinks the player has confirmed (local-only acknowledgment)
            public readonly Dictionary<DrinkEvent, int> ByKind = new();
        }

        // steamID -> tab for this run.
        private static readonly Dictionary<string, Tab> _run = new();

        // Career (this machine, the local player).
        private static int _careerUnits;
        private static int _careerRuns;
        private static int _careerRecord;       // biggest single-run tab
        private static readonly List<string> _playDays = new();  // distinct yyyy-MM-dd, sorted ascending
        private static bool _careerLoaded;

        private static string CareerPath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "PourDecisions", "career.txt");

        // Per game-save, per-person drink totals. The HOST owns the save, so it records every crewmate's
        // accumulated drinks into the loaded save file - so reloading a save remembers who's had what.
        internal sealed class SaveEntry { public int Drinks; public string Name = ""; }
        private static readonly Dictionary<string, Dictionary<string, SaveEntry>> _saveLedger = new();
        private static bool _savesLoaded;
        private static string SavesPath => Path.Combine(BepInEx.Paths.ConfigPath, "PourDecisions", "saves.txt");
        private static string CurrentSaveFile() => StatsManager.instance != null ? StatsManager.instance.saveFileCurrent ?? "" : "";

        // Drinks the LOCAL player has actually TAKEN (confirmed), persisted per save file, with an 8-hour
        // idle reset: if it's been 8h since your last drink, the tally starts fresh - a new sitting. Local
        // only; each machine tracks its own player. (Other players' taken counts aren't known here - that
        // would need PourDecisions to network confirmations.)
        private const long TakenResetSeconds = 8 * 60 * 60;
        private sealed class TakenEntry { public int Taken; public long LastUtc; }
        private static readonly Dictionary<string, TakenEntry> _taken = new();   // save file -> tally
        private static bool _takenLoaded;
        private static string TakenPath => Path.Combine(BepInEx.Paths.ConfigPath, "PourDecisions", "taken.txt");
        private static long NowUtc() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ---- run tab ----

        public static void AddDrinks(string steamID, string name, DrinkEvent kind, int units)
        {
            if (string.IsNullOrEmpty(steamID) || units <= 0) return;

            if (!_run.TryGetValue(steamID, out var tab))
            {
                tab = new Tab { Name = name };
                _run[steamID] = tab;
            }
            if (!string.IsNullOrEmpty(name)) tab.Name = name;
            tab.Total += units;
            tab.ByKind[kind] = (tab.ByKind.TryGetValue(kind, out var n) ? n : 0) + units;

            if (steamID == LocalSteamID()) _careerUnits += units;
        }

        public static int TeamRunUnits()
        {
            int sum = 0;
            foreach (var t in _run.Values) sum += t.Total;
            return sum;
        }

        public static int TabFor(string steamID) => _run.TryGetValue(steamID, out var t) ? t.Total : 0;

        // ---- confirm-a-drink (local player) ----

        public static int DrunkFor(string steamID) => _run.TryGetValue(steamID, out var t) ? t.Drunk : 0;
        public static int LocalDrunk() => DrunkFor(LocalSteamID());

        public static int LocalOwed()
        {
            if (!_run.TryGetValue(LocalSteamID(), out var t)) return 0;
            int owed = t.Total - t.Drunk;
            return owed > 0 ? owed : 0;
        }

        // Mark one owed drink as taken. Returns true if there was something to confirm.
        public static bool ConfirmOne()
        {
            if (!_run.TryGetValue(LocalSteamID(), out var t)) return false;
            if (t.Drunk >= t.Total) return false;
            t.Drunk++;
            RecordDrinkTaken();   // bank it into the per-save sitting tally (resets after 8h idle)
            return true;
        }

        // The local player's drinks taken in the current save's sitting (resets after 8h with no drink).
        public static int SittingDrinksTaken() => CurrentTaken().Taken;

        // Fetch the current save's taken tally, applying the 8-hour idle reset first.
        private static TakenEntry CurrentTaken()
        {
            EnsureTakenLoaded();
            string save = CurrentSaveFile();
            if (save.Length == 0) save = "_local";   // no save id (e.g. a client) - use a machine-local bucket
            if (!_taken.TryGetValue(save, out var e)) { e = new TakenEntry(); _taken[save] = e; }
            if (e.Taken > 0 && e.LastUtc > 0 && NowUtc() - e.LastUtc >= TakenResetSeconds)
            {
                e.Taken = 0;       // 8h since the last drink - fresh sitting
                SaveTaken();
            }
            return e;
        }

        private static void RecordDrinkTaken()
        {
            var e = CurrentTaken();
            e.Taken++;
            e.LastUtc = NowUtc();
            SaveTaken();
        }

        // Leaderboard, heaviest tab first.
        public static List<KeyValuePair<string, Tab>> Leaderboard() =>
            _run.OrderByDescending(kv => kv.Value.Total).ToList();

        public static void ResetRun()
        {
            int biggest = _run.Values.Count == 0 ? 0 : _run.Values.Max(t => t.Total);
            if (biggest > _careerRecord) _careerRecord = biggest;
            if (_run.Count > 0) { _careerRuns++; Save(); }
            FoldRunIntoSave();   // bank this run's drinks into the current save file (host only)
            _run.Clear();
        }

        // ---- per-save, per-person ledger ----

        // Add the run's per-player totals to the loaded save file. Host-only (it owns the save); other
        // machines may not have a save file id and shouldn't write a partial record.
        private static void FoldRunIntoSave()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer() || _run.Count == 0) return;
            string save = CurrentSaveFile();
            if (string.IsNullOrEmpty(save)) return;

            EnsureSavesLoaded();
            if (!_saveLedger.TryGetValue(save, out var book)) { book = new Dictionary<string, SaveEntry>(); _saveLedger[save] = book; }
            foreach (var kv in _run)
            {
                if (!book.TryGetValue(kv.Key, out var e)) { e = new SaveEntry(); book[kv.Key] = e; }
                e.Drinks += kv.Value.Total;
                if (!string.IsNullOrEmpty(kv.Value.Name)) e.Name = kv.Value.Name;
            }
            SaveSaves();
        }

        // This save's banked total for a player (NOT including the current in-progress run - add TabFor for that).
        public static int SaveDrinksFor(string steamID)
        {
            EnsureSavesLoaded();
            string save = CurrentSaveFile();
            return save.Length > 0 && _saveLedger.TryGetValue(save, out var book) && book.TryGetValue(steamID, out var e) ? e.Drinks : 0;
        }

        // This save's all-time drink leaderboard, heaviest first.
        public static List<KeyValuePair<string, SaveEntry>> SaveLeaderboard()
        {
            EnsureSavesLoaded();
            string save = CurrentSaveFile();
            if (save.Length == 0 || !_saveLedger.TryGetValue(save, out var book)) return new List<KeyValuePair<string, SaveEntry>>();
            return book.OrderByDescending(kv => kv.Value.Drinks).ToList();
        }

        // ---- late-join snapshot (host -> room property -> joiner) ----

        public static string SerializeRun()
        {
            var sb = new StringBuilder();
            foreach (var kv in _run)
                sb.Append(kv.Key).Append('=').Append(kv.Value.Total).Append('~').Append(Escape(kv.Value.Name)).Append(';');
            return sb.ToString();
        }

        public static void ApplyRunSnapshot(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            foreach (var entry in data.Split(';'))
            {
                if (entry.Length == 0) continue;
                int eq = entry.IndexOf('='); int tl = entry.IndexOf('~');
                if (eq < 0 || tl < eq) continue;
                string steamID = entry.Substring(0, eq);
                if (!int.TryParse(entry.Substring(eq + 1, tl - eq - 1), out int total)) continue;
                string name = Unescape(entry.Substring(tl + 1));
                // Preserve any drinks already confirmed locally (guards a re-applied snapshot, e.g.
                // host migration) so a player isn't told to re-drink what they've taken.
                int drunk = _run.TryGetValue(steamID, out var existing) ? existing.Drunk : 0;
                _run[steamID] = new Tab { Name = name, Total = total, Drunk = drunk };
            }
        }

        private static string Escape(string s) => s.Replace("~", "").Replace(";", "").Replace("=", "").Replace("|", "");
        private static string Unescape(string s) => s;

        // ---- career + play-date streak ----

        public static void EnsureLoaded() { if (!_careerLoaded) Load(); }

        public static int CareerUnits { get { EnsureLoaded(); return _careerUnits; } }
        public static int CareerRuns { get { EnsureLoaded(); return _careerRuns; } }
        public static int CareerRecord { get { EnsureLoaded(); return _careerRecord; } }

        // Records today as a play day. Returns the consecutive-day streak ending today.
        public static int RecordLaunchDay()
        {
            EnsureLoaded();
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_playDays.Count == 0 || _playDays[_playDays.Count - 1] != today)
            {
                _playDays.Add(today);
                if (_playDays.Count > 60) _playDays.RemoveRange(0, _playDays.Count - 60);
                Save();
            }
            return ConsecutiveDayStreak();
        }

        public static int ConsecutiveDayStreak()
        {
            EnsureLoaded();
            if (_playDays.Count == 0) return 0;

            int streak = 1;
            for (int i = _playDays.Count - 1; i > 0; i--)
            {
                if (!DateTime.TryParse(_playDays[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var cur) ||
                    !DateTime.TryParse(_playDays[i - 1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var prev))
                    break;
                if ((cur.Date - prev.Date).Days == 1) streak++;
                else break;
            }
            return streak;
        }

        private static void Load()
        {
            _careerLoaded = true;
            try
            {
                if (!File.Exists(CareerPath)) return;
                foreach (var line in File.ReadAllLines(CareerPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "units": int.TryParse(val, out _careerUnits); break;
                        case "runs": int.TryParse(val, out _careerRuns); break;
                        case "record": int.TryParse(val, out _careerRecord); break;
                        case "days":
                            _playDays.Clear();
                            _playDays.AddRange(val.Split(',').Where(d => d.Length > 0));
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                PourDecisions.Logger.LogWarning($"[Stats] couldn't read career file: {e.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CareerPath)!);
                var sb = new StringBuilder();
                sb.Append("units=").Append(_careerUnits).Append('\n');
                sb.Append("runs=").Append(_careerRuns).Append('\n');
                sb.Append("record=").Append(_careerRecord).Append('\n');
                sb.Append("days=").Append(string.Join(",", _playDays)).Append('\n');
                File.WriteAllText(CareerPath, sb.ToString());
            }
            catch (Exception e)
            {
                PourDecisions.Logger.LogWarning($"[Stats] couldn't write career file: {e.Message}");
            }
        }

        public static string LocalSteamID()
        {
            var local = SemiFunc.PlayerAvatarLocal();
            return local != null ? SemiFunc.PlayerGetSteamID(local) : "";
        }

        // ---- save-ledger persistence (file lines: "saveFile|steamID=drinks~name") ----

        private static void EnsureSavesLoaded()
        {
            if (_savesLoaded) return;
            _savesLoaded = true;
            try
            {
                if (!File.Exists(SavesPath)) return;
                foreach (var line in File.ReadAllLines(SavesPath))
                {
                    int bar = line.IndexOf('|');
                    int eq = line.IndexOf('=', bar + 1);
                    int tl = line.IndexOf('~', eq + 1);
                    if (bar < 0 || eq < bar || tl < eq) continue;
                    string save = line.Substring(0, bar);
                    string steamID = line.Substring(bar + 1, eq - bar - 1);
                    if (!int.TryParse(line.Substring(eq + 1, tl - eq - 1), out int drinks)) continue;
                    string name = line.Substring(tl + 1);
                    if (!_saveLedger.TryGetValue(save, out var book)) { book = new Dictionary<string, SaveEntry>(); _saveLedger[save] = book; }
                    book[steamID] = new SaveEntry { Drinks = drinks, Name = name };
                }
            }
            catch (Exception e)
            {
                PourDecisions.Logger.LogWarning($"[Stats] couldn't read saves file: {e.Message}");
            }
        }

        private static void SaveSaves()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavesPath)!);
                var sb = new StringBuilder();
                foreach (var save in _saveLedger)
                    foreach (var kv in save.Value)
                        sb.Append(save.Key).Append('|').Append(kv.Key).Append('=')
                          .Append(kv.Value.Drinks).Append('~').Append(Escape(kv.Value.Name)).Append('\n');
                File.WriteAllText(SavesPath, sb.ToString());
            }
            catch (Exception e)
            {
                PourDecisions.Logger.LogWarning($"[Stats] couldn't write saves file: {e.Message}");
            }
        }

        // ---- drinks-taken ledger (file lines: "saveFile|taken|lastUtc") ----

        private static void EnsureTakenLoaded()
        {
            if (_takenLoaded) return;
            _takenLoaded = true;
            try
            {
                if (!File.Exists(TakenPath)) return;
                foreach (var line in File.ReadAllLines(TakenPath))
                {
                    int a = line.LastIndexOf('|');
                    if (a <= 0) continue;
                    int b = line.LastIndexOf('|', a - 1);
                    if (b < 0) continue;
                    string save = line.Substring(0, b);
                    var e = new TakenEntry();
                    int.TryParse(line.Substring(b + 1, a - b - 1), out e.Taken);
                    long.TryParse(line.Substring(a + 1), out e.LastUtc);
                    _taken[save] = e;
                }
            }
            catch (Exception ex) { PourDecisions.Logger.LogWarning($"[Stats] couldn't read taken file: {ex.Message}"); }
        }

        private static void SaveTaken()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TakenPath)!);
                var sb = new StringBuilder();
                foreach (var kv in _taken)
                    sb.Append(kv.Key).Append('|').Append(kv.Value.Taken).Append('|').Append(kv.Value.LastUtc).Append('\n');
                File.WriteAllText(TakenPath, sb.ToString());
            }
            catch (Exception ex) { PourDecisions.Logger.LogWarning($"[Stats] couldn't write taken file: {ex.Message}"); }
        }
    }
}
