using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PourDecisions.UI
{
    // A warning card shown once per launch, right as the vanilla splash sequence finishes - built to
    // sit alongside the game's own photosensitivity warning: full-screen, the cloned game font, a
    // gentle hand-drawn wobble, and dismiss-on-click (no auto-timeout). Title and body are separate
    // colors. Reuses SplashScreen.warningSound. Self-contained; cannot affect game state.
    public class UsageSplash : MonoBehaviour
    {
        private static bool _shownThisLaunch;

        private static readonly Color Bg = new(0.03f, 0.02f, 0.02f, 0.95f);
        private static readonly Color TitleNormal = new(0.88f, 0.63f, 0.25f);  // amber
        private static readonly Color TitleAlarm = new(0.72f, 0.20f, 0.20f);   // red
        private static readonly Color Body = new(0.74f, 0.71f, 0.65f);         // muted grey
        private static readonly Color Hint = new(0.55f, 0.52f, 0.47f);

        public static void Show(string title, string body, bool alarm)
        {
            if (_shownThisLaunch) return;
            _shownThisLaunch = true;
            Spawn(title, body, alarm);
        }

        // Test command `pd splash ...` - bypasses the once-per-launch guard.
        public static void ShowForced(string title, string body, bool alarm) => Spawn(title, body, alarm);

        private static void Spawn(string title, string body, bool alarm)
        {
            var go = new GameObject("PourDecisions_UsageSplash");
            DontDestroyOnLoad(go);
            go.AddComponent<UsageSplash>().Build(title, body, alarm);
        }

        private CanvasGroup _group = null!;
        private RectTransform _content = null!;
        private TextMeshProUGUI _hint = null!;
        private Vector2 _contentHome;
        private bool _dismissable;
        private bool _dismissed;

        private void Build(string title, string body, bool alarm)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            // Reference resolution so our pixel sizes are 1080p-relative (without this, sizes blow up
            // against the default 800x600 reference and the text spans the whole screen).
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var bg = NewImage("bg", transform, Bg);
            Stretch(bg.rectTransform);

            // A centered column with comfortable side margins - not full-bleed.
            _content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(transform, false);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 0.5f);
            _content.pivot = new Vector2(0.5f, 0.5f);
            _content.sizeDelta = new Vector2(760f, 480f);
            _contentHome = _content.anchoredPosition;

            var titleText = NativeText.Create(_content, "title", 62f, TextAlignmentOptions.Center);
            titleText.text = title;
            titleText.color = alarm ? TitleAlarm : TitleNormal;
            titleText.fontStyle = FontStyles.Bold;
            titleText.characterSpacing = 8f;
            Place(titleText.rectTransform, new Vector2(720f, 110f), new Vector2(0f, 150f));

            var bodyText = NativeText.Create(_content, "body", 32f, TextAlignmentOptions.Center);
            bodyText.text = body;
            bodyText.color = Body;
            bodyText.fontStyle = FontStyles.Bold;
            bodyText.enableWordWrapping = true;
            bodyText.lineSpacing = 12f;
            Place(bodyText.rectTransform, new Vector2(680f, 260f), new Vector2(0f, -10f));

            _hint = NativeText.Create(_content, "hint", 16f, TextAlignmentOptions.Center);
            _hint.text = "click to continue";
            _hint.color = Hint;
            Place(_hint.rectTransform, new Vector2(400f, 36f), new Vector2(0f, -160f));

            if (SplashScreen.instance != null && SplashScreen.instance.warningSound != null)
                SplashScreen.instance.warningSound.Play(Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            StartCoroutine(FadeIn());
        }

        private void Update()
        {
            if (_dismissed) return;

            // Gentle hand-drawn wobble so it reads REPO-y, not flat.
            float t = Time.unscaledTime;
            _content.anchoredPosition = _contentHome + new Vector2(Mathf.Sin(t * 2.1f) * 3f, Mathf.Sin(t * 1.7f + 1f) * 2.5f);
            _content.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 0.5f);

            if (_hint != null)
                _hint.alpha = 0.45f + 0.4f * Mathf.Abs(Mathf.Sin(t * 2.2f));

            // Mouse-click only - keyboard skip-spam during the boot intro must not blow it away.
            if (_dismissable && Input.GetMouseButtonDown(0))
            {
                _dismissed = true;
                StartCoroutine(FadeOut());
            }
        }

        private IEnumerator FadeIn()
        {
            yield return Fade(0f, 1f, 0.35f);
            yield return new WaitForSecondsRealtime(0.4f);  // brief grace before it can be dismissed
            _dismissable = true;
        }

        private IEnumerator FadeOut()
        {
            yield return Fade(1f, 0f, 0.5f);
            Destroy(gameObject);
        }

        private IEnumerator Fade(float from, float to, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, t / dur);
                yield return null;
            }
            _group.alpha = to;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(parent, false);
            img.color = color;
            return img;
        }

        private static void Place(RectTransform rt, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
