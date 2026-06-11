using PourDecisions.Configuration;
using UnityEngine;

namespace PourDecisions.Services
{
    // All audio is native game audio. The signature "drink" cue is the museum martini's spill
    // sound (ValuableCocktail.soundSpill), captured the first time one spawns; everything else
    // reuses the MenuManager sound bank. No AssetBundle.
    internal static class Sfx
    {
        private static Sound? _spill;

        // The museum martini's spill SFX - grabbed off a live ValuableCocktail when one is present
        // (only spawns on some levels). Cached once found; falls back to the menu deny sound.
        private static Sound? Spill()
        {
            if (_spill != null) return _spill;
            var cocktail = Object.FindObjectOfType<ValuableCocktail>();
            if (cocktail != null) _spill = cocktail.soundSpill;
            return _spill;
        }

        public static void Drink()
        {
            if (!PluginConfig.Sound.Value) return;
            var s = Spill();
            if (s != null) { s.Play(CamPos()); return; }
            MenuManager.instance?.soundDeny?.Play(CamPos());
        }

        public static void Toast()
        {
            if (!PluginConfig.Sound.Value) return;
            Spill()?.Play(CamPos());
            MenuManager.instance?.soundConfirm?.Play(CamPos());
        }

        public static void Tick()
        {
            if (!PluginConfig.Sound.Value) return;
            MenuManager.instance?.soundTick?.Play(CamPos());
        }

        // Soft "nothing there" sound - used when you confirm a drink but owe nothing.
        public static void Dud()
        {
            if (!PluginConfig.Sound.Value) return;
            MenuManager.instance?.soundDud?.Play(CamPos());
        }

        private static Vector3 CamPos()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
            var local = PlayerAvatar.instance;
            return local != null ? local.transform.position : Vector3.zero;
        }
    }
}
