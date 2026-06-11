using System;
using System.Reflection;
using HarmonyLib;

namespace PourDecisions.Services
{
    // Optional hook into the ChatBox mod (UnloadedHangar-ChatBox). When it's installed, drunk texts render
    // as lines in its chat UI instead of our own feed, so they read like in-game chat. Reflected in and
    // soft-fails if ChatBox is absent or changes shape - never a hard dependency.
    //
    // ChatBox's AddChatMessageSystem is a LOCAL render (it just appends to the local chat object, no
    // networking). PourDecisions already fans every drunk text out to all clients, so each client calling
    // this for its own copy shows the line in everyone's ChatBox - no double-send, no extra RPC.
    internal static class ChatBoxBridge
    {
        private const string NameHex = "e0a040";   // same amber the built-in feed uses for the speaker name

        private static bool _resolved;
        private static PropertyInfo? _instanceProp;
        private static MethodInfo? _addSystem;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var t = AccessTools.TypeByName("ChatBox.NonPatch.NonPatches");
                if (t == null) return;
                _instanceProp = AccessTools.Property(t, "Instance");
                _addSystem = AccessTools.Method(t, "AddChatMessageSystem",
                    new[] { typeof(string), typeof(string), typeof(string) });
            }
            catch (Exception e) { PourDecisions.LogVerbose($"[ChatBox] resolve failed: {e.Message}"); }
        }

        internal static bool Available
        {
            get { Resolve(); return _instanceProp != null && _addSystem != null; }
        }

        // Render one drunk text in ChatBox's UI. Returns false (so the caller can fall back to our own feed)
        // if ChatBox isn't present or its instance isn't up yet.
        internal static bool Post(string name, string line)
        {
            if (!Available) return false;
            try
            {
                object? inst = _instanceProp!.GetValue(null);
                if (inst == null) return false;
                _addSystem!.Invoke(inst, new object[] { line, NameHex, name });
                return true;
            }
            catch (Exception e) { PourDecisions.LogVerbose($"[ChatBox] post failed: {e.Message}"); return false; }
        }
    }
}
