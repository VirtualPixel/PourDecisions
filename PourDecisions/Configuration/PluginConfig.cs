using BepInEx.Configuration;
using PourDecisions.Models;
using System.Collections.Generic;
using UnityEngine;

namespace PourDecisions.Configuration
{
    public enum VerbosityLevel { Off = 0, Debug = 1, Verbose = 2 }

    // Config is built to be skimmable: one number per event (0 = off), grouped into a few clearly
    // ordered sections. The preset dropdown is the everyday knob; the per-event numbers are there
    // when you want them, and touching one flips the preset to Custom.
    internal static class PluginConfig
    {
        public const string PresetCasual = "Casual";
        public const string PresetStandard = "Standard";
        public const string PresetLastResort = "Last Resort";
        public const string PresetCustom = "Custom";

        public const string BuildFast = "Fast";
        public const string BuildGradual = "Gradual";
        public const string BuildSlow = "Slow";

        // Section/key names must avoid = \n \t \ " ' [ ] (BepInEx restriction).
        private const string SGeneral = "1 - General";
        private const string SDrinks = "2 - Drinks per event (0 turns it off)";
        private const string SExtras = "3 - Drink extras";
        private const string SDisplay = "4 - Terminal and display";
        private const string SSafety = "5 - Safety";
        private const string SDebug = "6 - Debug";
        private const string SAdvanced = "7 - Advanced (fine tuning)";

        // --- General ---
        public static ConfigEntry<bool> Enabled = null!;
        public static ConfigEntry<string> Ruleset = null!;
        public static ConfigEntry<string> UnitWord = null!;
        public static ConfigEntry<bool> ShowTab = null!;
        public static ConfigEntry<KeyCode> ToggleHudKey = null!;
        public static ConfigEntry<KeyCode> ConfirmKey = null!;
        public static ConfigEntry<bool> RebindConfirmKey = null!;
        public static ConfigEntry<VerbosityLevel> LogLevel = null!;

        // --- Drinks per event (single int each, 0 = off) ---
        private static readonly Dictionary<DrinkEvent, ConfigEntry<int>> _amount = new();
        private static readonly HashSet<ConfigEntryBase> _ruleEntries = new();
        public static ConfigEntry<bool> FinishOnWipe = null!;       // QuotaMissed
        public static ConfigEntry<bool> EnemyEveryTime = null!;     // TaxmanCometh cadence

        // --- Display ---
        public static ConfigEntry<bool> DrunkTerminal = null!;
        public static ConfigEntry<string> TerminalBuildup = null!;
        public static ConfigEntry<bool> TaxmanReactions = null!;
        public static ConfigEntry<bool> DrunkTexts = null!;
        public static ConfigEntry<bool> UseChatBox = null!;
        public static ConfigEntry<float> PromptSeconds = null!;
        public static ConfigEntry<bool> TeamSplash = null!;
        public static ConfigEntry<bool> Sound = null!;
        public static ConfigEntry<bool> CameraEffects = null!;

        // --- Safety ---
        public static ConfigEntry<bool> UsageWarning = null!;
        public static ConfigEntry<int> ExcessiveUsageDays = null!;

        // --- Debug ---
        public static ConfigEntry<bool> EnableTestCommands = null!;

        // --- Advanced (fine tuning; the simple knobs above cover most needs) ---
        public static ConfigEntry<int> ProofThresholdOverride = null!;
        public static ConfigEntry<int> SoberUpPerLevel = null!;
        public static ConfigEntry<int> DollarsPerSmashDrink = null!;

        public static ConfigFile File = null!;
        private static bool _applyingPreset;

