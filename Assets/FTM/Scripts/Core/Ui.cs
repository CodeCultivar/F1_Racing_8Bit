using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>
    /// Small UI kit. Every position is given in design pixels from the TOP-LEFT of the parent,
    /// matching the measurements in the design doc.
    /// </summary>
    public static class Ui
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Group(Transform parent, string name, float x, float y, float w, float h)
        {
            return Place(Rect(parent, name), x, y, w, h);
        }

        public static Image Box(Transform parent, Color c, float x, float y, float w, float h, string name = "Box", bool raycast = false)
        {
            var rt = Place(Rect(parent, name), x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image FillBox(Transform parent, Color c, string name = "Fill", bool raycast = false)
        {
            var rt = Stretch(Rect(parent, name));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Dark panel with a thin light outline and square corners. Optional bold uppercase title at top-left.</summary>
        public static RectTransform Panel(Transform parent, float x, float y, float w, float h, string title = null, Color? fill = null, Color? outline = null, float border = 2f)
        {
            var outer = Box(parent, outline ?? Theme.Outline, x, y, w, h, "Panel" + (title != null ? "_" + title : ""));
            var inner = Box(outer.transform, fill ?? Theme.Panel, border, border, w - border * 2, h - border * 2, "Inner");
            if (!string.IsNullOrEmpty(title))
                Label(outer.transform, title.ToUpperInvariant(), Theme.PanelTitle, true, Theme.TextPrimary, Theme.Pad, Theme.Pad - 2, w - Theme.Pad * 2, 30);
            return outer.rectTransform;
        }

        /// <summary>Y position of content under a panel title (title + 12px).</summary>
        public const float UnderTitle = 20 + 28 + 12;

        public static Text Label(Transform parent, string text, int size, bool bold, Color color, float x, float y, float w, float h, TextAnchor anchor = TextAnchor.MiddleLeft, bool pixelFont = false)
        {
            var rt = Place(Rect(parent, "Text"), x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = pixelFont ? Theme.Pixel : (bold ? Theme.Bold : Theme.Regular);
            t.fontSize = size;
            t.color = color;
            t.text = text ?? "";
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Single-line label that shortens with "..." and shows the full text on hover.</summary>
        public static Text Ellipsis(Transform parent, string text, int size, bool bold, Color color, float x, float y, float w, float h, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = Label(parent, text, size, bold, color, x, y, w, h, anchor);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            string shortText = Truncate(text ?? "", t.font, size, w);
            t.text = shortText;
            if (shortText != text)
            {
                t.raycastTarget = true;
                var tip = t.gameObject.AddComponent<HoverTip>();
                tip.full = text;
            }
            return t;
        }

        public static float Measure(string s, Font font, int size)
        {
            if (font == null || string.IsNullOrEmpty(s)) return 0;
            font.RequestCharactersInTexture(s, size, FontStyle.Normal);
            float w = 0;
            CharacterInfo ci;
            foreach (char ch in s)
                if (font.GetCharacterInfo(ch, out ci, size, FontStyle.Normal)) w += ci.advance;
            return w;
        }

        public static string Truncate(string s, Font font, int size, float maxW)
        {
            if (Measure(s, font, size) <= maxW) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string c = s.Substring(0, n).TrimEnd() + "...";
                if (Measure(c, font, size) <= maxW) return c;
            }
            return "...";
        }

        public static FtmButton Button(Transform parent, string label, BtnStyle style, float x, float y, float w, float h, Action onClick, int fontSize = 18, bool bold = true)
        {
            var outline = Box(parent, Theme.Outline, x, y, w, h, "Btn_" + label, true);
            var fill = Box(outline.transform, Theme.Panel, 2, 2, w - 4, h - 4, "Fill");
            var btn = outline.gameObject.AddComponent<FtmButton>();
            btn.outlineImg = outline;
            btn.fillImg = fill;
            btn.style = style;
            btn.targetGraphic = outline;
            btn.transition = Selectable.Transition.None;
            if (!string.IsNullOrEmpty(label))
            {
                btn.label = Label(outline.transform, label, fontSize, bold, Theme.TextPrimary, 4, 0, w - 8, h, TextAnchor.MiddleCenter);
                btn.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            btn.Apply();
            return btn;
        }

        /// <summary>A disabled button with its reason written underneath.</summary>
        public static FtmButton DisabledButton(Transform parent, string label, BtnStyle style, float x, float y, float w, float h, string reason, int fontSize = 18)
        {
            var b = Button(parent, label, style, x, y, w, h, null, fontSize);
            b.SetEnabled(false);
            if (!string.IsNullOrEmpty(reason))
                Label(parent, reason, Theme.Small, false, Theme.TextSecondary, x, y + h + 2, w, 22, TextAnchor.UpperCenter);
            return b;
        }

        public static Image Icon(Transform parent, Sprite sprite, float x, float y, float w, float h, Color? tint = null)
        {
            var img = Box(parent, tint ?? Color.white, x, y, w, h, "Icon");
            img.sprite = sprite;
            img.preserveAspect = true;
            return img;
        }

        /// <summary>Stat bar on a 0-100 scale: filled part + darker empty part.</summary>
        public static void Bar(Transform parent, float x, float y, float w, float h, float value, Color fill, float potential = -1)
        {
            var bg = Box(parent, Theme.EmptyBar, x, y, w, h, "Bar");
            if (potential > value && potential > 0)
            {
                float pw = Mathf.Clamp01(potential / 100f) * w;
                Box(bg.transform, Theme.Darken(fill, 0.45f), 0, 0, pw, h, "Potential");
            }
            if (value > 0)
            {
                float vw = Mathf.Clamp01(value / 100f) * w;
                Box(bg.transform, fill, 0, 0, vw, h, "Value");
            }
        }

        /// <summary>Three-segment Easy / Medium / Hard meter.</summary>
        public static void Difficulty(Transform parent, float x, float y, float segW, float h, int level)
        {
            Color c = level == 0 ? Theme.Green : level == 1 ? Theme.Yellow : Theme.Red;
            for (int i = 0; i < 3; i++)
                Box(parent, (level >= 0 && i <= level) ? c : Theme.EmptyBar, x + i * (segW + 4), y, segW, h, "Seg");
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>
        /// Vertical scroll area with an 8px gray scrollbar inside on the right. Returns the content
        /// transform; call SetHeight on the result once rows have been added.
        /// </summary>
        public static ScrollArea Scroll(Transform parent, float x, float y, float w, float h)
        {
            var root = Group(parent, "Scroll", x, y, w, h);
            var viewport = Place(Rect(root, "Viewport"), 0, 0, w - 12, h);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0); // catches scroll-wheel / drag
            var content = Place(Rect(viewport, "Content"), 0, 0, w - 12, h);

            var sbRt = Place(Rect(root, "Scrollbar"), w - 8, 0, 8, h);
            var sbBg = sbRt.gameObject.AddComponent<Image>();
            sbBg.color = Theme.Darken(Theme.EmptyBar, 0.8f);
            var area = Stretch(Rect(sbRt, "Sliding"));
            var handle = Stretch(Rect(area, "Handle"));
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.color = Theme.Scrollbar;
            var sb = sbRt.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = hImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.transition = Selectable.Transition.None;

            var sr = root.gameObject.AddComponent<ScrollRect>();
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            sr.inertia = false;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return new ScrollArea { root = root, content = content, rect = sr, viewHeight = h, width = w - 12 };
        }
    }

    public class ScrollArea
    {
        public RectTransform root;
        public RectTransform content;
        public ScrollRect rect;
        public float viewHeight;
        public float width;

        public void SetHeight(float h)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(h, viewHeight));
            rect.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>Simple table: fixed header + scrolling body rows.</summary>
    public class Table
    {
        public ScrollArea scroll;
        public float[] colX;
        public float[] colW;
        public float rowH;
        public int rows;
        public float width;

        public const float HeaderH = 44f;

        public static Table Create(Transform parent, float x, float y, float w, float h, string[] headers, float[] percents, float rowH = 56f)
        {
            var t = new Table { rowH = rowH, width = w };
            float usable = w - 12; // leave room for scrollbar
            t.colX = new float[percents.Length];
            t.colW = new float[percents.Length];
            float cx = 0;
            for (int i = 0; i < percents.Length; i++)
            {
                t.colX[i] = cx;
                t.colW[i] = usable * percents[i] / 100f;
                cx += t.colW[i];
            }
            var header = Ui.Box(parent, Theme.TableHeader, x, y, w, HeaderH, "Header");
            for (int i = 0; i < headers.Length; i++)
                Ui.Ellipsis(header.transform, headers[i], Theme.Small, true, Theme.TableHeaderText, t.colX[i] + 12, 0, t.colW[i] - 16, HeaderH);
            t.scroll = Ui.Scroll(parent, x, y + HeaderH, w, h - HeaderH);
            return t;
        }

        /// <summary>Adds a body row and returns its transform (positions inside are relative to the row).</summary>
        public RectTransform AddRow(Color? bg = null)
        {
            Color c = bg ?? (rows % 2 == 0 ? Theme.RowA : Theme.RowB);
            var img = Ui.Box(scroll.content, c, 0, rows * rowH, scroll.width, rowH, "Row" + rows);
            rows++;
            scroll.SetHeight(rows * rowH);
            return img.rectTransform;
        }

        public Text Cell(RectTransform row, int col, string text, Color? color = null, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            return Ui.Ellipsis(row, text, Theme.Body, bold, color ?? Theme.TextPrimary, colX[col] + 12, 0, colW[col] - 20, rowH, anchor);
        }

        public void Message(string msg)
        {
            var row = AddRow(Theme.RowA);
            Ui.Label(row, msg, Theme.Body, false, Theme.TextSecondary, 16, 0, scroll.width - 32, rowH);
        }
    }
}
