using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>Race classification plus your weekend's points and money. Everything was already committed once.</summary>
    public class ResultsScreen : IScreen
    {
        readonly RaceState s;
        public ResultsScreen(RaceState state) { s = state; }

        public void Build(RectTransform root)
        {
            var c = GameApp.I.Career;
            Ui.FillBox(root, Theme.Background);
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TopBar").rectTransform;
            Ui.Label(bar, "RESULTS  •  " + s.grandPrix.ToUpperInvariant(), Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1920, 80, TextAnchor.MiddleCenter);
            Ui.Label(bar, "TEAM BALANCE", Theme.Small, true, Theme.TextSecondary, 1640, 10, 256, 22, TextAnchor.MiddleRight);
            Ui.Label(bar, Theme.Money(c.balance), Theme.BigNumber, true, c.balance < 0 ? Theme.Red : Theme.Green, 1640, 32, 256, 38, TextAnchor.MiddleRight);

            var lines = RaceSim.Classification(s);
            var p = Ui.Panel(root, 24, 100, 1228, 956, "Classification");
            var table = Table.Create(p, 20, Ui.UnderTitle, 1188, 956 - Ui.UnderTitle - 20,
                new[] { "POS", "DRIVER", "TEAM", "GRID", "LAPS", "TIME / GAP", "STOPS", "PTS" },
                new[] { 7f, 22f, 22f, 7f, 7f, 20f, 7f, 8f }, 38);
            foreach (var l in lines)
            {
                var car = s.cars[l.car];
                var row = table.AddRow(car.mine >= 0 ? Theme.Darken(Theme.Red, 0.55f) : (Color?)null);
                table.Cell(row, 0, l.classified ? l.position.ToString() : "NC", Theme.TextPrimary, true);
                Ui.Box(row, Theme.Hex(car.color), table.colX[1] + 4, 8, 5, 22);
                Ui.Ellipsis(row, car.name + (s.fastestCar == l.car ? "  (fastest lap)" : ""), Theme.Body, car.mine >= 0, Theme.TextPrimary, table.colX[1] + 16, 0, table.colW[1] - 22, 38);
                table.Cell(row, 2, car.teamName, Theme.TextSecondary);
                table.Cell(row, 3, car.grid.ToString());
                table.Cell(row, 4, car.lap.ToString());
                table.Cell(row, 5, l.gap, car.status == 3 ? Theme.Red : Theme.TextPrimary);
                table.Cell(row, 6, car.stops.ToString());
                table.Cell(row, 7, l.points.ToString(), l.points > 0 ? Theme.Green : Theme.TextSecondary, true, TextAnchor.MiddleRight);
            }

            var q = Ui.Panel(root, 1272, 100, 624, 956, "Your Weekend");
            float y = Ui.UnderTitle, w = 584;
            for (int k = 0; k < 2; k++)
            {
                var l = lines.FirstOrDefault(x => s.cars[x.car].mine == k);
                if (l == null) continue;
                var car = s.cars[l.car];
                Ui.Box(q, Theme.RowA, 20, y, w, 56);
                Ui.Box(q, Theme.Hex(car.color), 20, y, 5, 56);
                Ui.Ellipsis(q, "D" + (k + 1) + "  " + car.name, Theme.Body, true, Theme.TextPrimary, 36, y, 300, 56);
                string res = car.status == 3 ? "DNF" : l.classified ? "P" + l.position : "NC";
                Ui.Label(q, res + "  (from P" + car.grid + ")  " + l.points + " PTS", Theme.Body, true, l.points > 0 ? Theme.Green : Theme.TextPrimary, 300, y, w - 296, 56, TextAnchor.MiddleRight);
                y += 62;
            }
            y += 10;
            var sum = c.lastEvent;
            int i = 0;
            LobbyKit.ValueRow(q, 20, y, w, "Prize money", Theme.Money(sum.prize), Theme.Green, i++); y += 52;
            LobbyKit.ValueRow(q, 20, y, w, "Sponsor income (guaranteed)", Theme.Money(sum.sponsorPay), Theme.Green, i++); y += 52;
            LobbyKit.ValueRow(q, 20, y, w, "Sponsor bonuses earned", Theme.Money(sum.bonuses), Theme.Green, i++); y += 52;
            LobbyKit.ValueRow(q, 20, y, w, "Race costs (charged at start)", "-" + Theme.Money(sum.costs), Theme.Red, i++); y += 52;
            LobbyKit.ValueRow(q, 20, y, w, "Net money change", Theme.Money(sum.Net), sum.Net >= 0 ? Theme.Green : Theme.Red, i++); y += 64;

            Ui.Label(q, "SPONSOR TARGETS", Theme.Small, true, Theme.TextSecondary, 20, y, w, 24); y += 28;
            if (sum.lines.Count == 0) { Ui.Label(q, "No sponsor targets this race.", Theme.Small, false, Theme.TextSecondary, 20, y, w, 24); y += 28; }
            foreach (var line in sum.lines.Take(10))
            {
                Ui.Ellipsis(q, line, Theme.Small, false, line.Contains("+$") ? Theme.Green : Theme.TextSecondary, 20, y, w, 24);
                y += 26;
            }
            Ui.Button(q, "RETURN TO DASHBOARD", BtnStyle.Red, 20, 956 - 20 - 48, w, 48, () => GameApp.I.OpenCareer(GameApp.I.Career), 18);
        }
    }
}
