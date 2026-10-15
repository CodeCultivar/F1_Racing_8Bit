using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>
    /// Starts automatically when you press Play (no scene setup needed). Builds a 1920x1080 "stage"
    /// that scales to fit the window, keeps landscape proportions, letterboxes tall screens and
    /// scrolls below 1280x720. Owns screen navigation, popup windows and saving.
    /// </summary>
    public class GameApp : MonoBehaviour
    {
        public static GameApp I;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("FTM Game");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        Canvas canvas;
        RectTransform viewport;
        public RectTransform Stage;
        RectTransform screenLayer, windowLayer, tipLayer;
        ScrollRect stageScroll;
        Text saveStatus;
        float saveStatusTimer;
        RectTransform tooltip;
        Text tooltipText;

        public CareerSave Career;
        IScreen current;
        readonly List<Window> windows = new List<Window>();

        public const float MinScale = 720f / 1080f;
        const float Bleed = 3000f;
        Image topBand, bleed;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            // landscape only (either way round), never portrait
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            ConfigLoader.Load();
            EnsureEventSystem();
            BuildCanvas();
            Show(new OpeningScreen());
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            DontDestroyOnLoad(es);
        }

        void BuildCanvas()
        {
            var cgo = new GameObject("FTM Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            cgo.AddComponent<GraphicRaycaster>();

            // Letterbox background (dark space above/below on tall screens)
            var bg = Ui.FillBox(cgo.transform, Theme.Background, "Letterbox", true);

            viewport = Ui.Stretch(Ui.Rect(cgo.transform, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            Stage = Ui.Rect(viewport, "Stage");
            Stage.anchorMin = Stage.anchorMax = new Vector2(0.5f, 0.5f);
            Stage.pivot = new Vector2(0.5f, 0.5f);
            Stage.sizeDelta = new Vector2(Theme.DesignW, Theme.DesignH);
            var stageBg = Stage.gameObject.AddComponent<Image>();
            stageBg.color = Theme.Background;
            // Wide phones (iPhone ~19.5:9) are wider than the 16:9 design: the game fills the full height and
            // the screen's background and top bar continue out to both edges, so there are no black bars.
            // The extra width also keeps buttons clear of the notch / Dynamic Island.
            bleed = Ui.Box(Stage, Theme.Background, -Bleed, -Bleed, Theme.DesignW + 2 * Bleed, Theme.DesignH + 2 * Bleed, "Bleed");
            bleed.raycastTarget = false;
            topBand = Ui.Box(Stage, Theme.TopBar, -Bleed, 0, Theme.DesignW + 2 * Bleed, 80, "TopBand");
            topBand.raycastTarget = false;
            topBand.gameObject.SetActive(false);

            stageScroll = viewport.gameObject.AddComponent<ScrollRect>();
            stageScroll.viewport = viewport;
            stageScroll.content = Stage;
            stageScroll.movementType = ScrollRect.MovementType.Clamped;
            stageScroll.inertia = false;
            stageScroll.enabled = false;

            screenLayer = Ui.Stretch(Ui.Rect(Stage, "Screens"));
            windowLayer = Ui.Stretch(Ui.Rect(Stage, "Windows"));
            tipLayer = Ui.Stretch(Ui.Rect(Stage, "Tooltips"));

            saveStatus = Ui.Label(tipLayer, "", Theme.Small, true, Theme.Yellow, 160, 28, 220, 24);

            tooltip = Ui.Box(tipLayer, Theme.Dark, 0, 0, 10, 36, "Tooltip").rectTransform;
            tooltipText = Ui.Label(tooltip, "", Theme.Small, false, Theme.TextPrimary, 10, 0, 10, 36);
            tooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            tooltip.gameObject.SetActive(false);
            UpdateScale();
        }

        void UpdateScale()
        {
            float sw = Screen.width, sh = Screen.height;
            float fit = Mathf.Min(sw / Theme.DesignW, sh / Theme.DesignH);
            // Below 1280x720 keep the 1280x720 layout and let the whole game area scroll.
            float minPixels = MinScale;
            float scale = Mathf.Max(fit, minPixels);
            canvas.scaleFactor = scale;
            bool needScroll = Theme.DesignW * scale > sw + 0.5f || Theme.DesignH * scale > sh + 0.5f;
            if (needScroll != stageScroll.enabled)
            {
                stageScroll.enabled = needScroll;
                stageScroll.horizontal = needScroll;
                stageScroll.vertical = needScroll;
                if (!needScroll) Stage.anchoredPosition = Vector2.zero;
            }
            if (needScroll)
            {
                // anchor the stage to the top-left when it is larger than the window
                Stage.anchorMin = Stage.anchorMax = new Vector2(0, 1);
                Stage.pivot = new Vector2(0, 1);
            }
            else
            {
                Stage.anchorMin = Stage.anchorMax = new Vector2(0.5f, 0.5f);
                Stage.pivot = new Vector2(0.5f, 0.5f);
                Stage.anchoredPosition = Vector2.zero;
            }
        }

        void Update()
        {
            UpdateScale();

            if (Career != null && (current is LobbyScreen || current is RaceScreen || current is GridScreen)) Career.playtimeSeconds += Time.unscaledDeltaTime;

            if (saveStatusTimer > 0)
            {
                saveStatusTimer -= Time.unscaledDeltaTime;
                if (saveStatusTimer <= 0) saveStatus.text = "";
            }

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && windows.Count > 0)
                windows[windows.Count - 1].Close();
        }

        // ---------------- Screens ----------------

        public void Show(IScreen screen)
        {
            CloseAllWindows();
            HideTooltip();
            Ui.Clear(screenLayer);
            current = screen;
            var root = Ui.Stretch(Ui.Rect(screenLayer, screen.GetType().Name));
            screen.Build(root);
            // continue the screen's top bar to the screen edges on wide displays
            bool hasBar = root.Find("TopBar") != null || root.Find("TeamHeader") != null;
            topBand.gameObject.SetActive(hasBar);
            // the edges take the screen's own background colour
            var fill = root.Find("Fill");
            var fillImg = fill != null ? fill.GetComponent<Image>() : null;
            bleed.color = fillImg != null ? fillImg.color : Theme.Background;
        }

        public void RefreshCurrent()
        {
            if (current != null) Show(current);
        }

        // ---------------- Windows ----------------

        /// <summary>
        /// Centered dark window over a 65% black background. Title top-left, X top-right,
        /// Cancel bottom-left and the red confirm button bottom-right. X, Escape and Cancel close it
        /// without changing anything.
        /// </summary>
        public Window OpenWindow(string title, float width, float bodyHeight, string confirmLabel = null, Action<Window> onConfirm = null, string cancelLabel = "CANCEL", float maxHeight = 800f)
        {
            HideTooltip();
            var w = new Window();
            var overlay = Ui.FillBox(windowLayer, Theme.Overlay, "Overlay", true);
            // dim the edges too on wide phones, not just the 16:9 area
            float W = Theme.DesignW, H = Theme.DesignH;
            Ui.Box(overlay.transform, Theme.Overlay, -Bleed, -Bleed, Bleed, H + 2 * Bleed, "DimL", true);
            Ui.Box(overlay.transform, Theme.Overlay, W, -Bleed, Bleed, H + 2 * Bleed, "DimR", true);
            Ui.Box(overlay.transform, Theme.Overlay, 0, -Bleed, W, Bleed, "DimT", true);
            Ui.Box(overlay.transform, Theme.Overlay, 0, H, W, Bleed, "DimB", true);
            w.root = overlay.rectTransform;

            const float pad = 24, headerH = 40, footerH = 48;
            bool hasFooter = cancelLabel != null || confirmLabel != null;
            float chrome = pad + headerH + 20 + (hasFooter ? 20 + footerH : 0) + pad;
            float height = Mathf.Min(maxHeight, bodyHeight + chrome);
            float bodyView = height - chrome;

            float x = (Theme.DesignW - width) / 2f, y = (Theme.DesignH - height) / 2f;
            var panel = Ui.Panel(w.root, x, y, width, height);
            w.panel = panel;
            w.width = width;
            Ui.Ellipsis(panel, title, Theme.PanelTitle, true, Theme.TextPrimary, pad, pad, width - pad * 2 - 60, headerH);
            Ui.Button(panel, "X", BtnStyle.Dark, width - pad - 40, pad, 40, 40, () => w.Close());

            w.bodyWidth = width - pad * 2;
            if (bodyHeight > bodyView + 0.5f)
            {
                var sa = Ui.Scroll(panel, pad, pad + headerH + 20, width - pad * 2, bodyView);
                sa.SetHeight(bodyHeight);
                w.body = sa.content;
                w.bodyWidth = sa.width;
            }
            else
            {
                w.body = Ui.Group(panel, "Body", pad, pad + headerH + 20, width - pad * 2, bodyView);
            }

            float fy = height - pad - footerH;
            if (cancelLabel != null)
                w.cancel = Ui.Button(panel, cancelLabel, BtnStyle.Dark, pad, fy, 200, footerH, () => w.Close());
            if (confirmLabel != null)
            {
                float cw = Mathf.Max(220, Ui.Measure(confirmLabel, Theme.Bold, 18) + 48);
                w.confirm = Ui.Button(panel, confirmLabel, BtnStyle.Red, width - pad - cw, fy, cw, footerH, null);
                w.confirm.onClick.AddListener(() =>
                {
                    if (!w.confirm.interactable) return;
                    w.confirm.SetEnabled(false); // stop double clicks / double charges
                    if (onConfirm != null) onConfirm(w);
                });
                w.reasonY = fy - 30;
            }
            windows.Add(w);
            w.onClosed = () => windows.Remove(w);
            return w;
        }

        public void CloseAllWindows()
        {
            for (int i = windows.Count - 1; i >= 0; i--) windows[i].Close();
        }

        /// <summary>Simple message window with a single OK button.</summary>
        public void Message(string title, string text)
        {
            var w = OpenWindow(title, 600, 120, null, null, "OK");
            Ui.Label(w.body, text, Theme.Body, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 120, TextAnchor.UpperLeft);
        }

        // ---------------- Tooltip ----------------

        public void ShowTooltip(string text, RectTransform target)
        {
            if (string.IsNullOrEmpty(text)) return;
            tooltipText.text = text;
            float tw = Ui.Measure(text, Theme.Regular, Theme.Small) + 20;
            tooltip.sizeDelta = new Vector2(tw, 36);
            tooltipText.rectTransform.sizeDelta = new Vector2(tw - 20, 36);
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 local = Stage.InverseTransformPoint(corners[0]); // bottom-left of target
            float x = local.x - Stage.rect.xMin;
            float y = Stage.rect.yMax - local.y + 4;
            x = Mathf.Clamp(x, 0, Theme.DesignW - tw);
            y = Mathf.Clamp(y, 0, Theme.DesignH - 36);
            tooltip.anchoredPosition = new Vector2(x, -y);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
        }

        public void HideTooltip() { if (tooltip != null) tooltip.gameObject.SetActive(false); }

        // ---------------- Saving ----------------

        void SetStatus(string s, Color c, float seconds)
        {
            saveStatus.text = s;
            saveStatus.color = c;
            saveStatusTimer = seconds;
        }

        /// <summary>
        /// Applies a change to the current career and saves it. If saving fails, the change is undone
        /// (nothing charged) and Retry / Cancel are offered.
        /// </summary>
        public void Commit(Action<CareerSave> change, Action onSaved)
        {
            if (Career == null) return;
            string backup = JsonUtility.ToJson(Career);
            change(Career);
            Career.lastPlayedTicks = DateTime.UtcNow.Ticks;
            SetStatus("Saving...", Theme.Yellow, 10f);
            string err;
            if (SaveSystem.TrySave(Career, out err))
            {
                SetStatus("Saved", Theme.Green, 1.5f);
                if (onSaved != null) onSaved();
            }
            else
            {
                Career = JsonUtility.FromJson<CareerSave>(backup);
                SetStatus("Save failed", Theme.Red, 3f);
                var w = OpenWindow("SAVE FAILED", 600, 110, "RETRY", win => { win.Close(); Commit(change, onSaved); });
                Ui.Label(w.body, "Your last saved progress was kept and nothing was charged.\n" + err, Theme.Body, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 110, TextAnchor.UpperLeft);
            }
        }

        /// <summary>Saves the current career without changing anything (e.g. Home).</summary>
        public void SaveNow(Action onSaved) { Commit(c => { }, onSaved); }

        public void OpenCareer(CareerSave c)
        {
            Career = c;
            Rules.EnsureWorld(c);
            Show(new LobbyScreen());
        }

        void OnApplicationPause(bool paused) { if (paused) SaveOnExit(); }

        void OnApplicationQuit() { SaveOnExit(); }

        void SaveOnExit()
        {
            string err;
            var race = current as RaceScreen;
            if (race != null) { race.PersistForPause(); return; }
            if (Career != null && (current is LobbyScreen || current is GridScreen)) SaveSystem.TrySave(Career, out err);
        }
    }

    public interface IScreen
    {
        void Build(RectTransform root);
    }

    public class Window
    {
        public RectTransform root, panel, body;
        public float width, bodyWidth, reasonY;
        public FtmButton confirm, cancel;
        public Action onClosed;
        bool closed;

        public void Close()
        {
            if (closed) return;
            closed = true;
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            if (onClosed != null) onClosed();
            if (GameApp.I != null) GameApp.I.HideTooltip();
        }

        /// <summary>Disables the confirm button and writes the reason just above the footer.</summary>
        public void Block(string reason)
        {
            if (confirm == null) return;
            confirm.SetEnabled(false);
            if (!string.IsNullOrEmpty(reason))
                Ui.Label(panel, reason, Theme.Small, true, Theme.Yellow, 24, reasonY, width - 48, 26, TextAnchor.MiddleRight);
        }

        /// <summary>Re-enable after an action that did not complete.</summary>
        public void Unblock() { if (confirm != null) confirm.SetEnabled(true); }
    }
}