        public static void Init(ConfigFile config)
        {
            File = config;

            Enabled = config.Bind(SGeneral, "Enabled", true, "Turn the whole drinking game on or off.");
            Ruleset = config.Bind(SGeneral, "Ruleset", PresetStandard, new ConfigDescription(
                "The everyday knob. A preset fills in the numbers below; editing any number switches this to Custom.",
                new AcceptableValueList<string>(PresetCasual, PresetStandard, PresetLastResort, PresetCustom)));
            UnitWord = config.Bind(SGeneral, "Unit word", "drink",
                "What one unit is called. 'sip' for lighter sessions, 'shot' if you hate yourself, or 'dare' / 'push-up' for a dry game.");
            ShowTab = config.Bind(SGeneral, "Show my tab on screen", true, "Show your running tally in the corner during a run.");
            ToggleHudKey = config.Bind(SGeneral, "Hide/show overlay key", KeyCode.F7, "Hotkey to toggle the on-screen overlay.");
            // Hidden from REPOConfig (a raw KeyCode renders as a 300-entry slider). Rebind via the
            // toggle below, which captures the next key you press in-game.
            ConfirmKey = config.Bind(SGeneral, "Confirm a drink key", KeyCode.V,
                new ConfigDescription("Press once per drink to clear what you owe.", null, "HideFromREPOConfig"));
            RebindConfirmKey = config.Bind(SGeneral, "Rebind confirm key", false,
                "Turn on, then press any key in-game to set it as your confirm-a-drink key.");
            LogLevel = config.Bind(SGeneral, "Log level", VerbosityLevel.Off, "Logging detail. Off is fine for normal play.");

            // One number per event. Defaults are the Standard preset.
            BindAmount(config, DrinkEvent.Flatlined, "Death", 3, "Drinks when you die.");
            BindAmount(config, DrinkEvent.FirstBlood, "First death bonus", 1, "Extra drinks for the first death of the run.");
            BindAmount(config, DrinkEvent.DownBad, "Knocked down", 1, "Drinks when something knocks you over.");
            BindAmount(config, DrinkEvent.InTheBag, "Grabbed by enemy", 2, "Drinks when the head-grabber gets you.");
            BindAmount(config, DrinkEvent.Butterfingers, "Smashed a valuable", 1, "Drinks for breaking loot (scales with the money lost).");
            BindAmount(config, DrinkEvent.TaxmanCometh, "Enemy appears", 1, "Everyone drinks when an enemy wakes up.");
            BindAmount(config, DrinkEvent.CashOut, "Good extraction", 1, "Everyone toasts a completed extraction.");
            BindAmount(config, DrinkEvent.Surplus, "Surplus extraction", 2, "Everyone drinks when you beat the quota.");
            BindAmount(config, DrinkEvent.WelcomeBack, "Revived", 0, "Drinks when you're brought back. Off by default.");
            BindAmount(config, DrinkEvent.Waterfall, "Waterfall at level start", 0, "A waterfall when a new level begins. Off by default.");

            FinishOnWipe = config.Bind(SExtras, "Finish your drink on a wipe", true, "When the whole crew goes down, everyone finishes their drink.");
            EnemyEveryTime = config.Bind(SExtras, "Enemy drink every time", false, "On: every enemy spawn. Off: just the first one each level.");

            DrunkTerminal = config.Bind(SDisplay, "Drunk truck terminal", true, "The terminal slurs as the team drinks. Never touches haul or quota numbers.");
            TerminalBuildup = config.Bind(SDisplay, "Terminal gets drunk", BuildGradual, new ConfigDescription(
                "How quickly the terminal starts slurring.", new AcceptableValueList<string>(BuildFast, BuildGradual, BuildSlow)));
            TaxmanReactions = config.Bind(SDisplay, "Taxman terminal reactions", true, "The Taxman posts emoji and a tab recap on the terminal.");
            DrunkTexts = config.Bind(SDisplay, "Drunk texts", true, "Drunk Semibots fire off slurred messages on screen, more often the more they've had.");
            UseChatBox = config.Bind(SDisplay, "Use ChatBox for drunk texts", true, "If the ChatBox mod is installed, show drunk texts in its chat UI instead of the built-in feed.");
            PromptSeconds = config.Bind(SDisplay, "Prompt time (seconds)", 4f, new ConfigDescription(
                "How long a drink prompt stays up.", new AcceptableValueRange<float>(1.5f, 10f)));
            TeamSplash = config.Bind(SDisplay, "Big splash for team drinks", true, "Use the center splash for everyone-drinks moments.");
            Sound = config.Bind(SDisplay, "Sound", true, "Play game sounds with prompts.");
            CameraEffects = config.Bind(SDisplay, "Camera effects", true, "Vanilla screen-glitch feedback on drinks and when you confirm one.");

            UsageWarning = config.Bind(SSafety, "Startup warning", true,
                "Show a gentle drink-responsibly notice on startup, escalating after several days of use.");
            ExcessiveUsageDays = config.Bind(SSafety, "Days before easing-off notice", 3, new ConfigDescription(
                "Days in a row before the startup notice escalates.", new AcceptableValueRange<int>(2, 14)));

            EnableTestCommands = config.Bind(SDebug, "Test commands", false,
                "Enable the 'pd' test commands (type /pd in chat, or open the dev console). Dev use.");

            ProofThresholdOverride = config.Bind(SAdvanced, "Terminal drunkenness override", 0, new ConfigDescription(
                "Exact team-drink count for a fully-drunk terminal. 0 = use the simple Fast/Gradual/Slow setting.",
                new AcceptableValueRange<int>(0, 150)));
            SoberUpPerLevel = config.Bind(SAdvanced, "Sober up per level", 4, new ConfigDescription(
                "Drink units the team sobers off each new level (terminal only).", new AcceptableValueRange<int>(0, 30)));
            DollarsPerSmashDrink = config.Bind(SAdvanced, "Dollars per smash drink", 700, new ConfigDescription(
                "Smashing a valuable adds one drink per this many dollars of value lost (and is the minimum loss that counts).",
                new AcceptableValueRange<int>(100, 50000)));

            ApplyPreset(Ruleset.Value);
            config.SettingChanged += OnSettingChanged;
        }

