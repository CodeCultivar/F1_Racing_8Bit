using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// End of season review. Shows the final result, what changed over the winter (people, cars, the paddock)
    /// and asks for a decision on every contract that ran out. Nothing is saved until START SEASON is pressed.
    /// </summary>
    public class SeasonEndScreen : IScreen
    {
        readonly CareerSave next;
        readonly SeasonRollover.Review rv;
        readonly Dictionary<string, int> decisions = new Dictionary<string, int>();   // id -> years (0 = release)

        public SeasonEndScreen()
        {
            next = Rules.Clone(GameApp.I.Career);
            rv = SeasonRollover.Run(next);
        }

        public void Build(RectTransform root)
        {
            var c = GameApp.I.Career;
            Ui.FillBox(root, Theme.Background);
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TopBar").rectTransform;
            var home = Ui.Button(bar, "", BtnStyle.Dark, 24, 16, 48, 48, () => GameApp.I.OpenCareer(c));
            Ui.Icon(home.transform, PixelArt.HouseIcon(), 8, 8, 32, 32);
            Ui.Label(bar, "SEASON " + rv.season + " REVIEW", Theme.ScreenTitle, true, Theme.TextPrimary, 160, 0, 1600, 80, TextAnchor.MiddleCenter);
            Ui.Label(bar, "TEAM BALANCE", Theme.Small, true, Theme.TextSecondary, 1640, 10, 256, 22, TextAnchor.MiddleRight);
            Ui.Label(bar, Theme.Money(next.balance - Fees()), Theme.BigNumber, true, next.balance - Fees() < 0 ? Theme.Red : Theme.Green, 1640, 32, 256, 38, TextAnchor.MiddleRight);

            Result(Ui.Panel(root, 24, 100, 600, 956, "Season " + rv.season + " Result"), 600, 956);
            Contracts(Ui.Panel(root, 644, 100, 632, 560, "Contracts Ending"), 632, 560);
            Car(Ui.Panel(root, 644, 680, 632, 376, "Your Car For Season " + (rv.season + 1)), 632, 376);
            Paddock(Ui.Panel(root, 1296, 100, 600, 956, "Around The Paddock"), 600, 956);
        }

        long Fees() { return rv.expiring.Where(e => decisions.ContainsKey(e.id) && decisions[e.id] > 0).Sum(e => e.fee); }

        // ------------------------------------------------------------------ result + your people

        void Result(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle;
            string champ = string.IsNullOrEmpty(ConfigLoader.Config.championshipName) ? "THE CHAMPIONSHIP" : ConfigLoader.Config.championshipName.ToUpperInvariant();
            Ui.Ellipsis(p, rv.teamPos > 0 ? "P" + rv.teamPos + " OF " + rv.teamCount : "NOT RANKED", Theme.BigNumber, true, rv.teamChamp ? Theme.Yellow : Theme.TextPrimary, 20, y, w - 40, 44);
            y += 44;
            Ui.Label(p, rv.teamPoints + " POINTS  •  " + champ, Theme.Small, true, Theme.Green, 20, y, w - 40, 24);
            y += 34;
            if (rv.teamChamp) { Ui.Label(p, "CONSTRUCTORS' CHAMPIONS!", Theme.PanelTitle, true, Theme.Yellow, 20, y, w - 40, 30); y += 34; }
            if (rv.driverChamp) { Ui.Label(p, "YOUR DRIVER IS WORLD CHAMPION!", Theme.PanelTitle, true, Theme.Yellow, 20, y, w - 40, 30); y += 34; }
            int i = 0;
            LobbyKit.ValueRow(p, 20, y, w - 40, "Team champions", rv.champTeam, Theme.TextPrimary, i++); y += 52;
            LobbyKit.ValueRow(p, 20, y, w - 40, "Drivers' champion", rv.champDriver, Theme.TextPrimary, i++); y += 52;
            LobbyKit.ValueRow(p, 20, y, w - 40, "Season sponsor bonuses", Theme.Money(rv.bonusTotal), Theme.Green, i++); y += 56;
            foreach (var l in rv.bonusLines.Take(3)) { Ui.Ellipsis(p, l, Theme.Small, false, l.Contains("+$") ? Theme.Green : Theme.TextSecondary, 20, y, w - 40, 22); y += 24; }

            y += 8;
            Ui.Label(p, "YOUR TEAM OVER THE WINTER", Theme.Small, true, Theme.TextSecondary, 20, y, w - 40, 24);
            y += 28;
            foreach (var pc in rv.yourPeople)
            {
                Ui.Box(p, Theme.RowA, 20, y, w - 40, 40);
                Ui.Ellipsis(p, pc.name, Theme.Small, true, Theme.TextPrimary, 32, y, 220, 40);
                Ui.Label(p, pc.role + " • age " + pc.age, 13, false, Theme.TextSecondary, 256, y, 160, 40);
                float d = pc.after - pc.before;
                string txt = Rules.Fmt(pc.before) + " → " + Rules.Fmt(pc.after);
                Ui.Label(p, txt, Theme.Small, true, float.IsNaN(d) || Mathf.Abs(d) < 0.05f ? Theme.TextPrimary : d > 0 ? Theme.Green : Theme.Red, w - 20 - 170, y, 158, 40, TextAnchor.MiddleRight);
                y += 44;
            }
            foreach (var r in rv.yourRetired)
            {
                Ui.Ellipsis(p, "RETIRED: " + r, Theme.Small, true, Theme.Red, 20, y, w - 40, 24);
                y += 26;
            }
            if (rv.yourRetired.Count > 0)
                Ui.Label(p, "Fill the empty seats in Transfers before the first race.", Theme.Small, false, Theme.Yellow, 20, y, w - 40, 24);
        }

        // ------------------------------------------------------------------ contracts

        void Contracts(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle;
            if (rv.expiring.Count == 0)
            {
                Ui.Label(p, "No contracts end this winter. Everyone stays.", Theme.Body, false, Theme.TextSecondary, 20, y, w - 40, 30);
                return;
            }
            Ui.Label(p, "Renew each person (new salary from their current rating, plus a renewal fee of 3 races' salary) or release them.", Theme.Small, false, Theme.TextSecondary, 20, y - 6, w - 40, 44, TextAnchor.UpperLeft);
            y += 44;
            var area = Ui.Scroll(p, 20, y, w - 40, h - y - 20);
            float ry = 0, rw = area.width;
            foreach (var e in rv.expiring)
            {
                var ex = e;
                int choice;
                bool decided = decisions.TryGetValue(e.id, out choice);
                Ui.Box(area.content, Theme.RowA, 0, ry, rw, 108);
                Ui.Ellipsis(area.content, e.name, Theme.Body, true, Theme.TextPrimary, 12, ry + 4, rw - 200, 28);
                Ui.Label(area.content, KindLabel(e) + " • age " + e.age + " • OVR " + Rules.Fmt(e.overall), 13, false, Theme.TextSecondary, 12, ry + 30, rw - 24, 20);
                Ui.Label(area.content, Theme.Money(e.oldSalary) + " → " + Theme.Money(e.newSalary) + " / RACE", Theme.Small, true, e.newSalary > e.oldSalary ? Theme.Red : Theme.Green, rw - 300, ry + 4, 288, 28, TextAnchor.MiddleRight);
                Ui.Label(area.content, "Renewal fee " + Theme.Money(e.fee), 13, false, Theme.TextSecondary, rw - 300, ry + 30, 288, 20, TextAnchor.MiddleRight);
                float bw = (rw - 24 - 18) / 4f;
                for (int k = 0; k < 4; k++)
                {
                    int years = k == 3 ? 0 : k + 1;
                    string label = years == 0 ? "RELEASE" : "RENEW " + years + (years == 1 ? " YR" : " YRS");
                    bool picked = decided && choice == years;
                    var style = !picked ? BtnStyle.Dark : years == 0 ? BtnStyle.Red : BtnStyle.Green;
                    Ui.Button(area.content, label, style, 12 + k * (bw + 6), ry + 58, bw, 40, () => { decisions[ex.id] = years; GameApp.I.RefreshCurrent(); }, 13);
                }
                ry += 116;
            }
            area.SetHeight(ry);
        }

        static string KindLabel(SeasonRollover.Expiring e)
        {
            return e.kind == PersonKind.Driver ? "Driver " + (e.slot + 1) : e.kind == PersonKind.Engineer ? "Engineer " + (e.slot + 1) : "Coordinator";
        }

        // ------------------------------------------------------------------ car

        void Car(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle - 8;
            if (!SeasonRollover.CarRulesSet)
            {
                Ui.Label(p, "Car development rules " + Theme.NotConfigured.ToLowerInvariant() + " - your car carries over unchanged.", Theme.Small, false, Theme.Yellow, 20, y, w - 40, 40, TextAnchor.UpperLeft);
                return;
            }
            var R = ConfigLoader.Config.seasonRules;
            Ui.Label(p, "New regulations: every part -" + R.winterLoss + ".  Fund spent: " + Theme.Money(rv.fundSpent), Theme.Small, true, Theme.TextSecondary, 20, y, w - 40, 24);
            y += 28;
            for (int i = 0; i < 6; i++)
            {
                Ui.Box(p, i % 2 == 0 ? Theme.RowA : Theme.RowB, 20, y, w - 40, 34);
                Ui.Label(p, Stat.Part[i], Theme.Small, false, Theme.TextPrimary, 32, y, 200, 34);
                Ui.Label(p, rv.fundGain[i] > 0 ? "fund +" + rv.fundGain[i] : "fund +0", 13, false, rv.fundGain[i] > 0 ? Theme.Green : Theme.TextSecondary, 240, y, 120, 34);
                int d = rv.carAfter[i] - rv.carBefore[i];
                Ui.Label(p, rv.carBefore[i] + " → " + rv.carAfter[i] + "  (" + (d >= 0 ? "+" : "") + d + ")", Theme.Small, true, d > 0 ? Theme.Green : d < 0 ? Theme.Red : Theme.TextPrimary, w - 20 - 220, y, 208, 34, TextAnchor.MiddleRight);
                y += 36;
            }
            Ui.Label(p, "CAR OVERALL  " + Theme.OneDecimal((float)rv.carBefore.Average()) + " → " + Theme.OneDecimal((float)rv.carAfter.Average()), Theme.Small, true, Theme.TextPrimary, 20, y + 4, w - 40, 26, TextAnchor.MiddleRight);
        }

        // ------------------------------------------------------------------ paddock

        void Paddock(Transform p, float w, float h)
        {
            float top = Ui.UnderTitle;
            var area = Ui.Scroll(p, 20, top, w - 40, h - top - 20 - 48 - 70);
            float y = 0, rw = area.width;
            System.Action<string, List<string>, Color> section = (title, lines, col) =>
            {
                Ui.Label(area.content, title, Theme.Small, true, Theme.TextSecondary, 0, y, rw, 24);
                y += 26;
                if (lines.Count == 0) { Ui.Label(area.content, "None", 13, false, Theme.TextSecondary, 8, y, rw - 8, 22); y += 24; }
                foreach (var l in lines) { Ui.Ellipsis(area.content, l, 13, false, col, 8, y, rw - 8, 22); y += 24; }
                y += 10;
            };
            section("RETIREMENTS", rv.retired, Theme.TextPrimary);
            section("NEW F2 / F3 GRADUATES IN FREE AGENCY", rv.rookies, Theme.Green);
            section("TRANSFERS", rv.moves, Theme.TextPrimary);
            section("RIVAL CARS (OVERALL, BEFORE DIFFICULTY)", rv.rivalCars, Theme.TextPrimary);
            area.SetHeight(y);

            bool ready = rv.expiring.All(e => decisions.ContainsKey(e.id));
            long after = next.balance - Fees();
            string note = !ready ? "Decide on every contract that ended first." : after < 0 ? "Not enough money for those renewals." : "Training, study and upgrades in progress carry over.";
            Ui.Label(p, note, Theme.Small, true, !ready || after < 0 ? Theme.Yellow : Theme.TextSecondary, 20, h - 20 - 48 - 30, w - 40, 24, TextAnchor.MiddleCenter);
            var b = Ui.Button(p, "START SEASON " + (rv.season + 1), BtnStyle.Red, 20, h - 20 - 48, w - 40, 48, Start, 20);
            b.SetEnabled(ready && after >= 0);
        }

        void Start()
        {
            SeasonRollover.ApplyDecisions(next, rv, decisions);
            string json = JsonUtility.ToJson(next);
            GameApp.I.Commit(cs => JsonUtility.FromJsonOverwrite(json, cs), () => GameApp.I.OpenCareer(GameApp.I.Career));
        }
    }
}
