using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PourDecisions.Configuration;

namespace PourDecisions
{
    // R.E.P.O. drinking game. Host sets the rules; everyone in the lobby installs it.
    // All gameplay logic lives in Services/ and Patches/ - this file just boots it.
    [BepInPlugin(Guid, "Pour Decisions", BuildInfo.Version)]
    [BepInDependency("Vippy.StatsCore")]   // the tracking engine PourDecisions reads to drive the game
    public class PourDecisions : BaseUnityPlugin
    {
        public const string Guid = "Vippy.PourDecisions";

        internal static PourDecisions Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger => Instance.BaseLogger;
        private ManualLogSource BaseLogger => base.Logger;
        internal Harmony? Harmony { get; set; }

        private void Awake()
        {
            Instance = this;
            gameObject.transform.parent = null;
            gameObject.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

            PluginConfig.Init(Config);

            Harmony ??= new Harmony(Guid);
            Harmony.PatchAll();

            Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} loaded - last call. (LogLevel={PluginConfig.LogLevel.Value})");
        }

        internal static void LogAlways(string msg) => Logger.LogInfo(msg);

        internal static void LogInfo(string msg)
        {
            if (PluginConfig.LogLevel.Value >= VerbosityLevel.Debug) Logger.LogInfo(msg);
        }

        internal static void LogVerbose(string msg)
        {
            if (PluginConfig.LogLevel.Value >= VerbosityLevel.Verbose) Logger.LogDebug(msg);
        }
    }
}
