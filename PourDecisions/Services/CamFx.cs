using PourDecisions.Configuration;

namespace PourDecisions.Services
{
    // Fun screen feedback, all from the game's own CameraGlitch (the effect REPO uses for hurt,
    // upgrades, walkie static, etc.) - no new assets. PlayUpgrade is the satisfying power-up glitch
    // + chime + shake, reused as the "you drank it" confirmation.
    internal static class CamFx
    {
        private static bool On => PluginConfig.CameraEffects.Value;

        // CameraGlitch lives in the scene and never clears its static on destroy, so between scenes
        // the reference points at a dead object. Unity's == catches that; ?. would not.
        private static CameraGlitch? Live()
        {
            var g = CameraGlitch.Instance;
            return g != null ? g : null;
        }

        public static void Confirm() { if (On) Live()?.PlayUpgrade(); }   // glitch + reward chime + shake
        public static void Tiny()    { if (On) Live()?.PlayTiny(); }
        public static void Short()   { if (On) Live()?.PlayShort(); }
        public static void Long()    { if (On) Live()?.PlayLong(); }
    }
}