        private static void BindAmount(ConfigFile config, DrinkEvent ev, string label, int def, string help)
        {
            var entry = config.Bind(SDrinks, label, def, new ConfigDescription(help, new AcceptableValueRange<int>(0, 10)));
            _amount[ev] = entry;
            _ruleEntries.Add(entry);
        }

        // Only the host turns events into orders, so its local config IS the ruleset - no sync.
        public static bool RuleEnabled(DrinkEvent ev)
        {
            if (!Enabled.Value) return false;
            if (ev == DrinkEvent.QuotaMissed) return FinishOnWipe.Value;
            return RuleAmount(ev) > 0;
        }

        public static int RuleAmount(DrinkEvent ev) => ev == DrinkEvent.QuotaMissed ? 0 : RuleAmountLocal(ev);
        public static int RuleAmountLocal(DrinkEvent ev) => _amount.TryGetValue(ev, out var c) ? c.Value : 0;

        public static bool EffectiveEnemyEveryTime => EnemyEveryTime.Value;
        public static string EffectiveUnitWord => UnitWord.Value;   // display word stays local per player

        public static int ProofThresholdValue
        {
            get
            {
                if (ProofThresholdOverride.Value > 0) return ProofThresholdOverride.Value;
                return TerminalBuildup.Value switch { BuildFast => 15, BuildSlow => 55, _ => 32 };
            }
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (_applyingPreset) return;
            if (ReferenceEquals(e.ChangedSetting, RebindConfirmKey) && RebindConfirmKey.Value)
            {
                UI.DrinkHud.RequestRebind();
                return;
            }
            if (ReferenceEquals(e.ChangedSetting, Ruleset)) { ApplyPreset(Ruleset.Value); return; }
            if (_ruleEntries.Contains(e.ChangedSetting) ||
                ReferenceEquals(e.ChangedSetting, FinishOnWipe) ||
                ReferenceEquals(e.ChangedSetting, EnemyEveryTime))
                MarkCustom();
        }

        private static void MarkCustom()
        {
            if (_applyingPreset) return;
            if (Ruleset.Value != PresetCustom) Ruleset.Value = PresetCustom;
        }

        private static void ApplyPreset(string preset)
        {
            if (preset == PresetCustom) return;
            _applyingPreset = true;
            try
            {
                switch (preset)
                {
                    case PresetCasual:
                        Zero();
                        Set(DrinkEvent.Flatlined, 3);
                        Set(DrinkEvent.CashOut, 1);
                        FinishOnWipe.Value = true;
                        EnemyEveryTime.Value = false;
                        break;

                    case PresetLastResort:
                        Set(DrinkEvent.Flatlined, 6);
                        Set(DrinkEvent.FirstBlood, 2);
                        Set(DrinkEvent.DownBad, 2);
                        Set(DrinkEvent.InTheBag, 4);
                        Set(DrinkEvent.Butterfingers, 2);
                        Set(DrinkEvent.TaxmanCometh, 1);
                        Set(DrinkEvent.CashOut, 2);
                        Set(DrinkEvent.Surplus, 4);
                        Set(DrinkEvent.WelcomeBack, 1);
                        Set(DrinkEvent.Waterfall, 1);
                        FinishOnWipe.Value = true;
                        EnemyEveryTime.Value = true;
                        break;

                    default: // Standard
                        Set(DrinkEvent.Flatlined, 3);
                        Set(DrinkEvent.FirstBlood, 1);
                        Set(DrinkEvent.DownBad, 1);
                        Set(DrinkEvent.InTheBag, 2);
                        Set(DrinkEvent.Butterfingers, 1);
                        Set(DrinkEvent.TaxmanCometh, 1);
                        Set(DrinkEvent.CashOut, 1);
                        Set(DrinkEvent.Surplus, 2);
                        Set(DrinkEvent.WelcomeBack, 0);
                        Set(DrinkEvent.Waterfall, 0);
                        FinishOnWipe.Value = true;
                        EnemyEveryTime.Value = false;
                        break;
                }
            }
            finally { _applyingPreset = false; }
        }

        private static void Zero() { foreach (var c in _amount.Values) c.Value = 0; }
        private static void Set(DrinkEvent ev, int amount) { if (_amount.TryGetValue(ev, out var c)) c.Value = amount; }
    }
}
