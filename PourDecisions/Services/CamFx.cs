using PourDecisions.Configuration;

namespace PourDecisions.Services
{
    // Fun screen feedback, all from the game's own CameraGlitch (the effect REPO uses for hurt,
    // upgrades, walkie static, etc.) - no new assets. PlayUpgrade is the satisfying power-up glitch
    // + chime + shake, reused as the "you drank it" confirmation.
    internal static class CamFx
    {
        private static bool On => PluginConfig.CameraEffects.Value;

        public static void Confirm() { if (On) CameraGlitch.Instance?.PlayUpgrade(); }   // glitch + reward chime + shake
        public static void Tiny()    { if (On) CameraGlitch.Instance?.PlayTiny(); }
        public static void Short()   { if (On) CameraGlitch.Instance?.PlayShort(); }
        public static void Long()    { if (On) CameraGlitch.Instance?.PlayLong(); }
    }
}
