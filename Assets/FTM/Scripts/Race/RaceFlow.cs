using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>Starting, resuming, saving and committing a race weekend.</summary>
    public static class RaceFlow
    {
        static GameConfig Cfg { get { return ConfigLoader.Config; } }

        public static bool HasActive(CareerSave c) { return c != null && !string.IsNullOrEmpty(c.activeRace); }

        public static RaceState LoadActive(CareerSave c)
        {
            if (!HasActive(c)) return null;
            try { return JsonUtility.FromJson<RaceState>(c.activeRace); }
            catch (Exception e) { Debug.LogError("FTM: could not read the saved race: " + e.Message); return null; }
        }

        /// <summary>Dashboard "Start Race Weekend": confirm the per-race costs, charge them once, build the grid.</summary>
        public static void StartOrResume(CareerSave c)
        {
            var existing = LoadActive(c);
            if (existing != null) { Open(existing); return; }

            string reason = RaceSim.CannotStartReason(c);
            if (reason != null) { GameApp.I.Message("START RACE WEEKEND", reason); return; }
            var f = Rules.Finances(c);
            long costs = Math.Max(0, f.TotalCosts);
            var e = Rules.NextEvent(c);
            var w = GameApp.I.OpenWindow(e.grandPrix.ToUpperInvariant(), 600, 5 * 44 + 40, "START WEEKEND", win =>
            {
                GameApp.I.Commit(cs =>
                {
                    var state = RaceSim.Create(cs);
                    state.costsCharged = costs;
                    cs.balance -= costs;
                    cs.activeRace = JsonUtility.ToJson(state);
                }, () =>
                {
                    win.Close();
                    Open(LoadActive(GameApp.I.Career));
                });
            });
            float y = 0, bw = w.bodyWidth;
            LobbyKit.Line(w.body, ref y, bw, "Round", c.nextRound + " of " + Rules.Rounds, Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Race costs (salaries + operations)", Theme.Money(costs), Theme.Red);
            LobbyKit.Line(w.body, ref y, bw, "Current balance", Theme.Money(c.balance), Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Balance afterward", Theme.Money(c.balance - costs), c.balance - costs < 0 ? Theme.Red : Theme.Green);
            Ui.Label(w.body, "Costs are charged once, now. A negative balance blocks upgrades, training and signings until you are back above $0.", Theme.Small, false, Theme.TextSecondary, 0, y, bw, 50, TextAnchor.UpperLeft);
        }

        public static void Open(RaceState s)
        {
            if (s == null) return;
            if (s.session < Session.Race) { GameApp.I.Show(s.sessionEnded ? (IScreen)new SessionSummaryScreen(s) : new RaceScreen(s)); return; }
            if (s.phase == 0) GameApp.I.Show(new GridScreen(s));
            else GameApp.I.Show(new RaceScreen(s));
        }

        /// <summary>Writes the live race into the career and saves (Save and Exit, app pause).</summary>
        public static void Persist(CareerSave c, RaceState s, Action onSaved)
        {
            if (c == null || s == null || s.committed) { if (onSaved != null) onSaved(); return; }
            string json = JsonUtility.ToJson(s);
            GameApp.I.Commit(cs => cs.activeRace = json, onSaved);
        }

        // ------------------------------------------------------------------ results

        /// <summary>
        /// Applies the finished race exactly once: results, points, prize money, sponsor pay and bonuses,
        /// sponsor contracts, training/study/upgrade timers and the calendar.
        /// </summary>
        public static void Commit(RaceState s, Action onDone)
        {
            var c = GameApp.I.Career;
            if (c == null || s.committed) { if (onDone != null) onDone(); return; }
            var lines = RaceSim.Classification(s);
            GameApp.I.Commit(cs =>
            {
                var result = new RaceResult { round = s.round, grandPrix = s.grandPrix };
                var mine = new CarState[2];
                var minePos = new int[2];
                foreach (var l in lines)
                {
                    var car = s.cars[l.car];
                    result.classification.Add(new Classified
                    {
                        driverId = car.driverId, driverName = car.name, teamId = car.teamId, teamName = car.teamName,
                        position = l.classified ? l.position : 0, points = l.points, pole = car.grid == 1
                    });
                    if (l.position == 1) result.winner = car.name;
                    if (car.mine >= 0) { mine[car.mine] = car; minePos[car.mine] = l.classified ? l.position : 0; result.teamPoints += l.points; }
                }
                result.driver1Pos = minePos[0];
                result.driver2Pos = minePos[1];
                var rec = Rules.Season(cs, s.season);
                if (rec == null) { rec = new SeasonRecord { season = s.season }; cs.seasons.Add(rec); }
                rec.results.RemoveAll(r => r.round == s.round);
                rec.results.Add(result);

                var sum = new EventSummary { round = s.round, grandPrix = s.grandPrix, costs = s.costsCharged };
                for (int k = 0; k < 2; k++)
                    if (minePos[k] >= 1 && minePos[k] <= Cfg.prizeMoney.Count) sum.prize += Cfg.prizeMoney[minePos[k] - 1];

                foreach (var sp in cs.sponsors)
                {
                    if (!sp.active) continue;
                    sum.sponsorPay += sp.guaranteedPerRace;
                    if (sp.bonusAmount > 0 && !string.IsNullOrEmpty(sp.bonusCondition))
                    {
                        bool? met = BonusMet(sp.bonusCondition, s, mine, minePos);
                        if (met == true) { sum.bonuses += sp.bonusAmount; sum.lines.Add(sp.name + ": " + sp.bonusCondition + "  +" + Theme.Money(sp.bonusAmount)); }
                        else if (met == false) sum.lines.Add(sp.name + ": " + sp.bonusCondition + "  - missed");
                        else sum.lines.Add(sp.name + ": " + sp.bonusCondition + "  - judged at season end");
                    }
                    sp.racesRemaining--;
                    if (sp.racesRemaining <= 0) { sum.lines.Add(sp.name + " deal has ended."); cs.sponsors[Array.IndexOf(cs.sponsors, sp)] = new SponsorState(); }
                }
                cs.balance += sum.prize + sum.sponsorPay + sum.bonuses;
                Rules.AdvanceTimers(cs, s.grandPrix);
                cs.nextRound = s.round + 1;
                cs.activeRace = "";
                cs.lastEvent = sum;
            }, () => { s.committed = true; if (onDone != null) onDone(); });
        }

        /// <summary>true = earned, false = missed, null = can't be judged from one race (season-long targets).</summary>
        static bool? BonusMet(string cond, RaceState s, CarState[] mine, int[] pos)
        {
            string c = cond.ToLowerInvariant();
            Func<Func<int, bool>, bool> any = f => f(0) || f(1);
            Func<Func<int, bool>, bool> both = f => f(0) && f(1);
            Func<int, bool> finished = k => mine[k] != null && mine[k].status == 4;
            Func<int, int> p = k => pos[k] > 0 ? pos[k] : 99;
            if (c.Contains("championship") || c.Contains("this season")) return null;
            if (c.Contains("wet")) return s.wasWet && any(k => p(k) <= 10);
            int n = FirstNumber(c);
            if (c.Contains("win")) return any(k => p(k) == 1);
            if (c.Contains("both cars on the podium")) return both(k => p(k) <= 3);
            if (c.Contains("podium")) return any(k => p(k) <= 3);
            if (c.Contains("front row")) return any(k => mine[k] != null && mine[k].grid <= 2);
            if (c.Contains("qualifying")) return any(k => mine[k] != null && mine[k].grid <= (n > 0 ? n : 10));
            if (c.Contains("fastest lap")) return s.fastestCar >= 0 && s.cars[s.fastestCar].mine >= 0;
            if (c.Contains("fastest pit stop"))
            {
                float best = s.cars.Where(x => x.bestService > 0).Select(x => x.bestService).DefaultIfEmpty(0).Min();
                return best > 0 && any(k => mine[k] != null && Mathf.Approximately(mine[k].bestService, best));
            }
            if (c.Contains("lead a lap")) return any(k => mine[k] != null && mine[k].ledLap);
            if (c.Contains("gain")) return any(k => mine[k] != null && mine[k].grid - p(k) >= (n > 0 ? n : 5));
            if (c.Contains("beat your teammate")) return p(1) < p(0);
            if (c.Contains("both cars") && c.Contains("points")) return both(k => p(k) <= 10);
            if (c.Contains("both cars in the top")) return both(k => p(k) <= n);
            if (c.Contains("both cars finish")) return both(finished);
            if (c.Contains("points")) return any(k => p(k) <= 10);
            if (c.Contains("top")) return any(k => p(k) <= n);
            if (c.Contains("finish the race")) return any(finished);
            return null;
        }

        static int FirstNumber(string s)
        {
            int v = 0; bool found = false;
            foreach (char ch in s)
            {
                if (char.IsDigit(ch)) { v = v * 10 + (ch - '0'); found = true; }
                else if (found) break;
            }
            return found ? v : 0;
        }
    }
}
