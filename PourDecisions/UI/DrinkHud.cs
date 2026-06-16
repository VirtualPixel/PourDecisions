using PourDecisions.Configuration;
using PourDecisions.Models;
using PourDecisions.Services;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PourDecisions.UI
{
    // The on-screen layer. Drink prompts reuse the game's own BigMessageUI (the same popup as
    // "EXTRACTION COMPLETE"). The corner widget is two lines: DOWNED (what you've confirmed) and
    // DRINK UP (what you still owe, with a key to knock it down one at a time).
    public class DrinkHud : MonoBehaviour
    {
        public static DrinkHud? Instance { get; private set; }

        private static readonly Color Red = new(0.85f, 0.2f, 0.2f);
        private static readonly Color Green = new(0.33f, 0.55f, 0.18f);
        private static readonly Color Amber = new(1f, 0.65f, 0f);
        private static readonly Color White = new(0.95f, 0.95f, 0.95f);
        private static readonly Color FlashGreen = new(0.45f, 0.95f, 0.3f);

        private bool _hudVisible = true;
        private object? _lastLevel;
        private HUDCanvas? _lastCanvas;   // the HUD canvas is rebuilt per scene; track it to drop stale text

        private TextMeshProUGUI? _downedLine;
        private TextMeshProUGUI? _owedLine;
        private readonly Counter _downed = new();
        private int _owedPrev;       // DRINK UP shows the exact owed instantly (no lazy ratchet to race)
        private float _owedPop;
        private float _owedFlash;
        private float _dudFlash;

        // Stacking "why" toasts (newest on top), floating + fading like an upgrade pickup.
        private readonly List<Toast> _toasts = new();
        private const float ToastLife = 4.5f;
        private const float ToastSpacing = 26f;
        private const float ToastBaseY = -190f;
        private const int ToastMax = 6;

        // Drunk-text feed: chat-style messages, bottom-left, newest at the bottom. The last few stick around
        // and settle to a low resting opacity rather than timing out and vanishing; a new message brings the
        // whole stack back to full, then it eases back down.
        private readonly List<FeedItem> _feed = new();
        private const float FeedFont = 12f;        // small, like a chat line
        private const float FeedWidth = 320f;      // narrower wrap so lines don't run wide across the screen
        private const float FeedGap = 1f;          // tight vertical gap between messages
        private const float FeedX = 24f;           // tucked into the bottom-left
        private const float FeedBaseY = 72f;       // sit low, near the bottom edge
        private const int FeedMax = 5;             // keep the last five on screen
        private const float FeedRestAlpha = 0.25f; // settles here once it's been quiet - never fully hidden
        private const float FeedFullTime = 5f;     // seconds at full opacity after a message
        private const float FeedFadeTime = 2f;     // ease from full down to the resting opacity
        private float _feedActivity = -999f;       // time of the last message; drives the whole-stack fade

        // Rebind: the config toggle sets this; we capture the next key in-game.
        private static bool _rebindRequested;
        private bool _listening;
        private static KeyCode[]? _allKeys;

        public static void RequestRebind() => _rebindRequested = true;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (_rebindRequested && !_listening) { _rebindRequested = false; _listening = true; }
            if (_listening) { HandleRebind(); return; }

            SyncCanvas();        // scene change rebuilds HUDCanvas; drop text parented to the dead one
            DetectLevelChange();
            DrunkTexts.Tick();   // host-gated inside; fires slurred messages as Semibots get drunk

            if (Input.GetKeyDown(PluginConfig.ToggleHudKey.Value)) _hudVisible = !_hudVisible;

            // Confirm a drink: one press = one drink taken, knocking down what you owe. Pressing it
            // with nothing owed gives a soft dud - you can't drink past the debt. Only during actual
            // gameplay - the shop/truck are downtime (you can still see your DOWNED total there).
            if (CanSettle() && Input.GetKeyDown(PluginConfig.ConfirmKey.Value))
            {
                if (BarTabStats.ConfirmOne())
                {
                    Sfx.Drink(); CamFx.Confirm(); _downed.Bump();   // glitch + chime = you drank it
                    PourStats.PublishLocalDowned(BarTabStats.LocalDrunk());   // tell the lobby you kept up
                }
                else { Sfx.Dud(); _dudFlash = 1f; }
            }

            AnimateTab();
            UpdateToasts();
            UpdateDrunkFeed();
        }

        // Listen for the next key and bind it. Esc cancels.
        private void HandleRebind()
        {
            if (BigMessageUI.instance != null)
                BigMessageUI.instance.BigMessage("PRESS A KEY\nfor confirm-a-drink   (Esc cancels)", "", 48f, Amber, White);

            if (Input.GetKeyDown(KeyCode.Escape)) { EndRebind(null); return; }

            _allKeys ??= (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
            foreach (var kc in _allKeys)
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
                if (Input.GetKeyDown(kc)) { EndRebind(kc); return; }
            }
        }

        private void EndRebind(KeyCode? key)
        {
            _listening = false;
            PluginConfig.RebindConfirmKey.Value = false;
            if (key.HasValue)
            {
                PluginConfig.ConfirmKey.Value = key.Value;
                if (BigMessageUI.instance != null)
                    BigMessageUI.instance.BigMessage($"BOUND TO {key.Value}", "", 50f, Green, White);
                Sfx.Toast();
            }
        }

        private void AnimateTab()
        {
            string me = BarTabStats.LocalSteamID();
            int drunk = BarTabStats.DrunkFor(me);
            int owed = Mathf.Max(0, BarTabStats.TabFor(me) - drunk);

            bool visible = _hudVisible && PluginConfig.Enabled.Value && PluginConfig.ShowTab.Value && InRun();

            // DRINK UP tracks the exact owed (instant) so a confirm always reads as a clean -1, even
            // mid-load. Pop/flash/tick fire on the change; no lazy count that the confirm can race.
            if (owed > _owedPrev) { _owedPop = 0.35f; _owedFlash = 1f; if (visible) Sfx.Tick(); }
            else if (owed < _owedPrev) { _owedPop = Mathf.Max(_owedPop, 0.25f); }
            _owedPrev = owed;
            _owedPop = Mathf.MoveTowards(_owedPop, 0f, Time.deltaTime * 2.5f);
            _owedFlash = Mathf.MoveTowards(_owedFlash, 0f, Time.deltaTime * 2f);

            _downed.Step(drunk);
            _dudFlash = Mathf.MoveTowards(_dudFlash, 0f, Time.deltaTime * 2.5f);

            bool any = drunk > 0 || owed > 0 || _downed.Animating || _owedPop > 0.01f || _dudFlash > 0.01f;
            bool show = visible && any;

            EnsureLines();
            if (_downedLine == null || _owedLine == null) return;

            _downedLine.gameObject.SetActive(show);
            bool showOwed = show && owed > 0 && CanSettle();   // DRINK UP only where you can settle
            _owedLine.gameObject.SetActive(showOwed);

            if (show)
            {
                // Green flash on a confirmed drink; red flash on a dud (nothing owed).
                Color downColor = Color.Lerp(Color.Lerp(White, FlashGreen, _downed.Flash), Red, _dudFlash);
                string downHex = ColorUtility.ToHtmlStringRGB(downColor);
                _downedLine.text = $"<color=#558B2F>DOWNED</color>   <color=#{downHex}>{_downed.ShownInt}</color>";
                _downedLine.rectTransform.localScale = Vector3.one * (1f + _downed.Pop + _dudFlash * 0.15f);
            }

            if (showOwed)
            {
                string unit = PluginConfig.EffectiveUnitWord;
                string word = owed == 1 ? unit : unit + "s";
                string key = PluginConfig.ConfirmKey.Value.ToString();
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
                string amberHex = ColorUtility.ToHtmlStringRGB(Color.Lerp(Amber, Red, _owedFlash));

                _owedLine.text = $"<color=#{amberHex}>DRINK UP   {owed} {word}</color>  " +
                                 $"<size=70%><color=#999999>[{key}]</color></size>";
                _owedLine.rectTransform.localScale = Vector3.one * (1f + _owedPop);
                _owedLine.alpha = pulse;
            }
        }

        // Cheap once-per-level edge. Resets per-level state, sobers the terminal a touch when a new
        // level starts, and (host) reads the tab recap when you reach the truck.
        private void DetectLevelChange()
        {
            var rm = RunManager.instance;
            if (rm == null) return;
            object cur = rm.levelCurrent;
            if (ReferenceEquals(cur, _lastLevel)) return;
            _lastLevel = cur;

            RuleEngine.OnNewLevel();
            DrunkTexts.ResetLevel();
            Sfx.ResetLevelSearch();   // let a cocktail that spawns on this level get picked up for the drink SFX

            // Back in the REAL lobby (pre-game / where new players join) = a new run - wipe the tab.
            // The truck "lobby" between levels does NOT reset; the tab is run-long.
            if (SemiFunc.RunIsLobbyMenu()) RuleEngine.OnRunReset();

            if (SemiFunc.RunIsLevel())
            {
                ProofService.SoberUp();
                RuleEngine.OnEvent(DrinkEvent.Waterfall, null);   // host-gated + off unless enabled
            }
            if (SemiFunc.RunIsLobby() && SemiFunc.IsMasterClientOrSingleplayer())
                TaxmanBartender.Recap();
        }

        internal void ShowOrder(DrinkOrder order)
        {
            if (!_hudVisible || !PluginConfig.Enabled.Value || !InRun()) return;

            string local = BarTabStats.LocalSteamID();
            bool involved = order.IsTeam || order.Targets.ContainsKey(local);
            if (!involved) return;

            var (msg, main, flash) = Compose(order);

            // The "why": a stacking toast that floats and fades, like an upgrade pickup.
            string amt = order.FinishDrink ? "FINISH" : $"+{order.Amount}";
            PushToast(amt, order.Reason, main);

            // Keep the big center splash for the dramatic team moments.
            if (order.IsTeam && PluginConfig.TeamSplash.Value && BigMessageUI.instance != null)
                BigMessageUI.instance.BigMessage(msg, "", 70f, main, flash);

            if (order.IsTeam && order.Kind == DrinkEvent.CashOut) Sfx.Toast();
            else Sfx.Drink();

            // A vanilla glitch scaled to the moment.
            if (order.FinishDrink) CamFx.Long();
            else if (order.Kind == DrinkEvent.Flatlined) CamFx.Short();
            else CamFx.Tiny();
        }

        private (string, Color, Color) Compose(DrinkOrder order)
        {
            string unit = PluginConfig.EffectiveUnitWord;
            string reason = order.Reason;

            if (order.FinishDrink)
                return ($"{reason}\nFinish your {unit}!", Red, White);

            string plural = order.Amount == 1 ? unit : unit + "s";
            string line2 = $"Take {order.Amount} {plural}";

            Color main = order.Kind switch
            {
                DrinkEvent.CashOut or DrinkEvent.Surplus or DrinkEvent.WelcomeBack => Green,
                DrinkEvent.Flatlined or DrinkEvent.QuotaMissed => Red,
                DrinkEvent.Butterfingers or DrinkEvent.FriendlyFire or DrinkEvent.Backstabber => Amber,
                _ => White,
            };
            return ($"{reason}\n{line2}", main, White);
        }

        private void EnsureLines()
        {
            if (_downedLine != null || HUDCanvas.instance == null) return;
            if (!NativeText.TryCaptureFont()) return;

            _downedLine = MakeLine("PourDecisions_Downed", -118f, 18f);
            _owedLine = MakeLine("PourDecisions_Owed", -146f, 22f);
        }

        private static TextMeshProUGUI MakeLine(string name, float y, float size)
        {
            var tmp = NativeText.Create(HUDCanvas.instance.transform, name, size, TextAlignmentOptions.TopLeft);
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, y);
            rt.sizeDelta = new Vector2(420f, 40f);
            return tmp;
        }

        private void PushToast(string amount, string reason, Color color)
        {
            if (HUDCanvas.instance == null || !NativeText.TryCaptureFont()) return;

            var tmp = NativeText.Create(HUDCanvas.instance.transform, "PourDecisions_Toast", 18f, TextAlignmentOptions.TopLeft);
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(460f, 28f);
            // Place and hide it now - UpdateToasts doesn't run until next frame, and without this the
            // line renders one frame at the rect default (0,0) before snapping into the stack.
            rt.anchoredPosition = new Vector2(24f, ToastBaseY + 16f);
            tmp.alpha = 0f;

            string hex = ColorUtility.ToHtmlStringRGB(color);
            tmp.text = $"<color=#{hex}>{amount}</color>  <size=85%><color=#d2d2d2>{reason}</color></size>";

            _toasts.Insert(0, new Toast { Tmp = tmp, Y = ToastBaseY + 16f });
            while (_toasts.Count > ToastMax)
            {
                Destroy(_toasts[_toasts.Count - 1].Tmp.gameObject);
                _toasts.RemoveAt(_toasts.Count - 1);
            }
        }

        // A drunk text in the bottom-left chat feed (synced; called on every client).
        internal void AddDrunkFeed(string name, string line)
        {
            if (!PluginConfig.Enabled.Value || !PluginConfig.DrunkTexts.Value) return;

            // When ChatBox is installed (and allowed), drunk texts become chat lines in its UI - skip our
            // own feed so they don't show twice. Falls through to the built-in feed if ChatBox isn't there.
            if (PluginConfig.UseChatBox.Value && ChatBoxBridge.Post(name, line)) return;

            if (HUDCanvas.instance == null || !NativeText.TryCaptureFont()) return;

            var tmp = NativeText.Create(HUDCanvas.instance.transform, "PourDecisions_Feed", FeedFont, TextAlignmentOptions.BottomLeft);
            tmp.enableWordWrapping = true;
            tmp.characterSpacing = -1.5f;   // tighten horizontally so it reads like a compact chat line
            tmp.lineSpacing = -10f;         // pull wrapped lines together, less vertical padding
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(FeedWidth, 40f);   // box height is irrelevant to stacking (preferredHeight drives it)
            // Place and hide it now - UpdateDrunkFeed takes over next frame; without this the line flashes
            // one frame at the rect default (0,0) before sliding into the bottom-left feed.
            rt.anchoredPosition = new Vector2(FeedX, FeedBaseY - 12f);
            tmp.alpha = 0f;
            tmp.text = $"<color=#e0a040>{name}</color>  <color=#cfc6ba>{line}</color>";

            _feed.Add(new FeedItem { Tmp = tmp, Y = FeedBaseY - 12f });   // newest at the bottom, slides up
            _feedActivity = Time.time;                                    // snap the whole block back to full
            while (_feed.Count > FeedMax)
            {
                Destroy(_feed[0].Tmp.gameObject);
                _feed.RemoveAt(0);
            }
        }

        // A scene transition destroys HUDCanvas and every text we parented to it, but our lists keep the
        // now-dead references. Detect the swap and drop them - the main tab rebuilds itself in EnsureLines,
        // and the feed/toasts start fresh on the new canvas. Without this, the update loops dereference a
        // destroyed TMP every frame (NRE spam + FPS death).
        private void SyncCanvas()
        {
            var canvas = HUDCanvas.instance;
            if (canvas == _lastCanvas) return;
            _lastCanvas = canvas;
            _feed.Clear();           // children of the old canvas - already destroyed with it, just forget them
            _toasts.Clear();
            _downedLine = null;      // force EnsureLines to recreate under the new canvas
            _owedLine = null;
        }

        private void UpdateDrunkFeed()
        {
            // Drop only dead references (scene swaps). Messages otherwise PERSIST - they don't time out; the
            // oldest is pushed out by a newer one in AddDrunkFeed once we're past FeedMax.
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var f = _feed[i];
                f.Age += Time.deltaTime;
                if (f.Tmp == null) _feed.RemoveAt(i);
            }

            bool hidden = !_hudVisible || !PluginConfig.Enabled.Value;

            // Whole-stack fade: full while recent, then ease down to a low resting opacity and stay there
            // (never fully gone). A new message resets _feedActivity, bringing the stack back to full.
            float since = Time.time - _feedActivity;
            float block = since <= FeedFullTime
                ? 1f
                : Mathf.Lerp(1f, FeedRestAlpha, Mathf.Clamp01((since - FeedFullTime) / FeedFadeTime));

            // Stack from the bottom up: the newest (last) sits at FeedBaseY, each older one rides above by
            // the actual height of the line below it - so wrapped (taller) messages don't overlap.
            float bottom = FeedBaseY;
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var f = _feed[i];
                if (f.Tmp == null) continue;

                f.Y = Mathf.Lerp(f.Y, bottom, Time.deltaTime * 10f);
                f.Tmp.rectTransform.anchoredPosition = new Vector2(FeedX, f.Y);

                float fadeIn = f.Age < 0.25f ? f.Age / 0.25f : 1f;   // quick pop-in for the newest line
                f.Tmp.alpha = hidden ? 0f : Mathf.Clamp01(block * fadeIn);

                bottom += Mathf.Max(f.Tmp.preferredHeight, FeedFont) + FeedGap;
            }
        }

        private void UpdateToasts()
        {
            bool hidden = !_hudVisible || !PluginConfig.Enabled.Value;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Age += Time.deltaTime;
                if (t.Tmp == null || t.Age >= ToastLife)
                {
                    if (t.Tmp != null) Destroy(t.Tmp.gameObject);
                    _toasts.RemoveAt(i);
                    continue;
                }

                float targetY = ToastBaseY - i * ToastSpacing;   // newest (index 0) on top, older pushed down
                t.Y = Mathf.Lerp(t.Y, targetY, Time.deltaTime * 10f);
                t.Tmp.rectTransform.anchoredPosition = new Vector2(24f, t.Y);

                float a = 1f;
                if (t.Age < 0.2f) a = t.Age / 0.2f;                       // fade in
                else if (t.Age > ToastLife - 0.8f) a = (ToastLife - t.Age) / 0.8f;  // fade out
                t.Tmp.alpha = hidden ? 0f : Mathf.Clamp01(a);
            }
        }

        // Anywhere the tab is relevant - including downtime (shop/truck), where DOWNED still shows.
        private static bool InRun() =>
            RunManager.instance != null && (SemiFunc.RunIsLevel() || SemiFunc.RunIsArena()
                || SemiFunc.RunIsShop() || SemiFunc.RunIsLobby());

        // Actual gameplay - where drinks accrue and you settle them. Not the shop/truck.
        private static bool CanSettle() =>
            PluginConfig.Enabled.Value
            && RunManager.instance != null && (SemiFunc.RunIsLevel() || SemiFunc.RunIsArena());

        private sealed class Toast
        {
            public TextMeshProUGUI Tmp = null!;
            public float Age;
            public float Y;
        }

        private sealed class FeedItem
        {
            public TextMeshProUGUI Tmp = null!;
            public float Age;
            public float Y;
        }

        // A number that eases toward its target with a scale pop and a colour flash on each bump.
        private sealed class Counter
        {
            private float _shown;
            private int _last;
            private int _lastTick;
            public float Pop;
            public float Flash;

            public int ShownInt => Mathf.FloorToInt(_shown + 0.0001f);
            public bool Animating => Mathf.Abs(_shown - _last) > 0.01f || Pop > 0.01f || Flash > 0.01f;

            public void Bump() { Pop = 0.35f; Flash = 1f; }

            // Returns true the moment the displayed integer ticks up (for a per-unit click).
            public bool Step(int target)
            {
                bool tickedUp = false;
                if (target > _last) Bump();
                else if (target < _last) { _shown = target; _lastTick = target; }
                _last = target;

                float speed = Mathf.Max(3f, Mathf.Abs(target - _shown) * 6f);
                _shown = Mathf.MoveTowards(_shown, target, speed * Time.deltaTime);

                int si = ShownInt;
                if (si > _lastTick) tickedUp = true;
                _lastTick = si;

                Pop = Mathf.MoveTowards(Pop, 0f, Time.deltaTime * 2.5f);
                Flash = Mathf.MoveTowards(Flash, 0f, Time.deltaTime * 2f);
                return tickedUp;
            }
        }
    }
}
