using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    // =====================================================================
    public static class DashboardTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            float w = (1872 - 40) / 3f;
            TeamOverview(Ui.Panel(root, 0, 0, w, 280, "Team Overview"), c, w);
            NextEvent(Ui.Panel(root, w + 20, 0, w, 280, "Next Event"), c, w);
            LeaguePosition(Ui.Panel(root, 2 * (w + 20), 0, w, 280, "League Position"), c, w);
            Finances(Ui.Panel(root, 0, 300, w * 2 + 20, 580, "Finances"), c, w * 2 + 20);
            Reports(Ui.Panel(root, 2 * (w + 20), 300, w, 580, "Team Reports"), c, w);
        }

        static void TeamOverview(Transform p, CareerSave c, float w)
        {
            Ui.Ellipsis(p, c.teamName, Theme.Body, true, Theme.TextSecondary, 20, Ui.UnderTitle, w - 40, 26);
            // centre the car in the remaining space, whole-number scale so pixels stay square
            float top = Ui.UnderTitle + 30, avail = 280 - 20 - top;
            int scale = Mathf.Max(1, Mathf.Min((int)((w - 40) / 32), (int)(avail / 15)));
            float cw = 32 * scale, ch = 15 * scale;
            Ui.Icon(p, PixelArt.Car(Theme.Hex(c.teamColor)), (w - cw) / 2f, top + (avail - ch) / 2f, cw, ch);
        }

        static readonly string[] Words = { "Easy", "Medium", "Hard" };

        static void NextEvent(Transform p, CareerSave c, float w)
        {
            var e = Rules.NextEvent(c);
            float y = Ui.UnderTitle;
            if (e == null)
            {
                string msg = Rules.Rounds == 0 ? "Calendar " + Theme.NotConfigured.ToLowerInvariant() : "Season complete";
                Ui.Label(p, msg, Theme.Body, false, Theme.TextSecondary, 20, y, w - 40, 26);
            }
            else
            {
                Ui.Label(p, (e.laps < 0 ? "-- LAPS" : e.laps + " LAPS") + " • ROUND " + c.nextRound + " OF " + Rules.Rounds, Theme.Small, true, Theme.TextSecondary, 20, y - 2, w - 40, 26, TextAnchor.MiddleRight);
                Ui.Ellipsis(p, string.IsNullOrEmpty(e.grandPrix) ? "Grand Prix " + Theme.NotConfigured.ToLowerInvariant() : e.grandPrix, Theme.Body, true, Theme.TextPrimary, 20, y, w * 0.55f, 26);
                Ui.Ellipsis(p, string.IsNullOrEmpty(e.circuit) ? "--" : e.circuit, Theme.Small, false, Theme.TextSecondary, 20, y + 24, w - 40, 22);
                string[] labels = { "Overtaking Difficulty", "Tire Wear", "Cornering Demand" };
                string[] vals = { e.overtaking, e.tireWear, e.cornering };
                float ry = y + 52;
                for (int i = 0; i < 3; i++)
                {
                    int lvl = Rules.Level(vals[i]);
                    Ui.Label(p, labels[i], Theme.Small, false, Theme.TextPrimary, 20, ry, 220, 24);
                    Ui.Difficulty(p, w - 20 - 80 - 12 - 3 * 28 - 8, ry + 7, 28, 10, lvl);
                    Ui.Label(p, lvl < 0 ? "--" : Words[lvl], Theme.Small, true, lvl == 0 ? Theme.Green : lvl == 1 ? Theme.Yellow : lvl == 2 ? Theme.Red : Theme.TextSecondary, w - 20 - 80, ry, 80, 24, TextAnchor.MiddleRight);
                    ry += 25;
                }
            }
            if (SeasonRollover.SeasonComplete(c))
            {
                Ui.Label(p, "All " + Rules.Rounds + " rounds completed. Review the season, settle contracts and move on to Season " + (c.season + 1) + ".", Theme.Small, false, Theme.TextSecondary, 20, y + 30, w - 40, 60, TextAnchor.UpperLeft);
                Ui.Button(p, "SEASON REVIEW  •  START SEASON " + (c.season + 1), BtnStyle.Yellow, 20, 280 - 20 - 40 - 20, w - 40, 40, () => GameApp.I.Show(new SeasonEndScreen()), 15);
                Ui.Label(p, "Your career can continue for as many seasons as you like", Theme.Small, false, Theme.TextSecondary, 20, 280 - 20 - 20, w - 40, 20, TextAnchor.MiddleCenter);
                return;
            }
            // Opens the next unfinished session; costs are charged once when a new weekend starts.
            bool resume = RaceFlow.HasActive(c);
            string why = resume ? null : RaceSim.CannotStartReason(c);
            var b = Ui.Button(p, resume ? "RESUME RACE WEEKEND" : "START RACE WEEKEND", BtnStyle.Green, 20, 280 - 20 - 40 - 20, w - 40, 40, () => RaceFlow.StartOrResume(c));
            b.SetEnabled(why == null);
            string note = resume ? "Weekend in progress - costs already paid" : why ?? "Race costs are charged when the weekend starts";
            Ui.Label(p, note, Theme.Small, false, why != null ? Theme.Yellow : Theme.TextSecondary, 20, 280 - 20 - 20, w - 40, 20, TextAnchor.MiddleCenter);
        }

        static void LeaguePosition(Transform p, CareerSave c, float w)
        {
            var cfg = ConfigLoader.Config;
            string champ = string.IsNullOrEmpty(cfg.championshipName) ? "CHAMPIONSHIP" : cfg.championshipName.ToUpperInvariant();
            float y = Ui.UnderTitle + 10;
            if (!Rules.HasResults(c))
            {
                Ui.Label(p, "NOT YET RANKED", Theme.BigNumber, true, Theme.TextPrimary, 20, y, w - 40, 56);
                Ui.Label(p, "0 POINTS", Theme.Body, true, Theme.Green, 20, y + 60, w - 40, 40);
            }
            else
            {
                var st = Rules.TeamStandings(c);
                int pos = st.FindIndex(r => r.mine) + 1;
                Ui.Ellipsis(p, "P" + pos + " IN " + champ, Theme.BigNumber, true, Theme.TextPrimary, 20, y, w - 40, 56);
                Ui.Label(p, st[pos - 1].points + " POINTS", Theme.Body, true, Theme.Green, 20, y + 60, w - 40, 40);
            }
            Ui.Ellipsis(p, string.IsNullOrEmpty(cfg.championshipName) ? "Championship " + Theme.NotConfigured.ToLowerInvariant() : cfg.championshipName, Theme.Body, false, Theme.TextSecondary, 20, y + 112, w - 40, 26);
            Ui.Label(p, Rules.Rounds > 0 ? Rules.Rounds + " ROUNDS IN THE SEASON" : "Rounds " + Theme.NotConfigured.ToLowerInvariant(), Theme.Small, false, Theme.TextSecondary, 20, y + 140, w - 40, 24);
            Ui.Label(p, "OPPONENT LEVEL: " + Rules.DifficultyName(c).ToUpperInvariant(), Theme.Small, true, Theme.Yellow, 20, y + 166, w - 40, 24);
        }

        static void Finances(Transform p, CareerSave c, float w)
        {
            var f = Rules.Finances(c);
            float y = Ui.UnderTitle, rw = w - 40;
            int i = 0;
            System.Action<string, string, Color> row = (l, v, col) => { LobbyKit.ValueRow(p, 20, y, rw, l, v, col, i++); y += 52; };
            row("Active Sponsors", f.activeSponsors == 0 ? "No Active Sponsors" : f.activeSponsors + " / 6", Theme.TextPrimary);
            row("Guaranteed Sponsor Total", LobbyKit.PerRace(f.guaranteed), Theme.Green);
            row("Driver Costs", LobbyKit.PerRace(f.driverCosts), Theme.Red);
            row("Staff Costs", LobbyKit.PerRace(f.staffCosts), Theme.Red);
            row("Race Operations", f.raceOps < 0 ? Theme.NotConfigured : LobbyKit.PerRace(f.raceOps), f.raceOps < 0 ? Theme.TextSecondary : Theme.Red);
            row("Total Costs", f.TotalCosts < 0 ? Theme.NotConfigured : LobbyKit.PerRace(f.TotalCosts), f.TotalCosts < 0 ? Theme.TextSecondary : Theme.Red);
            row("Projected Profit", f.ProfitKnown ? LobbyKit.PerRace(f.Profit) : Theme.NotConfigured, !f.ProfitKnown ? Theme.TextSecondary : f.Profit >= 0 ? Theme.Green : Theme.Red);
            if (!f.salariesKnown) Ui.Label(p, "Some salaries are not configured, so they are left out of the costs above.", Theme.Small, false, Theme.Yellow, 20, y, rw, 24);
            string bonus = f.bonuses > 0 ? "Possible sponsor bonuses (not guaranteed, not included above): " + Theme.Money(f.bonuses) : "No sponsor bonuses available.";
            Ui.Label(p, bonus, Theme.Small, false, Theme.TextSecondary, 20, y + 24, rw, 24);

            // Financial Health strip: 48px tall, 20px above the panel bottom
            float sy = 580 - 20 - 48;
            Ui.Box(p, Theme.Dark, 20, sy, rw, 48);
            Ui.Label(p, "FINANCIAL HEALTH", Theme.Small, true, Theme.TextSecondary, 36, sy, 220, 48);
            string health; Color hc;
            if (!f.ProfitKnown) { health = Theme.NotConfigured.ToUpperInvariant(); hc = Theme.TextSecondary; }
            else if (f.Profit > 0) { health = "PROFITABLE"; hc = Theme.Green; }
            else if (f.Profit == 0) { health = "BREAK EVEN"; hc = Theme.TextPrimary; }
            else { health = "LOSING MONEY"; hc = Theme.Red; }
            Ui.Label(p, health, Theme.Body, true, hc, 260, sy, 300, 48);
            if (f.TotalCosts >= 0 && c.balance < f.TotalCosts)
                Ui.Label(p, "NOT ENOUGH CASH FOR NEXT EVENT", Theme.Small, true, Theme.Yellow, 20, sy, rw - 16, 48, TextAnchor.MiddleRight);
        }

        static string RankText(CareerSave c, System.Func<Rules.TeamView, float> metric)
        {
            int n; int r = Rules.Rank(c, metric, out n);
            return r < 0 ? Theme.NotConfigured : r + " / " + n;
        }

        static void Reports(Transform p, CareerSave c, float w)
        {
            float y = Ui.UnderTitle, rw = w - 40;
            int i = 0;
            System.Action<string, string> row = (l, v) => { LobbyKit.ValueRow(p, 20, y, rw, l, v, v == Theme.NotConfigured ? Theme.TextSecondary : Theme.TextPrimary, i++); y += 52; };
            var me = Rules.PlayerView(c);
            row("Active Sponsors", c.sponsors.Count(s => s.active) + " / 6");
            row("Car Rank", RankText(c, t => Rules.Overall(t.car)));
            row("Driver Rank", RankText(c, t => Rules.AvgOveralls(t.driverStats)));
            row("Engineer Rank", RankText(c, t => Rules.AvgOveralls(t.engineerStats)));
            row("Car Performance", Rules.Fmt(Rules.Overall(c.carParts)));
            row("Engineer Average", Rules.Fmt(Rules.AvgOveralls(me.engineerStats)));
            row("Coordinator Overall", c.coordinator.filled ? Rules.Fmt(Rules.Overall(c.coordinator.def.stats)) : "Not Filled");
            row("Pit Crew", Rules.PitCrew(c) + "  (" + StaffTab.PitCrewRank(c) + ")");
        }
    }

    // =====================================================================
    public static class DriversTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            for (int i = 0; i < 2; i++)
                DriverPanel(Ui.Panel(root, i * (926 + 20), 0, 926, 880, "Driver " + (i + 1)), c, i, 926, 880);
        }

        static void DriverPanel(Transform p, CareerSave c, int idx, float w, float h)
        {
            var ds = c.drivers[idx];
            if (!ds.filled) { LobbyKit.Vacant(p, w, h, "DRIVER " + (idx + 1), PersonKind.Driver); return; }
            var d = ds.def;
            float top = Ui.UnderTitle;
            Ui.Icon(p, PixelArt.Portrait(Theme.Hex(c.teamColor), idx + 1), 20, top, 96, 96);
            float ix = 136, iw = w - ix - 20;
            Ui.Ellipsis(p, People.NameLine(d), Theme.PanelTitle, true, Theme.TextPrimary, ix, top - 2, iw, 30);
            Ui.Label(p, "Age " + People.Age(d.age) + "    •    Contract " + People.Contract(d.contractSeasons), Theme.Small, false, Theme.TextSecondary, ix, top + 28, iw, 22);
            Ui.Label(p, "Salary " + (d.salaryPerRace < 0 ? Theme.NotConfigured : LobbyKit.PerRace(d.salaryPerRace)), Theme.Small, false, Theme.TextSecondary, ix, top + 50, iw, 22);
            Ui.Label(p, "CURRENT OVERALL  " + Rules.Fmt(Rules.Overall(d.stats)) + "      POTENTIAL OVERALL  " + Rules.Fmt(Rules.Overall(d.potential)), Theme.Body, true, Theme.Green, ix, top + 72, iw, 24);

            float y = top + 96 + 24;
            for (int s = 0; s < 5; s++)
            {
                int cur = Rules.StatAt(d.stats, s), pot = Rules.StatAt(d.potential, s);
                Ui.Label(p, Stat.Driver[s], Theme.Body, false, Theme.TextPrimary, 20, y, 160, 64);
                Ui.Bar(p, 200, y + 24, w - 200 - 20 - 100 - 20, 16, Mathf.Max(0, cur), Theme.Green, pot);
                Ui.Label(p, Rules.FmtInt(cur) + " / " + Rules.FmtInt(pot), Theme.Body, true, Theme.TextPrimary, w - 20 - 100, y, 100, 64, TextAnchor.MiddleRight);
                y += 64;
            }

            // ---- Train Driver ----
            y += 12;
            Ui.Label(p, "TRAIN DRIVER", Theme.Small, true, Theme.TextSecondary, 20, y, 300, 24);
            y += 32;
            var rule = ConfigLoader.Config.driverTraining;
            bool configured = rule.cost >= 0 && rule.durationEvents >= 0 && rule.gain > 0;
            float bw = (w - 40 - 4 * 8) / 5f;
            for (int s = 0; s < 5; s++)
            {
                int stat = s;
                int cur = Rules.StatAt(d.stats, s), pot = Rules.StatAt(d.potential, s);
                bool maxed = cur >= 0 && pot >= 0 && cur >= pot;
                var b = Ui.Button(p, maxed ? "MAX" : Stat.DriverShort[s], BtnStyle.Red, 20 + s * (bw + 8), y, bw, 48, () => Train(c, idx, stat));
                b.SetEnabled(configured && !maxed && cur >= 0 && pot >= 0 && !ds.training.active);
            }
            y += 56;
            string status;
            Color sc = Theme.TextSecondary;
            if (ds.training.active)
            {
                status = "Training " + Stat.Driver[ds.training.stat] + ": " + ds.training.remainingEvents + " of " + ds.training.totalEvents + " races left";
                sc = Theme.Yellow;
            }
            else status = configured ? "No training in progress" : "Training costs, durations and gains " + Theme.NotConfigured.ToLowerInvariant();
            Ui.Label(p, status, Theme.Small, true, sc, 20, y, w - 40, 24);
            y += 40;

            // ---- Recent ----
            Ui.Label(p, "RECENT", Theme.Small, true, Theme.TextSecondary, 20, y, 300, 24);
            y += 32;
            var sa = Ui.Scroll(p, 20, y, w - 40, h - 20 - y);
            if (ds.recent.Count == 0)
            {
                Ui.Label(sa.content, "No recent changes", Theme.Body, false, Theme.TextSecondary, 12, 0, sa.width - 24, 40);
                sa.SetHeight(40);
            }
            else
            {
                float ry = 0; int n = 0;
                foreach (var e in Enumerable.Reverse(ds.recent)) // newest first
                {
                    Ui.Box(sa.content, n++ % 2 == 0 ? Theme.RowA : Theme.RowB, 0, ry, sa.width, 40);
                    Ui.Label(sa.content, e.when, Theme.Small, false, Theme.TextSecondary, 12, ry, 220, 40);
                    Ui.Label(sa.content, e.stat, Theme.Small, true, Theme.TextPrimary, 240, ry, 220, 40);
                    Ui.Label(sa.content, (e.change > 0 ? "+" : "") + e.change + (string.IsNullOrEmpty(e.note) ? "" : "  " + e.note), Theme.Small, true, e.change >= 0 ? Theme.Green : Theme.Red, 470, ry, sa.width - 482, 40);
                    ry += 40;
                }
                sa.SetHeight(ry);
            }
        }

        static void Train(CareerSave c, int idx, int stat)
        {
            var rule = ConfigLoader.Config.driverTraining;
            var d = c.drivers[idx].def;
            int cur = Rules.StatAt(d.stats, stat), pot = Rules.StatAt(d.potential, stat);
            int after = Mathf.Min(pot, cur + rule.gain);
            string dur = rule.durationEvents == 0 ? "Immediate" : rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races");
            LobbyKit.Confirm("TRAIN " + Stat.Driver[stat].ToUpperInvariant(), c, rule.cost,
                d.name + ": " + Stat.Driver[stat] + " " + cur + " → " + after, dur, cs =>
                {
                    var ds = cs.drivers[idx];
                    cs.balance -= rule.cost;
                    if (rule.durationEvents == 0)
                    {
                        int before = ds.def.stats[stat];
                        ds.def.stats[stat] = Mathf.Min(ds.def.potential[stat], before + rule.gain);
                        ds.recent.Add(new ChangeEntry { when = "Season " + cs.season, stat = Stat.Driver[stat], change = ds.def.stats[stat] - before, note = "training complete" });
                    }
                    else
                    {
                        ds.training = new Job { active = true, stat = stat, remainingEvents = rule.durationEvents, totalEvents = rule.durationEvents, cost = rule.cost };
                    }
                });
        }
    }

    // =====================================================================
    public static class CarTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            CurrentCar(Ui.Panel(root, 0, 0, 1228, 880, "Current Car"), c, 1228, 880);
            Fund(Ui.Panel(root, 1248, 0, 624, 430, "Next Year Car Fund"), c, 624);
            Ranking(Ui.Panel(root, 1248, 450, 624, 430, "League Car Ranking"), c, 624, 430);
        }

        public static float LeagueAverage(CareerSave c, int part)
        {
            var vals = Rules.Championship(c).Select(t => Rules.StatAt(t.car, part)).ToList();
            if (vals.Count == 0 || vals.Any(v => v < 0)) return float.NaN;
            return (float)vals.Average();
        }

        static void CurrentCar(Transform p, CareerSave c, float w, float h)
        {
            var rule = ConfigLoader.Config.carUpgrade;
            bool configured = rule.cost >= 0 && rule.gain > 0;
            float y = Ui.UnderTitle;
            Ui.Label(p, "CAR OVERALL  " + Rules.Fmt(Rules.Overall(c.carParts)), Theme.Body, true, Theme.TextPrimary, 20, y - 48, w - 40, 30, TextAnchor.MiddleRight);
            for (int i = 0; i < 6; i++)
            {
                int part = i;
                int v = Rules.StatAt(c.carParts, i);
                float avg = LeagueAverage(c, i);
                Ui.Box(p, i % 2 == 0 ? Theme.RowA : Theme.RowB, 20, y, w - 40, 100);
                Ui.Label(p, Stat.Part[i], Theme.PanelTitle, true, Theme.TextPrimary, 36, y + 10, 260, 36);
                Ui.Label(p, (v < 0 ? "--" : v.ToString()) + " / 100", Theme.BigNumber, true, Theme.TextPrimary, 300, y + 8, 200, 40);
                Ui.Label(p, "LEAGUE AVG  " + Rules.Fmt(avg), Theme.Small, true, Theme.TextSecondary, 520, y + 10, 300, 36);
                float barW = w - 40 - 32 - 280;
                Ui.Bar(p, 36, y + 62, barW, 16, Mathf.Max(0, v), Theme.Red);
                if (!float.IsNaN(avg)) Ui.Box(p, Color.white, 36 + barW * Mathf.Clamp01(avg / 100f) - 1, y + 56, 3, 28, "LeagueAvg");
                bool maxed = v >= 100;
                bool busy = c.carUpgrade.active;
                bool thisOne = busy && c.carUpgrade.stat == i;
                string label = thisOne ? "IN PROGRESS" : maxed ? "MAX" : configured ? "UPGRADE " + Theme.Money(rule.cost) : Theme.NotConfigured;
                var b = Ui.Button(p, label, thisOne ? BtnStyle.Yellow : BtnStyle.Red, w - 20 - 16 - 248, y + 26, 248, 48, () => Upgrade(c, part), 16);
                b.SetEnabled(configured && !maxed && v >= 0 && !busy);
                if (thisOne) Ui.Label(p, "+" + rule.gain + " in " + c.carUpgrade.remainingEvents + (c.carUpgrade.remainingEvents == 1 ? " race" : " races"), Theme.Small, true, Theme.Yellow, w - 20 - 16 - 248, y + 76, 248, 20, TextAnchor.MiddleCenter);
                y += 108;
            }
            Ui.Label(p, "AVAILABLE BALANCE", Theme.Small, true, Theme.TextSecondary, 20, h - 20 - 40, 400, 40);
            Ui.Label(p, Theme.Money(c.balance), Theme.BigNumber, true, Theme.Green, 20, h - 20 - 40, w - 40, 40, TextAnchor.MiddleRight);
            if (!configured) Ui.Label(p, "Upgrade costs and gains " + Theme.NotConfigured.ToLowerInvariant(), Theme.Small, false, Theme.Yellow, 20, h - 20 - 70, w - 40, 26);
            else if (c.carUpgrade.active) Ui.Label(p, "Upgrading " + Stat.Part[c.carUpgrade.stat] + ": " + c.carUpgrade.remainingEvents + " of " + c.carUpgrade.totalEvents + (c.carUpgrade.totalEvents == 1 ? " race" : " races") + " left. One car upgrade at a time.", Theme.Small, true, Theme.Yellow, 20, h - 20 - 70, w - 40, 26);
            else Ui.Label(p, "One car upgrade at a time. Each takes " + rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races") + " to finish.", Theme.Small, false, Theme.TextSecondary, 20, h - 20 - 70, w - 40, 26);
        }

        static void Upgrade(CareerSave c, int part)
        {
            var rule = ConfigLoader.Config.carUpgrade;
            int cur = c.carParts[part];
            int after = Mathf.Min(100, cur + rule.gain);
            string dur = rule.durationEvents <= 0 ? "Immediate" : rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races");
            LobbyKit.Confirm("UPGRADE " + Stat.Part[part].ToUpperInvariant(), c, rule.cost, Stat.Part[part] + " " + cur + " → " + after, dur,
                cs =>
                {
                    cs.balance -= rule.cost;
                    if (rule.durationEvents <= 0) cs.carParts[part] = Mathf.Min(100, cs.carParts[part] + rule.gain);
                    else cs.carUpgrade = new Job { active = true, stat = part, remainingEvents = rule.durationEvents, totalEvents = rule.durationEvents, cost = rule.cost };
                },
                "CONFIRM", new[] { "Upgrades your current car. Separate from next year's car fund." });
        }

        static void Fund(Transform p, CareerSave c, float w)
        {
            bool rules = SeasonRollover.CarRulesSet;
            var R = ConfigLoader.Config.seasonRules;
            float y = Ui.UnderTitle - 6;
            Ui.Label(p, "IN FUND", Theme.Small, true, Theme.TextSecondary, 20, y, 200, 30);
            Ui.Label(p, Theme.Money(c.nextYearFund), Theme.BigNumber, true, Theme.Green, 20, y, w - 40, 30, TextAnchor.MiddleRight);
            y += 38;
            long[] amounts = { 100_000, 500_000, 1_000_000 };
            string[] adds = { "+$100,000", "+$500,000", "+$1,000,000" };
            float bw = (w - 40 - 16) / 3f;
            for (int i = 0; i < 3; i++)
            {
                long amt = amounts[i];
                var b = Ui.Button(p, adds[i], BtnStyle.Red, 20 + i * (bw + 8), y, bw, 40, () => AddFund(c, amt), 16);
                b.SetEnabled(rules && c.balance >= amt);
            }
            if (rules)
                Ui.Label(p, "Every part loses " + R.winterLoss + " for next season's rules. Tap a part to change its priority.", 12, false, Theme.TextSecondary, 20, y + 42, w - 40, 22, TextAnchor.MiddleCenter);
            else
                Ui.Label(p, "Development rules not set", Theme.Small, true, Theme.Yellow, 20, y + 42, w - 40, 22, TextAnchor.MiddleCenter);
            y += 70;
            Ui.Box(p, Theme.TableHeader, 20, y, w - 40, 26);
            float c1 = 32, c2 = 200, c3 = 290, c4 = 380, c5 = w - 36;
            Ui.Label(p, "PART", 14, true, Theme.TableHeaderText, c1, y, 160, 26);
            Ui.Label(p, "ENG AVG", 14, true, Theme.TableHeaderText, c2, y, 90, 26);
            Ui.Label(p, "PRIORITY", 14, true, Theme.TableHeaderText, c3, y, 90, 26);
            Ui.Label(p, "AMOUNT", 14, true, Theme.TableHeaderText, c4, y, 110, 26);
            Ui.Label(p, "NEXT YEAR", 14, true, Theme.TableHeaderText, c5 - 120, y, 120, 26, TextAnchor.MiddleRight);
            y += 26;
            var alloc = SeasonRollover.Allocation(c);
            var gains = SeasonRollover.FundGains(c);
            string[] pr = { "OFF", "LOW", "NORMAL", "HIGH" };
            for (int i = 0; i < 6; i++)
            {
                int part = i;
                var engVals = c.engineers.Where(e => e.filled).Select(e => Rules.StatAt(e.def.stats, i)).ToList();
                string eng = engVals.Count == 0 || engVals.Any(v => v < 0) ? "--" : Theme.OneDecimal((float)engVals.Average());
                int wgt = c.fundWeights != null && c.fundWeights.Length == 6 ? c.fundWeights[i] : 2;
                var row = Ui.Button(p, "", BtnStyle.Dark, 20, y, w - 40, 30, () => CycleWeight(c, part), 12);
                row.customFill = i % 2 == 0 ? Theme.RowA : Theme.RowB; row.Apply();
                row.SetEnabled(rules);
                Ui.Label(p, Stat.Part[i], Theme.Small, false, Theme.TextPrimary, c1, y, 160, 30);
                Ui.Label(p, eng, Theme.Small, true, Theme.Green, c2, y, 90, 30);
                Ui.Label(p, rules ? pr[Mathf.Clamp(wgt, 0, 3)] : "--", Theme.Small, true, wgt == 0 ? Theme.TextSecondary : wgt == 3 ? Theme.Yellow : Theme.TextPrimary, c3, y, 90, 30);
                Ui.Label(p, rules ? Theme.Money((long)(c.nextYearFund * alloc[i])) : "--", Theme.Small, false, Theme.TextPrimary, c4, y, 120, 30);
                int now = Rules.StatAt(c.carParts, i);
                string nxt = rules && now >= 0 ? Mathf.Clamp(now - R.winterLoss + gains[i], 1, 100) + (gains[i] > 0 ? "  (+" + gains[i] + ")" : "") : "--";
                Ui.Label(p, nxt, Theme.Small, true, gains[i] >= (rules ? R.winterLoss : 0) && rules ? Theme.Green : Theme.TextPrimary, c5 - 140, y, 140, 30, TextAnchor.MiddleRight);
                y += 30;
            }
        }

        static void CycleWeight(CareerSave c, int part)
        {
            GameApp.I.Commit(cs =>
            {
                if (cs.fundWeights == null || cs.fundWeights.Length != 6) cs.fundWeights = new[] { 2, 2, 2, 2, 2, 2 };
                cs.fundWeights[part] = (cs.fundWeights[part] + 1) % 4;
                if (cs.fundWeights.All(x => x == 0)) cs.fundWeights[part] = 1;
            }, LobbyScreen.Refresh);
        }

        static void AddFund(CareerSave c, long amount)
        {
            var R = ConfigLoader.Config.seasonRules;
            LobbyKit.Confirm("NEXT YEAR CAR FUND", c, amount, "Fund " + Theme.Money(c.nextYearFund) + " → " + Theme.Money(c.nextYearFund + amount), "Spent at the end of the season",
                cs => { cs.balance -= amount; cs.nextYearFund += amount; },
                "CONFIRM", new[] { "Moved from cash into next season's car. It can't be taken back out.", "About +1 point per " + Theme.Money(R.fundPerPoint) + " on a part with 80-rated engineers." });
        }

        static void Ranking(Transform p, CareerSave c, float w, float h)
        {
            var teams = Rules.Championship(c);
            bool known = teams.All(t => !float.IsNaN(Rules.Overall(t.car)));
            if (known) teams = teams.OrderByDescending(t => Rules.Overall(t.car)).ToList();
            float y = Ui.UnderTitle;
            if (!known) { Ui.Label(p, "Car overall " + Theme.NotConfigured.ToLowerInvariant() + " - not ranked", Theme.Small, false, Theme.Yellow, 20, y - 6, w - 40, 22); y += 20; }
            var sa = Ui.Scroll(p, 20, y, w - 40, h - 20 - y);
            float ry = 0; int n = 0;
            foreach (var t in teams)
            {
                Color bg = t.isPlayer ? Theme.Darken(Theme.Red, 0.55f) : (n % 2 == 0 ? Theme.RowA : Theme.RowB);
                Ui.Box(sa.content, bg, 0, ry, sa.width, 40);
                Ui.Label(sa.content, known ? (n + 1).ToString() : "--", Theme.Body, true, Theme.TextPrimary, 10, ry, 40, 40);
                Ui.Box(sa.content, t.color, 56, ry + 8, 6, 24);
                Ui.Ellipsis(sa.content, t.name, Theme.Body, t.isPlayer, Theme.TextPrimary, 72, ry, sa.width - 72 - 90, 40);
                Ui.Label(sa.content, Rules.Fmt(Rules.Overall(t.car)) == Theme.NotConfigured ? "--" : Rules.Fmt(Rules.Overall(t.car)), Theme.Body, true, Theme.TextPrimary, sa.width - 90, ry, 80, 40, TextAnchor.MiddleRight);
                ry += 40; n++;
            }
            if (teams.Count == 0) Ui.Label(sa.content, "Teams " + Theme.NotConfigured.ToLowerInvariant(), Theme.Body, false, Theme.TextSecondary, 10, 0, sa.width, 40);
            sa.SetHeight(Mathf.Max(ry, 40));
        }
    }

    // =====================================================================
    public static class StaffTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            float ew = (1872 - 40) / 3f;
            for (int i = 0; i < 3; i++)
                Engineer(Ui.Panel(root, i * (ew + 20), 0, ew, 430, "Engineer " + (i + 1)), c, i, ew, 430);
            Coordinator(Ui.Panel(root, 0, 450, 926, 430, "Coordinator"), c, 926, 430);
            PitCrew(Ui.Panel(root, 926 + 20, 450, 926, 430, "Pit Crew"), c, 926, 430);
        }

        public static string PitCrewRank(CareerSave c)
        {
            var teams = Rules.Championship(c);
            int mine = Rules.PitCrew(c);
            if (teams.Count == 0) return "--";
            return (1 + teams.Count(t => !t.isPlayer && t.pitCrew > mine)) + " / " + teams.Count;
        }

        static void PitCrew(Transform p, CareerSave c, float w, float h)
        {
            int crew = Rules.PitCrew(c);
            Ui.Label(p, "RATING " + crew, Theme.BigNumber, true, Theme.Green, w - 20 - 240, Ui.UnderTitle - 56, 240, 40, TextAnchor.MiddleRight);
            Ui.Label(p, "Changes tyres and sends the car back out. A better crew means shorter, more reliable stops.", Theme.Small, false, Theme.TextSecondary, 20, Ui.UnderTitle, w - 40, 24);
            Ui.Bar(p, 20, Ui.UnderTitle + 34, w - 40, 12, crew, Theme.Green);
            float y = Ui.UnderTitle + 58;
            int i = 0;
            System.Action<string, string, Color> row = (l, v, col) => { LobbyKit.ValueRow(p, 20, y, w - 40, l, v, col, i++); y += 52; };
            row("Typical stationary time", Rules.PitServiceTime(crew).ToString("0.0") + "s", Theme.TextPrimary);
            row("Chance of a slow stop", Mathf.RoundToInt(Rules.SlowStopChance(crew) * 100) + "%  (costs 2.5-9s)", Theme.Yellow);
            row("League rank", PitCrewRank(c), Theme.TextPrimary);

            var rule = ConfigLoader.Config.pitCrewUpgrade;
            bool configured = rule.cost >= 0 && rule.gain > 0;
            bool busy = c.pitCrewJob.active;
            y += 4;
            string label = crew >= 100 ? "MAX" : busy ? "TRAINING IN PROGRESS" : configured ? "TRAIN PIT CREW  +" + rule.gain + "  •  " + Theme.Money(rule.cost) : Theme.NotConfigured;
            var b = Ui.Button(p, label, busy ? BtnStyle.Yellow : BtnStyle.Red, 20, y, w - 40, 48, () => Upgrade(c), 16);
            b.SetEnabled(configured && !busy && crew < 100);
            string status = busy ? "Pit stop practice: +" + rule.gain + " in " + c.pitCrewJob.remainingEvents + (c.pitCrewJob.remainingEvents == 1 ? " race" : " races")
                          : configured ? "Each session takes " + rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races") + ". One at a time." : "";
            Ui.Label(p, status, Theme.Small, true, busy ? Theme.Yellow : Theme.TextSecondary, 20, y + 52, w - 40, 24, TextAnchor.MiddleCenter);
        }

        static void Upgrade(CareerSave c)
        {
            var rule = ConfigLoader.Config.pitCrewUpgrade;
            int cur = Rules.PitCrew(c), after = Mathf.Min(100, cur + rule.gain);
            string dur = rule.durationEvents <= 0 ? "Immediate" : rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races");
            LobbyKit.Confirm("TRAIN PIT CREW", c, rule.cost, "Pit crew " + cur + " → " + after + "  (" + Rules.PitServiceTime(cur).ToString("0.0") + "s → " + Rules.PitServiceTime(after).ToString("0.0") + "s)", dur, cs =>
            {
                cs.balance -= rule.cost;
                if (cs.pitCrew < 0) cs.pitCrew = Rules.PitCrew(cs);
                if (rule.durationEvents <= 0) cs.pitCrew = Mathf.Min(100, cs.pitCrew + rule.gain);
                else cs.pitCrewJob = new Job { active = true, stat = 0, remainingEvents = rule.durationEvents, totalEvents = rule.durationEvents, cost = rule.cost };
            });
        }

        static void Header(Transform p, PersonDef d, float w, int[] stats)
        {
            float y = Ui.UnderTitle;
            Ui.Ellipsis(p, People.NameLine(d), Theme.Body, true, Theme.TextPrimary, 20, y, w - 260, 28);
            Ui.Label(p, "OVR " + Rules.Fmt(Rules.Overall(stats)), Theme.BigNumber, true, Theme.Green, w - 20 - 240, y - 56, 240, 40, TextAnchor.MiddleRight);
            Ui.Label(p, "Contract " + People.Contract(d.contractSeasons) + "    •    Salary " + (d.salaryPerRace < 0 ? Theme.NotConfigured : LobbyKit.PerRace(d.salaryPerRace)), Theme.Small, false, Theme.TextSecondary, 20, y + 30, w - 40, 22);
        }

        static void Engineer(Transform p, CareerSave c, int idx, float w, float h)
        {
            var es = c.engineers[idx];
            if (!es.filled) { LobbyKit.Vacant(p, w, h, "ENGINEER " + (idx + 1), PersonKind.Engineer); return; }
            Header(p, es.def, w, es.def.stats);
            float colW = (w - 40 - 40) / 2f;
            for (int i = 0; i < 6; i++)
            {
                float x = 20 + (i / 3) * (colW + 40), y = Ui.UnderTitle + 70 + (i % 3) * 96;
                int v = Rules.StatAt(es.def.stats, i);
                Ui.Label(p, Stat.Part[i], Theme.Body, false, Theme.TextPrimary, x, y, colW - 60, 28);
                Ui.Label(p, Rules.FmtInt(v), Theme.Body, true, Theme.TextPrimary, x + colW - 60, y, 60, 28, TextAnchor.MiddleRight);
                Ui.Bar(p, x, y + 34, colW, 12, Mathf.Max(0, v), Theme.Green);
            }
        }

        static void Coordinator(Transform p, CareerSave c, float w, float h)
        {
            var cs = c.coordinator;
            if (!cs.filled) { LobbyKit.Vacant(p, w, h, "COORDINATOR", PersonKind.Coordinator); return; }
            Header(p, cs.def, w, cs.def.stats);
            float y = Ui.UnderTitle + 64;
            for (int i = 0; i < 3; i++)
            {
                int v = Rules.StatAt(cs.def.stats, i);
                Ui.Label(p, Stat.Coord[i], Theme.Body, false, Theme.TextPrimary, 20, y, 300, 26);
                Ui.Label(p, Rules.FmtInt(v), Theme.Body, true, Theme.TextPrimary, w - 20 - 80, y, 80, 26, TextAnchor.MiddleRight);
                Ui.Bar(p, 20, y + 28, w - 40, 12, Mathf.Max(0, v), Theme.Green);
                y += 50;
            }
            var rule = ConfigLoader.Config.coordinatorStudy;
            bool configured = rule.cost >= 0 && rule.durationEvents >= 0 && rule.gain > 0;
            y += 2;
            Ui.Label(p, "STUDY", Theme.Small, true, Theme.TextSecondary, 20, y, 200, 22);
            y += 24;
            float bw = (w - 40 - 16) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int stat = i;
                bool maxed = Rules.StatAt(cs.def.stats, i) >= 100;
                var b = Ui.Button(p, maxed ? "MAX" : Stat.Coord[i].ToUpperInvariant(), BtnStyle.Red, 20 + i * (bw + 8), y, bw, 40, () => Study(c, stat), 16);
                b.SetEnabled(configured && !maxed && !cs.study.active && Rules.StatAt(cs.def.stats, i) >= 0);
            }
            y += 46;
            if (cs.study.active)
            {
                float prog = cs.study.totalEvents > 0 ? 1f - (float)cs.study.remainingEvents / cs.study.totalEvents : 0f;
                Ui.Label(p, "Studying " + Stat.Coord[cs.study.stat] + ": " + cs.study.remainingEvents + " of " + cs.study.totalEvents + " races left", Theme.Small, true, Theme.Yellow, 20, y, w * 0.6f, 24);
                Ui.Bar(p, w * 0.62f, y + 6, w * 0.38f - 20, 12, prog * 100f, Theme.Yellow);
            }
            else Ui.Label(p, configured ? "Not studying" : "Study costs, durations and gains " + Theme.NotConfigured.ToLowerInvariant(), Theme.Small, true, Theme.TextSecondary, 20, y, w - 40, 24);
        }

        static void Study(CareerSave c, int stat)
        {
            var rule = ConfigLoader.Config.coordinatorStudy;
            int cur = c.coordinator.def.stats[stat];
            int after = Mathf.Min(100, cur + rule.gain);
            string dur = rule.durationEvents == 0 ? "Immediate" : rule.durationEvents + (rule.durationEvents == 1 ? " race" : " races");
            LobbyKit.Confirm("STUDY " + Stat.Coord[stat].ToUpperInvariant(), c, rule.cost, Stat.Coord[stat] + " " + cur + " → " + after, dur, cs =>
            {
                cs.balance -= rule.cost;
                if (rule.durationEvents == 0) cs.coordinator.def.stats[stat] = Mathf.Min(100, cs.coordinator.def.stats[stat] + rule.gain);
                else cs.coordinator.study = new Job { active = true, stat = stat, remainingEvents = rule.durationEvents, totalEvents = rule.durationEvents, cost = rule.cost };
            });
        }
    }
}
