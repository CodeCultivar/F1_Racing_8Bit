using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// Every calculation the lobby needs. Nothing here invents numbers: if a value or formula
    /// has not been configured, the result is "unknown" (NaN / -1) and the UI shows "Not configured".
    /// </summary>
    public static class Rules
    {
        public static GameConfig Cfg { get { return ConfigLoader.Config; } }

        // ---------- Overalls ----------
        public static bool FormulaSet { get { return Cfg.overallFormula == "average"; } }

        public static float Overall(int[] stats)
        {
            if (!FormulaSet || stats == null || stats.Length == 0 || stats.Any(s => s < 0)) return float.NaN;
            return (float)stats.Average();
        }

        public static string Fmt(float v) { return float.IsNaN(v) ? Theme.NotConfigured : Theme.OneDecimal(v); }
        public static string FmtInt(int v) { return v < 0 ? "--" : v.ToString(); }
        public static string FmtMoney(long v) { return v < 0 ? Theme.NotConfigured : Theme.Money(v); }

        public static int StatAt(int[] a, int i) { return (a != null && i < a.Length) ? a[i] : -1; }

        // ---------- Lookups ----------
        /// <summary>The loaded career's world (people, rival cars), or null to use GameConfig as-is.</summary>
        public static WorldState W
        {
            get
            {
                var c = GameApp.I != null ? GameApp.I.Career : null;
                return c != null && c.world != null && c.world.active ? c.world : null;
            }
        }

        public static List<DriverDef> Drivers { get { var w = W; return w != null ? w.drivers : Cfg.drivers; } }
        public static List<EngineerDef> Engineers { get { var w = W; return w != null ? w.engineers : Cfg.engineers; } }
        public static List<CoordinatorDef> Coordinators { get { var w = W; return w != null ? w.coordinators : Cfg.coordinators; } }

        public static DriverDef FindDriver(string id) { return Drivers.FirstOrDefault(d => d.id == id) ?? Cfg.drivers.FirstOrDefault(d => d.id == id); }
        public static EngineerDef FindEngineer(string id) { return Engineers.FirstOrDefault(d => d.id == id) ?? Cfg.engineers.FirstOrDefault(d => d.id == id); }
        public static CoordinatorDef FindCoordinator(string id) { return Coordinators.FirstOrDefault(d => d.id == id) ?? Cfg.coordinators.FirstOrDefault(d => d.id == id); }
        public static WorldTeam FindWorldTeam(CareerSave c, string id) { return c != null && c.world != null && c.world.active ? c.world.teams.FirstOrDefault(t => t.id == id) : null; }

        /// <summary>
        /// Gives an older career its own world (a snapshot of GameConfig). People already on your team stay
        /// marked as taken; your base team's original roster and any replaced team's roster become free agents.
        /// </summary>
        public static void EnsureWorld(CareerSave c)
        {
            if (c == null) return;
            if (c.world == null) c.world = new WorldState();
            if (c.world.active) return;
            var w = c.world;
            string replaced = c.baseTeamId == "custom" && Cfg.customTeam.joinMode.StartsWith("replace:") ? Cfg.customTeam.joinMode.Substring(8) : null;
            Func<string, bool> gone = tid => string.IsNullOrEmpty(tid) || tid == c.baseTeamId || tid == replaced;
            w.drivers = Cfg.drivers.Select(x => Clone(x)).ToList();
            w.engineers = Cfg.engineers.Select(x => Clone(x)).ToList();
            w.coordinators = Cfg.coordinators.Select(x => Clone(x)).ToList();
            foreach (var p in w.drivers.Cast<PersonDef>().Concat(w.engineers).Concat(w.coordinators))
                if (gone(p.teamId) || c.takenPeople.Contains(p.id)) { p.teamId = ""; p.buyoutFee = -1; }
            w.teams = new List<WorldTeam>();
            foreach (var t in Cfg.teams)
            {
                if (t.id == c.baseTeamId || t.id == replaced) continue;
                var wt = new WorldTeam { id = t.id, carParts = (int[])t.carParts.Clone(), pitCrew = t.pitCrew >= 0 ? t.pitCrew : 60 };
                wt.drivers = t.drivers.Where(id => !c.takenPeople.Contains(id)).ToList();
                wt.engineers = t.engineers.Where(id => !c.takenPeople.Contains(id)).ToList();
                wt.coordinator = c.takenPeople.Contains(t.coordinator) ? "" : t.coordinator;
                w.teams.Add(wt);
            }
            w.active = true;
            // anyone a rival lost to you before this world existed is replaced straight away
            foreach (var wt in w.teams) SeasonRollover.FillSeats(c, c.world, wt, new System.Random(c.season * 7919 + SeasonRollover.Hash(wt.id)));
        }
        public static TeamDef FindTeam(string id) { return Cfg.teams.FirstOrDefault(t => t.id == id); }

        public static T Clone<T>(T obj) { return JsonUtility.FromJson<T>(JsonUtility.ToJson(obj)); }

        /// <summary>Copy that keeps the real type (driver / engineer / coordinator) and all its ratings.</summary>
        public static PersonDef ClonePerson(PersonDef p)
        {
            if (p is DriverDef) return Clone((DriverDef)p);
            if (p is EngineerDef) return Clone((EngineerDef)p);
            if (p is CoordinatorDef) return Clone((CoordinatorDef)p);
            return Clone(p);
        }

        public static bool TeamConfigured(TeamDef t)
        {
            if (t == null || string.IsNullOrEmpty(t.name) || t.startingBudget < 0) return false;
            if (t.carParts == null || t.carParts.Length != 6 || t.carParts.Any(p => p < 0)) return false;
            if (t.drivers == null || t.drivers.Length != 2 || t.drivers.Any(id => FindDriver(id) == null)) return false;
            if (t.engineers == null || t.engineers.Length != 3 || t.engineers.Any(id => FindEngineer(id) == null)) return false;
            return FindCoordinator(t.coordinator) != null;
        }

        public static bool CustomConfigured
        {
            get
            {
                var c = Cfg.customTeam;
                return c != null && c.startingBudget >= 0 && c.carParts != null && c.carParts.Length == 6
                    && c.carParts.All(p => p >= 0) && !string.IsNullOrEmpty(c.joinMode);
            }
        }

        // ---------- Timers ----------
        /// <summary>
        /// Call ONCE when a race is completed (the race system does this; lobby buttons never do).
        /// Counts down driver training, coordinator study and the car upgrade, and applies finished ones.
        /// </summary>
        public static void AdvanceTimers(CareerSave c, string eventName)
        {
            foreach (var d in c.drivers)
            {
                var j = d.training;
                if (!d.filled || !j.active) continue;
                if (--j.remainingEvents > 0) continue;
                int before = d.def.stats[j.stat];
                d.def.stats[j.stat] = Mathf.Min(d.def.potential[j.stat], before + Mathf.Max(0, Cfg.driverTraining.gain));
                d.recent.Add(new ChangeEntry { when = eventName, stat = Stat.Driver[j.stat], change = d.def.stats[j.stat] - before, note = "training complete" });
                d.training = new Job();
            }
            var s = c.coordinator.study;
            if (c.coordinator.filled && s.active && --s.remainingEvents <= 0)
            {
                c.coordinator.def.stats[s.stat] = Mathf.Min(100, c.coordinator.def.stats[s.stat] + Mathf.Max(0, Cfg.coordinatorStudy.gain));
                c.coordinator.study = new Job();
            }
            var u = c.carUpgrade;
            if (u.active && --u.remainingEvents <= 0)
            {
                c.carParts[u.stat] = Mathf.Min(100, c.carParts[u.stat] + Mathf.Max(0, Cfg.carUpgrade.gain));
                c.carUpgrade = new Job();
            }
            var pc = c.pitCrewJob;
            if (pc.active && --pc.remainingEvents <= 0)
            {
                c.pitCrew = Mathf.Min(100, PitCrew(c) + Mathf.Max(0, Cfg.pitCrewUpgrade.gain));
                c.pitCrewJob = new Job();
            }
        }

        // ---------- Pit crew ----------
        /// <summary>Your pit crew rating; older saves take it from the team they started with.</summary>
        public static int PitCrew(CareerSave c)
        {
            if (c.pitCrew >= 0) return c.pitCrew;
            if (c.baseTeamId == "custom") return Cfg.customTeam.pitCrew >= 0 ? Cfg.customTeam.pitCrew : 50;
            var t = FindTeam(c.baseTeamId);
            return t != null && t.pitCrew >= 0 ? t.pitCrew : 50;
        }

        /// <summary>Typical stationary time for a crew rating (no mistakes).</summary>
        public static float PitServiceTime(float crew) { return 2.1f + (100f - crew) * 0.045f; }

        /// <summary>Chance of a slow stop (stuck wheel nut, unsafe-release hold) for a crew rating.</summary>
        public static float SlowStopChance(float crew) { return 0.015f + (100f - crew) * 0.002f; }

        // ---------- Opponent difficulty ----------
        public static int DifficultyIndex(CareerSave c)
        {
            int n = Cfg.difficulties.Count;
            if (n == 0) return -1;
            int i = c != null && c.difficulty >= 0 ? c.difficulty : Cfg.defaultDifficulty;
            return Mathf.Clamp(i, 0, n - 1);
        }

        public static string DifficultyName(CareerSave c)
        {
            int i = DifficultyIndex(c);
            return i < 0 ? Theme.NotConfigured : Cfg.difficulties[i].name;
        }

        public static int RivalOffset(CareerSave c)
        {
            int i = DifficultyIndex(c);
            return i < 0 ? 0 : Cfg.difficulties[i].rivalOffset;
        }

        /// <summary>A copy of a rival's ratings with the difficulty offset applied (unknown values stay unknown).</summary>
        public static int[] Adjust(int[] stats, int offset)
        {
            if (stats == null) return null;
            if (offset == 0) return stats;
            var r = new int[stats.Length];
            for (int i = 0; i < stats.Length; i++) r[i] = stats[i] < 0 ? stats[i] : Mathf.Clamp(stats[i] + offset, 1, 100);
            return r;
        }

        // ---------- Rival teams (everyone in the championship except you) ----------
        public class TeamView
        {
            public string id, name;
            public Color color;
            public int logo;
            public bool isPlayer;
            public int[] car;
            public List<int[]> driverStats = new List<int[]>();
            public List<int[]> engineerStats = new List<int[]>();
            public int[] coordStats;
            public int pitCrew = 50;
            public List<string> driverIds = new List<string>();
            public List<string> driverNames = new List<string>();
        }

        public static List<TeamView> Championship(CareerSave c)
        {
            var list = new List<TeamView>();
            string replaced = null;
            if (c.baseTeamId == "custom" && Cfg.customTeam.joinMode.StartsWith("replace:"))
                replaced = Cfg.customTeam.joinMode.Substring("replace:".Length);

            int off = RivalOffset(c);
            foreach (var t in Cfg.teams)
            {
                if (t.id == c.baseTeamId) { list.Add(PlayerView(c)); continue; }
                if (t.id == replaced) continue;
                var wt = FindWorldTeam(c, t.id);
                var v = new TeamView { id = t.id, name = t.name, color = Theme.Hex(t.color), logo = t.logo, car = Adjust(wt != null ? wt.carParts : t.carParts, off) };
                v.pitCrew = Mathf.Clamp((wt != null ? wt.pitCrew : t.pitCrew >= 0 ? t.pitCrew : 60) + off, 1, 100);
                IEnumerable<string> drivers = wt != null ? (IEnumerable<string>)wt.drivers : t.drivers;
                IEnumerable<string> engineers = wt != null ? (IEnumerable<string>)wt.engineers : t.engineers;
                string coordinator = wt != null ? wt.coordinator : t.coordinator;
                foreach (var id in drivers)
                {
                    var d = FindDriver(id);
                    if (d != null && !c.takenPeople.Contains(id)) { v.driverStats.Add(Adjust(d.stats, off)); v.driverIds.Add(d.id); v.driverNames.Add(d.name); }
                }
                foreach (var id in engineers)
                {
                    var e = FindEngineer(id);
                    if (e != null && !c.takenPeople.Contains(id)) v.engineerStats.Add(Adjust(e.stats, off));
                }
                var co = string.IsNullOrEmpty(coordinator) ? null : FindCoordinator(coordinator);
                v.coordStats = (co != null && !c.takenPeople.Contains(co.id)) ? Adjust(co.stats, off) : null;
                list.Add(v);
            }
            if (c.baseTeamId == "custom") list.Add(PlayerView(c));
            return list;
        }

        public static TeamView PlayerView(CareerSave c)
        {
            var v = new TeamView { id = PlayerTeamId(c), name = c.teamName, color = Theme.Hex(c.teamColor), logo = c.logo, isPlayer = true, car = c.carParts };
            foreach (var d in c.drivers) if (d.filled) { v.driverStats.Add(d.def.stats); v.driverIds.Add(d.def.id); v.driverNames.Add(d.def.name); }
            foreach (var e in c.engineers) if (e.filled) v.engineerStats.Add(e.def.stats);
            v.coordStats = c.coordinator.filled ? c.coordinator.def.stats : null;
            v.pitCrew = PitCrew(c);
            return v;
        }

        public static string PlayerTeamId(CareerSave c) { return c.baseTeamId == "custom" ? "player" : c.baseTeamId; }

        public static float AvgOveralls(List<int[]> stats)
        {
            if (stats == null || stats.Count == 0) return float.NaN;
            var o = stats.Select(Overall).ToList();
            if (o.Any(float.IsNaN)) return float.NaN;
            return o.Average();
        }

        /// <summary>Rank (1 = best) of the player among all teams by a metric, or -1 if unknown.</summary>
        public static int Rank(CareerSave c, Func<TeamView, float> metric, out int teamCount)
        {
            var teams = Championship(c);
            teamCount = teams.Count;
            var vals = teams.Select(t => new { t, v = metric(t) }).ToList();
            if (vals.Any(x => float.IsNaN(x.v))) return -1;
            var me = vals.FirstOrDefault(x => x.t.isPlayer);
            if (me == null) return -1;
            return 1 + vals.Count(x => x.v > me.v);
        }

        // ---------- Finances (per race) ----------
        public class Finance
        {
            public int activeSponsors;
            public long guaranteed, bonuses;
            public long driverCosts, staffCosts;
            public long raceOps = -1;
            public bool salariesKnown = true;
            public long TotalCosts { get { return (raceOps < 0 || !salariesKnown) ? -1 : driverCosts + staffCosts + raceOps; } }
            public bool ProfitKnown { get { return TotalCosts >= 0; } }
            public long Profit { get { return guaranteed - TotalCosts; } }
        }

        public static Finance Finances(CareerSave c)
        {
            var f = new Finance();
            foreach (var s in c.sponsors) if (s.active) { f.activeSponsors++; f.guaranteed += s.guaranteedPerRace; f.bonuses += s.bonusAmount; }
            foreach (var d in c.drivers) if (d.filled) { if (d.def.salaryPerRace < 0) f.salariesKnown = false; else f.driverCosts += d.def.salaryPerRace; }
            foreach (var e in c.engineers) if (e.filled) { if (e.def.salaryPerRace < 0) f.salariesKnown = false; else f.staffCosts += e.def.salaryPerRace; }
            if (c.coordinator.filled) { if (c.coordinator.def.salaryPerRace < 0) f.salariesKnown = false; else f.staffCosts += c.coordinator.def.salaryPerRace; }
            f.raceOps = Cfg.raceOperationsPerRace;
            return f;
        }

        public static long Payroll(IEnumerable<PersonDef> people)
        {
            long t = 0;
            foreach (var p in people) { if (p.salaryPerRace < 0) return -1; t += p.salaryPerRace; }
            return t;
        }

        // ---------- Calendar / results ----------
        public static int Rounds { get { return Cfg.calendar.Count; } }

        public static EventDef NextEvent(CareerSave c)
        {
            int i = c.nextRound - 1;
            return (i >= 0 && i < Cfg.calendar.Count) ? Cfg.calendar[i] : null;
        }

        public static int Level(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.ToLowerInvariant();
            return s == "easy" ? 0 : s == "medium" ? 1 : s == "hard" ? 2 : -1;
        }

        public static SeasonRecord Season(CareerSave c, int season)
        {
            return c.seasons.FirstOrDefault(s => s.season == season);
        }

        public class StandingRow { public string id, name; public Color color; public int points; public bool mine; }

        /// <summary>Points totals from saved results. Order ties exactly as stored (tie-break rules not configured).</summary>
        public static List<StandingRow> TeamStandings(CareerSave c)
        {
            var teams = Championship(c);
            var pts = new Dictionary<string, int>();
            var rec = Season(c, c.season);
            if (rec != null)
                foreach (var r in rec.results)
                    foreach (var cl in r.classification)
                    {
                        int p; pts.TryGetValue(cl.teamId, out p); pts[cl.teamId] = p + cl.points;
                    }
            return teams.Select(t => { int p; pts.TryGetValue(t.id, out p); return new StandingRow { id = t.id, name = t.name, color = t.color, points = p, mine = t.isPlayer }; })
                        .OrderByDescending(r => r.points).ToList();
        }

        public static List<StandingRow> DriverStandings(CareerSave c)
        {
            var rows = new List<StandingRow>();
            var mine = new HashSet<string>();
            foreach (var d in c.drivers) if (d.filled) mine.Add(d.def.id);
            foreach (var t in Championship(c))
                for (int i = 0; i < t.driverIds.Count; i++)
                    rows.Add(new StandingRow { id = t.driverIds[i], name = t.driverNames[i], color = t.color, mine = mine.Contains(t.driverIds[i]) });
            var rec = Season(c, c.season);
            if (rec != null)
                foreach (var r in rec.results)
                    foreach (var cl in r.classification)
                    {
                        var row = rows.FirstOrDefault(x => x.id == cl.driverId);
                        if (row != null) row.points += cl.points;
                    }
            return rows.OrderByDescending(r => r.points).ToList();
        }

        public static bool HasResults(CareerSave c)
        {
            var rec = Season(c, c.season);
            return rec != null && rec.results.Count > 0;
        }

        // ---------- Transfer pool ----------
        public static bool OnPlayerTeam(CareerSave c, string id)
        {
            foreach (var d in c.drivers) if (d.filled && d.def.id == id) return true;
            foreach (var e in c.engineers) if (e.filled && e.def.id == id) return true;
            return c.coordinator.filled && c.coordinator.def.id == id;
        }

        public static string TeamNameOf(CareerSave c, PersonDef p)
        {
            if (c != null && OnPlayerTeam(c, p.id)) return c.teamName;
            if (string.IsNullOrEmpty(p.teamId)) return "Free Agent";
            if (c != null && p.teamId == c.baseTeamId) return "Free Agent"; // your base team's original roster
            if (c != null && c.world != null && c.world.active && FindWorldTeam(c, p.teamId) == null) return "Free Agent";
            var t = FindTeam(p.teamId);
            return t != null ? t.name : "Free Agent";
        }

        public static bool IsFreeAgent(CareerSave c, PersonDef p) { return TeamNameOf(c, p) == "Free Agent"; }

        // ---------- Market value (what someone asks for when signing or renewing) ----------
        public static long DriverSalary(float ovr)
        {
            long s = ovr >= 92 ? 950_000 : ovr >= 89 ? 700_000 : ovr >= 86 ? 480_000 : ovr >= 83 ? 320_000 : ovr >= 80 ? 210_000 : ovr >= 76 ? 140_000 : ovr >= 70 ? 90_000 : 50_000;
            return s;
        }

        public static long StaffSalary(float ovr) { return Math.Max(20_000, RoundTo(70_000 * (1.0 + (ovr - 70) * 0.06), 1000)); }
        public static long CoordSalary(float ovr) { return Math.Max(30_000, RoundTo(90_000 * (1.0 + (ovr - 70) * 0.07), 1000)); }

        public static long MarketSalary(PersonDef p)
        {
            float o = Overall(People.StatsOf(p));
            if (float.IsNaN(o)) return p.salaryPerRace;
            if (p is DriverDef) return DriverSalary(o);
            if (p is EngineerDef) return StaffSalary(o);
            return CoordSalary(o);
        }

        public static long RoundTo(double v, long step) { return (long)(Math.Round(v / step) * step); }

        public static int BestIndex(int[] stats)
        {
            if (stats == null || stats.Length == 0) return -1;
            int b = 0;
            for (int i = 1; i < stats.Length; i++) if (stats[i] > stats[b]) b = i;
            return b;
        }
    }
}
