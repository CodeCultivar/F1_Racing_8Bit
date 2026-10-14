using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// Pause between sessions: best laps, laps completed, tyre sets used, issues, what the team now knows
    /// about the tyres, and the coordinator's updated race plan. Continue opens the next session.
    /// </summary>
    public class SessionSummaryScreen : IScreen
    {
        readonly RaceState s;
        public SessionSummaryScreen(RaceState state) { s = state; }

        static readonly string[] NextLabel = { "CONTINUE TO PRACTICE 2", "CONTINUE TO PRACTICE 3", "CONTINUE TO QUALIFYING", "CONTINUE TO Q2", "CONTINUE TO Q3", "CONTINUE TO THE GRID" };

        public void Build(RectTransform root)
        {
            var c = GameApp.I.Career;
            var t = Track.Load(s.trackId);
            bool quali = Session.IsQuali(s);
            Ui.FillBox(root, Theme.Background);
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TopBar").rectTransform;
            var home = Ui.Button(bar, "", BtnStyle.Dark, 24, 16, 48, 48, () => { GameApp.I.Career = null; GameApp.I.Show(new OpeningScreen()); });
            Ui.Icon(home.transform, PixelArt.HouseIcon(), 8, 8, 32, 32);
            Ui.Label(bar, s.grandPrix.ToUpperInvariant() + "  •  " + Session.Names[s.session] + " RESULTS", Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1920, 80, TextAnchor.MiddleCenter);

            // ---- classification
            var p = Ui.Panel(root, 24, 100, 1100, 956, quali ? "Qualifying" : "Times");
            var order = RaceSim.Order(s);
            float fastest = order.Count > 0 ? Session.SegBest(s, s.cars[order[0]]) : 0;
            var table = Table.Create(p, 20, Ui.UnderTitle, 1060, 956 - Ui.UnderTitle - 20,
                new[] { "POS", "DRIVER", "TEAM", "BEST LAP", "GAP", "LAPS", quali ? "STATUS" : "NEW SETS" },
                new[] { 7f, 23f, 22f, 15f, 12f, 8f, 13f }, 36);
            int active = Session.Active(s);
            for (int k = 0; k < order.Count; k++)
            {
                var car = s.cars[order[k]];
                float best = quali && car.qOutSeg > 0 ? (car.qOutSeg == 2 ? car.q2 : car.q1) : Session.SegBest(s, car);
                var row = table.AddRow(car.mine >= 0 ? Theme.Darken(Theme.Red, 0.55f) : (Color?)null);
                table.Cell(row, 0, (k + 1).ToString(), Theme.TextPrimary, true);
                Ui.Box(row, Theme.Hex(car.color), table.colX[1] + 4, 8, 5, 20);
                Ui.Ellipsis(row, car.name, Theme.Body, car.mine >= 0, Theme.TextPrimary, table.colX[1] + 16, 0, table.colW[1] - 22, 36);
                table.Cell(row, 2, car.teamName, Theme.TextSecondary);
                table.Cell(row, 3, best > 0 ? RaceSim.Lap(best) : "NO TIME");
                table.Cell(row, 4, best > 0 && fastest > 0 && k > 0 && car.qOutSeg == 0 ? "+" + (best - fastest).ToString("0.000") : "");
                table.Cell(row, 5, car.sLaps.ToString());
                string st = quali ? (car.qOutSeg == 1 ? "OUT IN Q1" : car.qOutSeg == 2 ? "OUT IN Q2" : s.session == Session.Q3 ? "GRID P" + (k + 1) : "THROUGH") : car.setsUsedNew.ToString();
                table.Cell(row, 6, st, car.qOutSeg > 0 ? Theme.Red : Theme.TextPrimary, quali);
            }

            // ---- your team
            var q = Ui.Panel(root, 1144, 100, 752, 956, "Your Team");
            float y = Ui.UnderTitle, w = 712;
            for (int k = 0; k < 2; k++)
            {
                int idx = s.cars.FindIndex(x => x.mine == k);
                if (idx < 0) continue;
                var car = s.cars[idx];
                int pos = order.IndexOf(idx) + 1;
                float best = quali && car.qOutSeg > 0 ? (car.qOutSeg == 2 ? car.q2 : car.q1) : Session.SegBest(s, car);
                Ui.Box(q, Theme.RowA, 20, y, w, 74);
                Ui.Box(q, Theme.Hex(car.color), 20, y, 5, 74);
                Ui.Label(q, "D" + (k + 1) + "  " + car.name, Theme.Body, true, Theme.TextPrimary, 36, y + 4, 360, 28);
                Ui.Label(q, "P" + pos + "   " + (best > 0 ? RaceSim.Lap(best) : "No time"), Theme.Body, true, Theme.Green, 380, y + 4, w - 376, 28, TextAnchor.MiddleRight);
                int usedS = car.sets.Count(x => x.used && x.compound == Tyre.Soft), usedM = car.sets.Count(x => x.used && x.compound == Tyre.Medium), usedH = car.sets.Count(x => x.used && x.compound == Tyre.Hard);
                string line = car.sLaps + " timed laps  •  sets used S" + usedS + "/8  M" + usedM + "/3  H" + usedH + "/2";
                if (!string.IsNullOrEmpty(car.issue)) line += "  •  " + car.issue;
                Ui.Ellipsis(q, line, Theme.Small, false, string.IsNullOrEmpty(car.issue) ? Theme.TextSecondary : Theme.Yellow, 36, y + 38, w - 40, 26);
                y += 82;
            }

            int mineIdx = s.cars.FindIndex(x => x.mine == 0);
            if (mineIdx >= 0)
            {
                var me = s.cars[mineIdx];
                y += 6;
                Ui.Label(q, "TYRE KNOWLEDGE  (how the tyres will hold up here)", Theme.Small, true, Theme.TextSecondary, 20, y, w, 24); y += 30;
                for (int comp = 0; comp < 3; comp++)
                {
                    float conf = Session.Confidence(s, me, comp);
                    float life = Session.EstimatedLife(s, me, comp);
                    float spread = life * Mathf.Abs(me.lifeErr[comp]) * (1 - conf) + 1.5f * (1 - conf);
                    Ui.Box(q, Theme.RowA, 20, y, w, 46);
                    Ui.Box(q, Theme.Hex(Tyre.Hex[comp]), 32, y + 15, 16, 16);
                    Ui.Label(q, Tyre.Name[comp], Theme.Body, true, Theme.TextPrimary, 58, y, 140, 46);
                    Ui.Label(q, "~" + Mathf.RoundToInt(life) + " laps" + (conf < 0.95f ? " (±" + Mathf.Max(1, Mathf.RoundToInt(spread)) + ")" : ""), Theme.Body, false, Theme.TextPrimary, 200, y, 200, 46);
                    Ui.Label(q, "CONFIDENCE", 12, true, Theme.TextSecondary, 410, y, 100, 46);
                    Ui.Bar(q, 510, y + 17, 150, 12, conf * 100f, conf > 0.66f ? Theme.Green : conf > 0.33f ? Theme.Yellow : Theme.Red);
                    Ui.Label(q, Mathf.RoundToInt(conf * 100) + "%", Theme.Small, true, Theme.TextPrimary, 664, y, 60, 46);
                    y += 50;
                }
                Ui.Label(q, "Laps are counted until a set reaches about 85% wear at balanced pace. Practice laps on a compound raise confidence; no running means no new knowledge.", Theme.Small, false, Theme.TextSecondary, 20, y, w, 44, TextAnchor.UpperLeft);
                y += 52;

                Ui.Label(q, "COORDINATOR'S RACE PLAN (UPDATED)", Theme.Small, true, Theme.TextSecondary, 20, y, w, 24); y += 30;
                for (int k = 0; k < 2; k++)
                {
                    int idx = s.cars.FindIndex(x => x.mine == k);
                    if (idx < 0) continue;
                    var car = s.cars[idx];
                    Coordinator.Plan(s, car, -1);
                    string seq = string.Join(" > ", car.plan.Select(st => Tyre.Letter[st.compound] + " to L" + st.endLap));
                    Ui.Ellipsis(q, car.name + ":  " + seq, Theme.Small, true, Theme.TextPrimary, 20, y, w, 24);
                    y += 26;
                }
            }

            var cont = Ui.Button(q, NextLabel[Mathf.Clamp(s.session, 0, 5)], BtnStyle.Red, 20, 956 - 20 - 48, w, 48, () => Continue(t), 18);
            if (Session.IsPractice(s) && s.session < Session.FP3)
                Ui.Button(q, "SKIP REMAINING PRACTICE", BtnStyle.Dark, 20, 956 - 20 - 48 - 56, w, 48, () => SkipPractice(t), 16);
        }

        void Continue(Track t)
        {
            if (s.session >= Session.Q3)
            {
                Session.StartRace(s, t);
                RaceFlow.Persist(GameApp.I.Career, s, () => GameApp.I.Show(new GridScreen(s)));
                return;
            }
            Session.Begin(s, t, s.session + 1);
            RaceFlow.Persist(GameApp.I.Career, s, () => GameApp.I.Show(new RaceScreen(s)));
        }

        /// <summary>Runs every remaining practice session instantly with the coordinator in charge.</summary>
        void SkipPractice(Track t)
        {
            while (s.session < Session.FP3)
            {
                Session.Begin(s, t, s.session + 1);
                int guard = 0;
                while (!s.sessionEnded && guard++ < 40000) RaceSim.Step(s, t, 0.25f);
            }
            RaceFlow.Persist(GameApp.I.Career, s, () => GameApp.I.Show(new SessionSummaryScreen(s)));
        }
    }
}
