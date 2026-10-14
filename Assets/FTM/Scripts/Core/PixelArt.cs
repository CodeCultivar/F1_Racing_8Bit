using System.Collections.Generic;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// Procedural 8-bit placeholder art (car, logos, icons, portraits) drawn into point-filtered
    /// textures so they never blur. Swap these for hand-made pixel art any time.
    /// </summary>
    public static class PixelArt
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public const int LogoCount = 12;

        static Sprite Make(string key, int w, int h, System.Func<int, int, Color> pixel)
        {
            Sprite s;
            if (Cache.TryGetValue(key, out s) && s != null) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, h - 1 - y); // (x, y) with y measured from the top
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
            Cache[key] = s;
            return s;
        }

        static bool In(int x, int y, int x0, int y0, int x1, int y1) { return x >= x0 && x <= x1 && y >= y0 && y <= y1; }

        /// <summary>Top-down formula car, nose pointing right. 32 x 15 pixels.</summary>
        public static Sprite Car(Color team)
        {
            string key = "car" + ColorUtility.ToHtmlStringRGB(team);
            Color wing = Theme.Darken(team, 0.7f);
            Color tyre = new Color(0.07f, 0.07f, 0.08f);
            Color clear = new Color(0, 0, 0, 0);
            Color cockpit = new Color(0.05f, 0.05f, 0.06f);
            Color helmet = Color.white;
            Color stripe = Theme.TextOn(team) == Color.black ? Theme.Darken(team, 0.55f) : Color.Lerp(team, Color.white, 0.55f);
            return Make(key, 32, 15, (x, y) =>
            {
                if (In(x, y, 0, 1, 2, 13)) return wing;                       // rear wing
                if (In(x, y, 4, 0, 8, 3) || In(x, y, 4, 11, 8, 14)) return tyre;   // rear tyres
                if (In(x, y, 21, 1, 24, 3) || In(x, y, 21, 11, 24, 13)) return tyre; // front tyres
                if (In(x, y, 28, 1, 30, 13)) return wing;                     // front wing
                if (In(x, y, 14, 6, 15, 8)) return helmet;
                if (In(x, y, 12, 5, 17, 9)) return cockpit;
                if (In(x, y, 3, 7, 29, 7)) return stripe;                    // centre stripe
                if (In(x, y, 3, 5, 27, 9)) return team;                      // main body
                if (In(x, y, 9, 3, 19, 11)) return team;                     // sidepods
                if (In(x, y, 26, 6, 31, 8)) return team;                     // nose
                if (In(x, y, 20, 6, 21, 8) || In(x, y, 25, 6, 25, 8)) return team;
                return clear;
            });
        }

        /// <summary>One of 12 premade pixel emblems, 16 x 16.</summary>
        public static Sprite Logo(int index, Color c)
        {
            index = ((index % LogoCount) + LogoCount) % LogoCount;
            string key = "logo" + index + ColorUtility.ToHtmlStringRGB(c);
            Color clear = new Color(0, 0, 0, 0);
            Color hi = Color.white;
            return Make(key, 16, 16, (x, y) =>
            {
                float cx = x - 7.5f, cy = y - 7.5f;
                float r = Mathf.Sqrt(cx * cx + cy * cy);
                switch (index)
                {
                    case 0: return r <= 7.5f ? (r <= 4f ? hi : c) : clear;                                   // dot
                    case 1: return Mathf.Abs(cx) + Mathf.Abs(cy) <= 8f ? (Mathf.Abs(cx) + Mathf.Abs(cy) <= 3.5f ? hi : c) : clear; // diamond
                    case 2: return (y < 10 ? Mathf.Abs(cx) <= 7 : Mathf.Abs(cx) <= 7 - (y - 9) * 1.2f) ? (In(x, y, 6, 2, 9, 11) ? hi : c) : clear; // shield
                    case 3: return (Mathf.Abs(cx) <= 1.5f || Mathf.Abs(cy) <= 1.5f || Mathf.Abs(cx - cy) <= 1.5f || Mathf.Abs(cx + cy) <= 1.5f) && r <= 7.5f ? c : clear; // star
                    case 4: { int d = Mathf.Abs(x - 8); return (y >= 2 + d && y <= 6 + d) || (y >= 8 + d / 2 && y <= 10 + d / 2 && d < 6) ? c : clear; } // chevron
                    case 5: return (In(x, y, 8, 0, 11, 7) || In(x, y, 4, 7, 11, 8) || In(x, y, 4, 8, 7, 15)) ? c : clear; // bolt
                    case 6: return r <= 7.5f && r >= 5f ? c : (r <= 2.5f ? hi : clear);                     // ring
                    case 7: return (y >= 2 && Mathf.Abs(cx) <= (y - 1) * 0.55f) ? (y > 10 && Mathf.Abs(cx) < 2.5f ? hi : c) : clear; // triangle
                    case 8: return In(x, y, 1, 1, 14, 14) ? (In(x, y, 5, 5, 10, 10) ? hi : (In(x, y, 3, 3, 12, 12) ? clear : c)) : clear; // square
                    case 9: return (In(x, y, 0, 5, 15, 7) || In(x, y, 2, 8, 13, 9) || In(x, y, 5, 10, 10, 11) || In(x, y, 6, 2, 9, 12)) ? (In(x, y, 6, 2, 9, 4) ? hi : c) : clear; // wings
                    case 10: return ((x + y) % 6 < 3) && In(x, y, 1, 1, 14, 14) ? c : clear;               // stripes
                    default: return (Mathf.Abs(cx) <= 2f || Mathf.Abs(cy) <= 2f) && In(x, y, 1, 1, 14, 14) ? c : (r <= 7.5f && r >= 6.5f ? hi : clear); // cross
                }
            });
        }

        static readonly string[] House =
        {
            "................",
            ".......##.......",
            "......####......",
            ".....######.....",
            "....########....",
            "...##########...",
            "..############..",
            ".##############.",
            "...##########...",
            "...##########...",
            "...###....###...",
            "...###....###...",
            "...###....###...",
            "...###....###...",
            "...###....###...",
            "................",
        };

        static readonly string[] Gear =
        {
            "................",
            ".......##.......",
            "...##..##..##...",
            "...##########...",
            "....########....",
            "...###....###...",
            ".####......####.",
            ".###........###.",
            ".###........###.",
            ".####......####.",
            "...###....###...",
            "....########....",
            "...##########...",
            "...##..##..##...",
            ".......##.......",
            "................",
        };

        /// <summary>Smooth anti-aliased disc (track dots and selection rings).</summary>
        public static Sprite Circle()
        {
            Sprite s;
            if (Cache.TryGetValue("circle", out s) && s != null) return s;
            int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(31.5f - d));
                }
            tex.SetPixels(px); tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 1f);
            Cache["circle"] = s;
            return s;
        }

        public static Sprite HouseIcon() { return FromMap("house", House, Color.white); }
        public static Sprite GearIcon() { return FromMap("gear", Gear, Color.white); }

        static Sprite FromMap(string key, string[] map, Color c)
        {
            int h = map.Length, w = map[0].Length;
            return Make(key, w, h, (x, y) => map[y][x] == '#' ? c : new Color(0, 0, 0, 0));
        }

        /// <summary>Pixel helmet portrait in team colors, 24 x 24.</summary>
        public static Sprite Portrait(Color team, int seed)
        {
            string key = "por" + ColorUtility.ToHtmlStringRGB(team) + (seed % 5);
            Color bg = Theme.Darken(team, 0.35f);
            Color visor = new Color(0.08f, 0.1f, 0.14f);
            Color shine = new Color(0.55f, 0.75f, 0.95f);
            Color stripe = Color.white;
            Color suit = Theme.Darken(team, 0.8f);
            int stripeRow = 6 + seed % 5;
            return Make(key, 24, 24, (x, y) =>
            {
                float cx = x - 11.5f, cy = y - 10f;
                if (y >= 19 && Mathf.Abs(cx) <= 9) return suit;                     // shoulders
                if (cx * cx / 64f + cy * cy / 72f <= 1f)
                {
                    if (y >= 9 && y <= 12 && x >= 7 && x <= 19) return (x == 8 && y == 10) || (x == 9 && y == 10) ? shine : visor;
                    if (y == stripeRow) return stripe;
                    return team;
                }
                return bg;
            });
        }
    }
}
