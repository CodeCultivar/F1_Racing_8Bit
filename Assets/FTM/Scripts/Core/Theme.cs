using System.Globalization;
using UnityEngine;

namespace FTM
{
    /// <summary>Colors, fonts and sizes from the design doc ("Exact Lobby Sizes and Colors").</summary>
    public static class Theme
    {
        public static readonly Color Background = Hex("#829BAA");
        public static readonly Color TopBar = Hex("#15191D");
        public static readonly Color Panel = Hex("#1D2329");
        public static readonly Color Outline = Hex("#B8C7CE");
        public static readonly Color RowA = Hex("#252D34");
        public static readonly Color RowB = Hex("#303A43");
        public static readonly Color TextPrimary = Hex("#F4F6F8");
        public static readonly Color TextSecondary = Hex("#C7D0D8");
        public static readonly Color Red = Hex("#F20D32");
        public static readonly Color Green = Hex("#46ED91");
        public static readonly Color Yellow = Hex("#F5D84A");
        public static readonly Color EmptyBar = Hex("#46515B");
        public static readonly Color DisabledFill = Hex("#3A4148");
        public static readonly Color DisabledText = Hex("#8A949C");
        public static readonly Color Letterbox = Hex("#0B0D0F");
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.65f);
        public static readonly Color TableHeader = Hex("#C7D0D8");
        public static readonly Color TableHeaderText = Hex("#15191D");
        public static readonly Color Scrollbar = Hex("#8A949C");
        public static readonly Color Dark = Hex("#15191D");
        public static readonly Color White = Color.white;

        // Font sizes (pixels at the 1920x1080 design size)
        public const int TitleGame = 48;
        public const int ScreenTitle = 30;
        public const int PanelTitle = 24;
        public const int Body = 18;
        public const int Small = 16;
        public const int BigNumber = 28;

        // Text size setting (Settings window). Phones default to the largest size.
        public static readonly float[] TextScales = { 1f, 1.2f, 1.4f };
        public static readonly string[] TextScaleNames = { "NORMAL", "LARGE", "EXTRA LARGE" };
        static int _textScaleIndex = -1;
        public static int TextScaleIndex
        {
            get
            {
                if (_textScaleIndex < 0)
                {
                    try { _textScaleIndex = PlayerPrefs.GetInt("ftm_textsize", Application.isMobilePlatform ? 2 : 0); }
                    catch { _textScaleIndex = 0; }
                    _textScaleIndex = Mathf.Clamp(_textScaleIndex, 0, TextScales.Length - 1);
                }
                return _textScaleIndex;
            }
            set
            {
                _textScaleIndex = Mathf.Clamp(value, 0, TextScales.Length - 1);
                try { PlayerPrefs.SetInt("ftm_textsize", _textScaleIndex); PlayerPrefs.Save(); } catch { }
            }
        }
        public static float TextScale { get { return TextScales[TextScaleIndex]; } }

        // Layout
        public const float DesignW = 1920f;
        public const float DesignH = 1080f;
        public const float Pad = 20f;
        public const float Gap = 20f;
        public const float TitleGap = 12f;

        static Font _regular, _bold, _pixel;
        public static Font Regular { get { if (_regular == null) _regular = LoadFont("FTM/Fonts/Inter-Regular"); return _regular; } }
        public static Font Bold { get { if (_bold == null) _bold = LoadFont("FTM/Fonts/Inter-Bold"); return _bold; } }
        public static Font Pixel { get { if (_pixel == null) _pixel = LoadFont("FTM/Fonts/PressStart2P-Regular"); return _pixel; } }

        static Font LoadFont(string path)
        {
            var f = Resources.Load<Font>(path);
            if (f == null)
            {
                Debug.LogWarning("FTM: font missing at Resources/" + path + ", using built-in font.");
                f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return f;
        }

        public static Color Hex(string hex)
        {
            Color c;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out c)) return c;
            return new Color(0.55f, 0.58f, 0.6f);
        }

        public static Color Darken(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }

        /// <summary>Black text on light team colors, white on dark ones.</summary>
        public static Color TextOn(Color bg)
        {
            float lum = 0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b;
            return lum > 0.55f ? Color.black : Color.white;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Money(long v)
        {
            return (v < 0 ? "-$" : "$") + System.Math.Abs(v).ToString("N0", Inv);
        }

        public static string OneDecimal(float v) { return v.ToString("0.0", Inv); }

        public const string NotConfigured = "Not configured";
    }
}
