using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>Starting Grid and Game Plan: the final grid plus both of your drivers' plans before the race.</summary>
    public class GridScreen : IScreen
    {
        readonly RaceState s;
        public GridScreen(RaceState state) { s = state; }

        public void Build(RectTransform root)
        {
            var c = GameApp.I.Career;
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TopBar").rectTransform;
            var home = Ui.Button(bar, "", BtnStyle.Dark, 24, 16, 48, 48, () => RaceFlow.Persist(GameApp.I.Career, s, () => { GameApp.I.Career = null; GameApp.I.Show(new OpeningScreen()); }));
            Ui.Icon(home.transform, PixelArt.HouseIcon(), 8, 8, 32, 32);
            Ui.Label(bar, s.grandPrix.ToUpperInvariant() + "  •  STARTING GRID AND GAME PLAN", Theme.ScreenTitle, true, Theme.TextPrimary, 160, 0, 1600, 80, TextAnchor.MiddleCenter);

            // ---- grid
            var gp = Ui.Panel(root, 24, 100, 1100, 956, "Starting Grid");
            Ui.Label(gp, "Grid set by Q1-Q3 knockout qualifying. Tyre wear from practice and qualifying carries into the race.", Theme.Small, false, Theme.Yellow, 20, 52, 1060, 24);
            float colW = (1100 - 60) / 2f, rowH = 70;
            for (int p = 0; p < s.gridOrder.Count; p++)
            {
                var car = s.cars[s.gridOrder[p]];
                float x = 20 + (p % 2) * (colW + 20), y = 90 + (p / 2) * rowH + (p % 2) * 30;
                Color bg = car.mine >= 0 ? Theme.Darken(Theme.Red, 0.55f) : Theme.RowA;
                Ui.Box(gp, bg, x, y, colW, rowH - 34);
                Ui.Box(gp, Theme.TableHeader, x, y, 40, rowH - 34);
                Ui.Label(gp, (p + 1).ToString(), Theme.Body, true, Theme.Dark, x, y, 40, rowH - 34, TextAnchor.MiddleCenter);
                Ui.Box(gp, Theme.Hex(car.color), x + 46, y + 6, 6, rowH - 46);
                Ui.Ellipsis(gp, car.name, Theme.Body, true, Theme.TextPrimary, x + 62, y, colW * 0.5f, rowH - 34);
                Ui.Ellipsis(gp, car.teamName, Theme.Small, false, Theme.TextSecondary, x + 62 + colW * 0.5f, y, colW * 0.5f - 72, rowH - 34, TextAnchor.MiddleRight);
            }

            // ---- plans
            var pp = Ui.Panel(root, 1144, 100, 752, 956, "Game Plan");
            float py = 60;
            for (int k = 0; k < 2; k++)
            {
                int idx = s.cars.FindIndex(x => x.mine == k);
                if (idx < 0) continue;
                PlanCard(pp, idx, 20, py, 712, 360);
                py += 376;
            }
            Ui.Label(pp, "Formation lap, then five red lights. The drivers launch themselves.", Theme.Small, false, Theme.TextSecondary, 20, 956 - 20 - 48 - 34, 712, 26);
            // weather briefing
            int f5 = Mathf.RoundToInt(RaceControl.Forecast(s, 5) * 100), f15 = Mathf.RoundToInt(RaceControl.Forecast(s, 15) * 100);
            string cond = s.rain > 0.05f ? "Raining" : s.wetness > 0.07f ? "Wet track" : s.wetness > 0.02f ? "Damp track" : "Dry track";
            Ui.Label(pp, "WEATHER: " + cond + " (" + Mathf.RoundToInt(s.wetness * 100) + "% wet)  •  Rain chance: " + f5 + "% in 5 min, " + f15 + "% in 15 min", Theme.Small, true, f5 >= 50 || s.wetness > 0.07f ? Theme.Hex("#4FC3F7") : Theme.TextSecondary, 20, 956 - 20 - 48 - 66, 712, 26);
            Ui.Button(pp, "BEGIN RACE", BtnStyle.Red, 20, 956 - 20 - 48, 712, 48, Begin, 20);
        }

        void PlanCard(Transform p, int idx, float x, float y, float w, float h)
        {
            var car = s.cars[idx];
            Ui.Box(p, Theme.RowA, x, y, w, h);
            Ui.Label(p, "DRIVER " + (car.mine + 1) + "  •  " + car.name.ToUpperInvariant(), Theme.PanelTitle, true, Theme.TextPrimary, x + 16, y + 10, w - 160, 32);
            Ui.Label(p, "P" + car.grid, Theme.BigNumber, true, Theme.Green, x + w - 140, y + 8, 124, 36, TextAnchor.MiddleRight);

            Ui.Label(p, "STARTING TYRES", Theme.Small, true, Theme.TextSecondary, x + 16, y + 56, 300, 24);
            int cur = car.set >= 0 ? car.sets[car.set].compound : -1;
            for (int c = 0; c < 5; c++)
            {
                int comp = c;
                var b = Ui.Button(p, c <= Tyre.Hard ? Tyre.Name[c].ToUpperInvariant() : c == Tyre.Inter ? "INTER" : "WET", BtnStyle.Dark, x + 16 + c * 138, y + 84, 130, 40, () => ChangeStart(idx, comp), 15);
                b.customFill = Theme.Hex(Tyre.Hex[c]);
                b.outlineOverride = comp == cur ? (Color?)Color.white : null;
                b.Apply();
                b.SetEnabled(car.sets.Any(ts => ts.compound == comp));
                if (comp == cur) Ui.Box(p, Color.white, x + 16 + c * 138, y + 126, 130, 4);
            }

            Ui.Label(p, "COORDINATOR'S PLAN", Theme.Small, true, Theme.TextSecondary, x + 16, y + 146, 300, 24);
            float sy = y + 174;
            int start = 1;
            for (int i = 0; i < car.plan.Count; i++)
            {
                var st = car.plan[i];
                string txt = "Stint " + (i + 1) + ":  " + Tyre.Name[st.compound] + "  laps " + start + "-" + st.endLap;
                Ui.Box(p, Theme.Hex(Tyre.Hex[st.compound]), x + 16, sy + 6, 14, 14);
                Ui.Label(p, txt, Theme.Body, false, Theme.TextPrimary, x + 40, sy, w - 56, 26);
                if (i < car.plan.Count - 1) Ui.Label(p, "pit window ~lap " + st.endLap, Theme.Small, false, Theme.TextSecondary, x + w - 260, sy, 244, 26, TextAnchor.MiddleRight);
                start = st.endLap + 1;
                sy += 30;
            }
            Ui.Label(p, car.planReason, Theme.Small, false, Theme.TextSecondary, x + 16, sy + 6, w - 32, 60, TextAnchor.UpperLeft);
            int used = 0;
            foreach (var st in car.plan) used |= 1 << st.compound;
            int distinct = 0; for (int b2 = 0; b2 < 3; b2++) if ((used & (1 << b2)) != 0) distinct++;
            if (cur >= Tyre.Inter) Ui.Label(p, "Wet start: the plan's slick stints begin once the track dries. Two-compound rule does not apply.", Theme.Small, true, Theme.Hex("#4FC3F7"), x + 16, y + h - 34, w - 32, 24);
            else if (distinct < 2) Ui.Label(p, "Warning: plan uses only one dry compound.", Theme.Small, true, Theme.Yellow, x + 16, y + h - 34, w - 32, 24);
            else Ui.Label(p, "Rules: at least two different dry compounds must be used.", Theme.Small, false, Theme.TextSecondary, x + 16, y + h - 34, w - 32, 24);
        }

        void ChangeStart(int idx, int compound)
        {
            var car = s.cars[idx];
            if (car.set >= 0 && car.sets[car.set].compound == compound) return;
            if (compound >= Tyre.Inter)
            {
                RaceSim.FitWetStart(car, compound);
                GameApp.I.RefreshCurrent();
                return;
            }
            car.usedWet = false;
            Coordinator.Plan(s, car, compound);
            RaceSim.FitStartSet(car);
            GameApp.I.RefreshCurrent();
        }

        void Begin()
        {
            s.phase = 1;
            s.lightsOn = 0;
            s.lightsTimer = -1.5f;   // short formation pause before the first light
            RaceFlow.Persist(GameApp.I.Career, s, () => GameApp.I.Show(new RaceScreen(s)));
        }
    }
}
