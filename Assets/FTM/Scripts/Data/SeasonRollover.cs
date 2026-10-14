using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// End of season: final standings and titles, season-long sponsor bonuses, contracts, aging and development,
    /// retirements, rookies, rival car development, the Next Year Car Fund and rival transfers.
    /// Run() works on a copy of the career with a seeded random generator, so the review you see is exactly
    /// what gets applied when you start the next season. Careers can roll over forever.
    /// </summary>
    public static class SeasonRollover
    {
        static GameConfig Cfg { get { return ConfigLoader.Config; } }
        static SeasonRules R { get { return Cfg.seasonRules; } }

        public static bool SeasonComplete(CareerSave c)
        {
            return c != null && Rules.Rounds > 0 && c.nextRound > Rules.Rounds && !RaceFlow.HasActive(c);
        }

        public static bool CarRulesSet { get { return R != null && R.winterLoss >= 0 && R.fundPerPoint > 0; } }

        public static int Hash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char ch in s ?? "") h = h * 31 + ch;
                return h & 0x7fffffff;
            }
        }

        // ================================================================== review data

        public class Expiring
        {
            public PersonKind kind;
            public int slot;
            public string id, name;
            public float overall;
            public int age;
            public long oldSalary, newSalary, fee;
        }

        public class PersonChange { public string name, role; public float before, after; public int age; }

        public class Review
        {
            public int season;
            public int teamPos, teamPoints, teamCount;
            public string champTeam = "", champDriver = "";
            public bool teamChamp, driverChamp;
            public List<string> bonusLines = new List<string>();
            public long bonusTotal;
            public int[] carBefore = new int[6], carAfter = new int[6], fundGain = new int[6];
            public long fundSpent;
            public List<Expiring> expiring = new List<Expiring>();
            public List<PersonChange> yourPeople = new List<PersonChange>();
            public List<string> yourRetired = new List<string>();
            public List<string> retired = new List<string>();
            public List<string> rookies = new List<string>();
            public List<string> moves = new List<string>();
            public List<string> rivalCars = new List<string>();
        }

        // ================================================================== the rollover

        /// <summary>Rolls `next` (a copy of the career) into the following season and returns what happened.</summary>
        public static Review Run(CareerSave next)
        {
            var c = next;
            Rules.EnsureWorld(c);
            var w = c.world;
            var rng = new System.Random((int)(c.createdTicks & 0x3fffffff) ^ (c.season * 104729) ^ Hash(c.teamName));
            var rv = new Review { season = c.season };

            // ---- 1. final standings and titles (before anything changes)
            var teams = Rules.TeamStandings(c);
            var drivers = Rules.DriverStandings(c);
            rv.teamCount = teams.Count;
            rv.teamPos = teams.FindIndex(t => t.mine) + 1;
            rv.teamPoints = rv.teamPos > 0 ? teams[rv.teamPos - 1].points : 0;
            rv.champTeam = teams.Count > 0 && teams[0].points > 0 ? teams[0].name : "--";
            rv.champDriver = drivers.Count > 0 && drivers[0].points > 0 ? drivers[0].name : "--";
            rv.teamChamp = rv.teamPos == 1 && rv.teamPoints > 0;
            rv.driverChamp = drivers.Count > 0 && drivers[0].mine && drivers[0].points > 0;
            var rec = Rules.Season(c, c.season);
            if (rec == null) { rec = new SeasonRecord { season = c.season }; c.seasons.Add(rec); }
            rec.teamChampion = rv.teamChamp;
            rec.driverChampion = rv.driverChamp;
            rec.teamPosition = rv.teamPos;
            rec.championTeam = rv.champTeam;
            rec.championDriver = rv.champDriver;

            // ---- 2. season-long sponsor targets
            foreach (var sp in c.sponsors)
            {
                if (!sp.active || sp.bonusAmount <= 0 || string.IsNullOrEmpty(sp.bonusCondition)) continue;
                string cond = sp.bonusCondition.ToLowerInvariant();
                bool? met = null;
                if (cond.Contains("championship"))
                {
                    int n = FirstNumber(cond);
                    met = rv.teamPos > 0 && rv.teamPos <= (n > 0 ? n : 1);
                }
                else if (cond.Contains("this season"))
                    met = rec.results.Count >= Rules.Rounds && rec.results.All(r => r.driver1Pos > 0 && r.driver2Pos > 0);
                if (met == null) continue;
                if (met == true) { rv.bonusTotal += sp.bonusAmount; rv.bonusLines.Add(sp.name + ": " + sp.bonusCondition + "  +" + Theme.Money(sp.bonusAmount)); }
                else rv.bonusLines.Add(sp.name + ": " + sp.bonusCondition + "  - missed");
            }
            c.balance += rv.bonusTotal;

            // ---- 3. contracts
            var taken = new HashSet<string>(c.takenPeople);
            foreach (var p in Mine(c))
            {
                p.def.contractSeasons = Math.Max(0, p.def.contractSeasons - 1);
            }
            foreach (var wt in w.teams)
            {
                foreach (var id in wt.drivers.Concat(wt.engineers).Concat(new[] { wt.coordinator }).Where(x => !string.IsNullOrEmpty(x)).ToList())
                {
                    var p = FindAny(w, id);
                    if (p == null) continue;
                    p.contractSeasons = Math.Max(0, p.contractSeasons - 1);
                    if (p.contractSeasons > 0) continue;
                    if (rng.Next(100) < R.rivalRenewChance) { p.contractSeasons = rng.Next(1, 4); p.salaryPerRace = Rules.MarketSalary(p); continue; }
                    Leave(w, wt, p);
                    rv.moves.Add(p.name + " leaves " + TeamName(wt.id) + " (contract ended)");
                }
            }

            // ---- 4. aging and development
            foreach (var m in Mine(c))
            {
                float before = Rules.Overall(People.StatsOf(m.def));
                var changes = Develop(m.def, rng);
                float after = Rules.Overall(People.StatsOf(m.def));
                rv.yourPeople.Add(new PersonChange { name = m.def.name, role = m.role, before = before, after = after, age = m.def.age });
                if (m.kind == PersonKind.Driver)
                    foreach (var ch in changes)
                        c.drivers[m.slot].recent.Add(new ChangeEntry { when = "Season " + c.season + " end", stat = ch.Key, change = ch.Value, note = ch.Value > 0 ? "development" : "age" });
            }
            foreach (var p in AllWorld(w).Where(q => !taken.Contains(q.id))) Develop(p, rng);

            // ---- 5. retirements
            foreach (var m in Mine(c).ToList())
            {
                if (!Retires(m.def, rng)) continue;
                rv.yourRetired.Add(m.def.name + " (" + m.role + ", age " + m.def.age + ")");
                rv.retired.Add(m.def.name + " (your " + m.role.ToLowerInvariant() + ", " + m.def.age + ")");
                w.retired.Add(m.def.name);
                c.takenPeople.Remove(m.def.id);
                RemoveFromWorld(w, m.def.id);
                Vacate(c, m.kind, m.slot);
            }
            foreach (var p in AllWorld(w).Where(q => !taken.Contains(q.id)).ToList())
            {
                if (!Retires(p, rng)) continue;
                foreach (var wt in w.teams) RemoveFromTeam(wt, p.id);
                RemoveFromWorld(w, p.id);
                w.retired.Add(p.name);
                if (p is DriverDef || Rules.Overall(People.StatsOf(p)) >= 80) rv.retired.Add(p.name + (p is DriverDef ? " (driver, " : p is EngineerDef ? " (engineer, " : " (coordinator, ") + p.age + ")");
            }

            // ---- 6. contracts that ran out on your team (decided on the review screen)
            foreach (var m in Mine(c))
            {
                if (m.def.contractSeasons > 0) continue;
                long sal = Rules.MarketSalary(m.def);
                rv.expiring.Add(new Expiring
                {
                    kind = m.kind, slot = m.slot, id = m.def.id, name = m.def.name, overall = Rules.Overall(People.StatsOf(m.def)), age = m.def.age,
                    oldSalary = m.def.salaryPerRace, newSalary = sal, fee = sal * 3
                });
            }

            // ---- 7. rookies
            for (int i = 0; i < Math.Max(0, R.rookieDrivers); i++) rv.rookies.Add(NewDriver(c, rng).name + " (driver)");
            for (int i = 0; i < Math.Max(0, R.rookieEngineers); i++) NewEngineer(c, rng);
            for (int i = 0; i < Math.Max(0, R.rookieCoordinators); i++) NewCoordinator(c, rng);

            // ---- 8. cars: new regulations, development, your fund
            Array.Copy(c.carParts, rv.carBefore, 6);
            if (CarRulesSet)
            {
                var gains = FundGains(c);
                for (int i = 0; i < 6; i++)
                {
                    rv.fundGain[i] = gains[i];
                    c.carParts[i] = Mathf.Clamp(c.carParts[i] - R.winterLoss + gains[i], 1, 100);
                }
                rv.fundSpent = c.nextYearFund;
                c.nextYearFund = 0;
                foreach (var wt in w.teams)
                {
                    float before = (float)wt.carParts.Average();
                    for (int i = 0; i < 6; i++)
                    {
                        int part = wt.carParts[i];
                        int d = -R.winterLoss + (int)Math.Round(R.winterLoss + 0.3 + Gauss(rng) * 1.2);
                        if (part >= 93 && rng.Next(2) == 0) d -= 1;   // hard to find more at the front
                        if (part <= 65) d += 1;                      // the back of the grid catches up a little
                        wt.carParts[i] = Mathf.Clamp(part + d, 40, 99);
                    }
                    wt.pitCrew = Mathf.Clamp(wt.pitCrew + rng.Next(-2, 4), 30, 99);
                    float after = (float)wt.carParts.Average();
                    rv.rivalCars.Add(TeamName(wt.id) + "  " + Theme.OneDecimal(before) + " → " + Theme.OneDecimal(after));
                }
            }
            Array.Copy(c.carParts, rv.carAfter, 6);

            // ---- 9. rival teams fill their seats (best cars choose first)
            foreach (var wt in w.teams.OrderByDescending(t => t.carParts.Average()).ToList())
                foreach (var line in FillSeats(c, w, wt, rng)) rv.moves.Add(line);

            // ---- 10. free agents' asking prices follow their new ratings
            foreach (var p in AllWorld(w).Where(q => !taken.Contains(q.id)))
            {
                p.salaryPerRace = Rules.MarketSalary(p);
                bool onTeam = !string.IsNullOrEmpty(p.teamId) && w.teams.Any(t => t.id == p.teamId);
                if (!onTeam)
                {
                    p.teamId = "";
                    p.buyoutFee = -1;
                    if (p.contractSeasons <= 0) p.contractSeasons = rng.Next(1, 4);
                    p.signingBonus = Rules.RoundTo(p.salaryPerRace * (p.age <= 22 ? 4 : 6), 10000);
                }
                else p.buyoutFee = Rules.RoundTo(p.salaryPerRace * (p is DriverDef ? 22 : p is EngineerDef ? 18 : 20), 10000);
            }

            // ---- 11. the new season
            c.season += 1;
            c.nextRound = 1;
            c.lastEvent = new EventSummary();
            rec.notes = new List<string>();
            if (rv.teamChamp) rec.notes.Add("Constructors' champions");
            if (rv.driverChamp) rec.notes.Add("Drivers' champion");
            return rv;
        }

        /// <summary>Your renew / release decisions, then the copy replaces the live career.</summary>
        public static void ApplyDecisions(CareerSave next, Review rv, Dictionary<string, int> renewYears)
        {
            foreach (var e in rv.expiring)
            {
                int years;
                PersonDef def = DefAt(next, e.kind, e.slot);
                if (def == null || def.id != e.id) continue;
                if (renewYears.TryGetValue(e.id, out years) && years > 0)
                {
                    def.contractSeasons = years;
                    def.salaryPerRace = e.newSalary;
                    next.balance -= e.fee;
                }
                else
                {
                    // released: back into the world as a free agent, keeping their current ratings
                    var copy = Rules.ClonePerson(def);
                    copy.teamId = ""; copy.buyoutFee = -1; copy.contractSeasons = 2;
                    copy.signingBonus = Rules.RoundTo(e.newSalary * 6, 10000);
                    copy.salaryPerRace = e.newSalary;
                    PutInWorld(next.world, copy);
                    next.takenPeople.Remove(def.id);
                    Vacate(next, e.kind, e.slot);
                }
            }
        }

        // ================================================================== Next Year Car Fund

        public static float EngineerAvg(CareerSave c, int part)
        {
            var vals = c.engineers.Where(e => e.filled).Select(e => Rules.StatAt(e.def.stats, part)).Where(v => v >= 0).ToList();
            return vals.Count == 0 ? 50f : (float)vals.Average();
        }

        public static float[] Allocation(CareerSave c)
        {
            var wts = c.fundWeights != null && c.fundWeights.Length == 6 ? c.fundWeights : new[] { 2, 2, 2, 2, 2, 2 };
            float sum = wts.Sum();
            var a = new float[6];
            for (int i = 0; i < 6; i++) a[i] = sum > 0 ? wts[i] / sum : 1f / 6f;
            return a;
        }

        /// <summary>Points each part gains next season from the fund (engineers rated 80 on a part = 1 point per fundPerPoint).</summary>
        public static int[] FundGains(CareerSave c)
        {
            var g = new int[6];
            if (!CarRulesSet || c.nextYearFund <= 0) return g;
            var a = Allocation(c);
            var raw = new double[6];
            double total = 0;
            for (int i = 0; i < 6; i++) { raw[i] = c.nextYearFund * a[i] / R.fundPerPoint * (EngineerAvg(c, i) / 80f); g[i] = (int)Math.Floor(raw[i]); total += raw[i]; }
            // no money is wasted on rounding: leftover points go to the parts closest to their next point
            int left = (int)Math.Round(total) - g.Sum();
            foreach (int i in Enumerable.Range(0, 6).Where(k => a[k] > 0).OrderByDescending(k => raw[k] - Math.Floor(raw[k])).Take(Math.Max(0, left))) g[i]++;
            return g;
        }

        // ================================================================== people

        class Member { public PersonKind kind; public int slot; public PersonDef def; public string role; }

        static List<Member> Mine(CareerSave c)
        {
            var list = new List<Member>();
            for (int i = 0; i < 2; i++) if (c.drivers[i].filled) list.Add(new Member { kind = PersonKind.Driver, slot = i, def = c.drivers[i].def, role = "Driver " + (i + 1) });
            for (int i = 0; i < 3; i++) if (c.engineers[i].filled) list.Add(new Member { kind = PersonKind.Engineer, slot = i, def = c.engineers[i].def, role = "Engineer " + (i + 1) });
            if (c.coordinator.filled) list.Add(new Member { kind = PersonKind.Coordinator, slot = 0, def = c.coordinator.def, role = "Coordinator" });
            return list;
        }

        static PersonDef DefAt(CareerSave c, PersonKind k, int slot)
        {
            if (k == PersonKind.Driver) return c.drivers[slot].filled ? c.drivers[slot].def : null;
            if (k == PersonKind.Engineer) return c.engineers[slot].filled ? c.engineers[slot].def : null;
            return c.coordinator.filled ? c.coordinator.def : null;
        }

        static void Vacate(CareerSave c, PersonKind k, int slot)
        {
            if (k == PersonKind.Driver) c.drivers[slot] = new DriverState();
            else if (k == PersonKind.Engineer) c.engineers[slot] = new EngineerState();
            else c.coordinator = new CoordinatorState();
        }

        static IEnumerable<PersonDef> AllWorld(WorldState w)
        {
            return w.drivers.Cast<PersonDef>().Concat(w.engineers).Concat(w.coordinators);
        }

        static PersonDef FindAny(WorldState w, string id)
        {
            return (PersonDef)w.drivers.FirstOrDefault(x => x.id == id) ?? (PersonDef)w.engineers.FirstOrDefault(x => x.id == id) ?? w.coordinators.FirstOrDefault(x => x.id == id);
        }

        static void RemoveFromWorld(WorldState w, string id)
        {
            w.drivers.RemoveAll(x => x.id == id);
            w.engineers.RemoveAll(x => x.id == id);
            w.coordinators.RemoveAll(x => x.id == id);
        }

        /// <summary>Adds or replaces a person in the world (e.g. someone you released, with their trained ratings).</summary>
        public static void PutInWorld(WorldState w, PersonDef p)
        {
            if (w == null || !w.active || p == null) return;
            RemoveFromWorld(w, p.id);
            foreach (var t in w.teams) RemoveFromTeam(t, p.id);
            if (p is DriverDef) w.drivers.Add((DriverDef)p);
            else if (p is EngineerDef) w.engineers.Add((EngineerDef)p);
            else if (p is CoordinatorDef) w.coordinators.Add((CoordinatorDef)p);
        }

        static void RemoveFromTeam(WorldTeam t, string id)
        {
            t.drivers.Remove(id);
            t.engineers.Remove(id);
            if (t.coordinator == id) t.coordinator = "";
        }

        static void Leave(WorldState w, WorldTeam t, PersonDef p)
        {
            RemoveFromTeam(t, p.id);
            p.teamId = "";
            p.buyoutFee = -1;
            p.contractSeasons = 0;
        }

        static string TeamName(string id)
        {
            var t = Rules.FindTeam(id);
            return t != null ? t.name : id;
        }

        /// <summary>One season older: young people grow toward their potential, veterans decline. Returns stat changes.</summary>
        public static Dictionary<string, int> Develop(PersonDef p, System.Random rng)
        {
            var changes = new Dictionary<string, int>();
            int a = p.age;
            if (p.age >= 0) p.age++;
            var d = p as DriverDef;
            int[] st = People.StatsOf(p);
            if (st == null || st.Length == 0 || a < 0) return changes;
            string[] names = d != null ? Stat.Driver : p is EngineerDef ? Stat.Part : Stat.Coord;
            for (int i = 0; i < st.Length; i++)
            {
                if (st[i] < 0) continue;
                int delta;
                if (d != null)
                {
                    bool fit = i == 4;
                    if (a <= 22) delta = rng.Next(1, 4);
                    else if (a <= 26) delta = rng.Next(0, 3);
                    else if (a <= 31) delta = rng.Next(100) < 35 ? 1 : 0;
                    else if (a <= 33) delta = rng.Next(100) < 20 ? -1 : 0;
                    else if (a <= 37) delta = -rng.Next(0, 2) - (fit ? 1 : 0);
                    else delta = -rng.Next(1, 3) - (fit ? 1 : 0);
                    int cap = d.potential != null && i < d.potential.Length && d.potential[i] > 0 ? d.potential[i] : 100;
                    int before = st[i];
                    st[i] = delta > 0 ? Math.Min(cap, st[i] + delta) : Math.Max(1, st[i] + delta);
                    if (a >= 34 && d.potential != null && i < d.potential.Length) d.potential[i] = Math.Max(st[i], d.potential[i] - 1);
                    if (st[i] != before) changes[names[i]] = st[i] - before;
                }
                else
                {
                    if (a <= 34) delta = rng.Next(0, 2);
                    else if (a <= 54) delta = rng.Next(100) < 15 ? 1 : 0;
                    else delta = -rng.Next(0, 2);
                    int before = st[i];
                    st[i] = Mathf.Clamp(st[i] + delta, 1, 99);
                    if (st[i] != before) changes[names[i]] = st[i] - before;
                }
            }
            return changes;
        }

        static bool Retires(PersonDef p, System.Random rng)
        {
            if (p.age < 0) return false;
            bool free = string.IsNullOrEmpty(p.teamId);
            if (p is DriverDef)
            {
                if (p.age >= 42) return true;
                if (p.age < 36) return false;
                double chance = (p.age - 35) * 0.18 + (free ? 0.25 : 0);
                return rng.NextDouble() < chance;
            }
            if (p.age >= 67) return true;
            if (p.age < 60) return false;
            return rng.NextDouble() < (p.age - 59) * 0.2 + (free ? 0.15 : 0);
        }

        // ================================================================== rival seats

        /// <summary>Rival team signs the best available free agents for any empty seat. Returns news lines.</summary>
        public static List<string> FillSeats(CareerSave c, WorldState w, WorldTeam t, System.Random rng)
        {
            var news = new List<string>();
            if (w == null || t == null) return news;
            Func<PersonDef, bool> free = p => string.IsNullOrEmpty(p.teamId) && !c.takenPeople.Contains(p.id) && !Rules.OnPlayerTeam(c, p.id);
            Func<PersonDef, double> score = p => { float o = Rules.Overall(People.StatsOf(p)); return (float.IsNaN(o) ? 50 : o) + rng.NextDouble() * 5; };

            while (t.drivers.Count < 2)
            {
                var p = w.drivers.Where(x => free(x)).OrderByDescending(x => score(x)).FirstOrDefault() ?? NewDriver(c, rng);
                Join(t, p, rng); t.drivers.Add(p.id);
                news.Add(TeamName(t.id) + " signs " + p.name);
            }
            while (t.engineers.Count < 3)
            {
                var p = w.engineers.Where(x => free(x)).OrderByDescending(x => score(x)).FirstOrDefault() ?? NewEngineer(c, rng);
                Join(t, p, rng); t.engineers.Add(p.id);
            }
            if (string.IsNullOrEmpty(t.coordinator))
            {
                var p = w.coordinators.Where(x => free(x)).OrderByDescending(x => score(x)).FirstOrDefault() ?? NewCoordinator(c, rng);
                Join(t, p, rng); t.coordinator = p.id;
                news.Add(TeamName(t.id) + " appoints " + p.name + " as coordinator");
            }
            return news;
        }

        static void Join(WorldTeam t, PersonDef p, System.Random rng)
        {
            p.teamId = t.id;
            p.contractSeasons = rng.Next(1, 4);
            p.salaryPerRace = Rules.MarketSalary(p);
            p.buyoutFee = Rules.RoundTo(p.salaryPerRace * (p is DriverDef ? 22 : p is EngineerDef ? 18 : 20), 10000);
        }

        // ================================================================== rookies

        static readonly string[] First = { "Tom", "Marco", "Ana", "Luca", "Hiro", "Sofia", "Ben", "Ines", "Jonas", "Clara", "Raj", "Elena", "Pablo",
            "Mia", "Henrik", "Yuki", "Oliver", "Chloe", "Diego", "Freya", "Adam", "Nadia", "Kai", "Lena", "Sam", "Priya", "Viktor", "Grace",
            "Mateo", "Hannah", "Felix", "Rosa", "Ethan", "Ayla", "Noah", "Ivy", "Leon", "Sara", "Tariq", "Emma", "Paolo", "Kenji", "Lucia",
            "Max", "Nora", "Omar", "Zoe", "Ruben", "Alba", "Theo", "Greta", "Jamal", "Ingrid", "Bruno", "Aiko", "Callum", "Esme", "Dario", "Lina",
            "Arvid", "Enzo", "Joao", "Rafael", "Sebastian", "Kimi", "Dino", "Owen", "Ryo", "Matias", "Hugo", "Elias", "Jake", "Liam", "Nico" };
        static readonly string[] Last = { "Hartley", "Bianchi", "Moreau", "Keller", "Tanaka", "Lindqvist", "Okafor", "Ferreira", "Novak", "Brandt",
            "Sato", "Russo", "Delgado", "Whitfield", "Kowalski", "Haugen", "Marchetti", "Dubois", "Castillo", "Ashworth", "Vogel", "Petrov",
            "Mercer", "Lund", "Rinaldi", "Fontaine", "Yamada", "Carver", "Ortega", "Blackwood", "Engel", "Moretti", "Laurent", "Quinn",
            "Sinclair", "Varga", "Holm", "Ricci", "Navarro", "Pike", "Strand", "Vidal", "Kimura", "Barlow", "Conti", "Faure", "Hale", "Ibarra",
            "Janssen", "Kerr", "Lowe", "Mendes", "Nakamura", "Oakley", "Parisi", "Reyes", "Silva", "Thorne", "Ueda", "Weiss", "Aldana",
            "Brennan", "Cortese", "Dahl", "Eriksen", "Falk", "Garrido", "Hyde", "Ishikawa", "Jovanovic", "Kovac", "Lacroix", "Maddox" };
        static readonly string[] Nats = { "GBR", "ITA", "FRA", "GER", "JPN", "SWE", "NGA", "POR", "CZE", "AUT", "ESP", "USA", "POL", "NOR", "NED",
            "BRA", "IND", "AUS", "BEL", "DEN", "FIN", "CAN", "MEX", "SUI", "IRL", "ARG", "CHN", "NZL", "THA", "EST" };

        static string NewName(CareerSave c, System.Random rng)
        {
            var used = new HashSet<string>(AllWorld(c.world).Select(p => p.name));
            foreach (var r in c.world.retired) used.Add(r);
            for (int k = 0; k < 200; k++)
            {
                string n = First[rng.Next(First.Length)] + " " + Last[rng.Next(Last.Length)];
                if (!used.Contains(n)) return n;
            }
            return First[rng.Next(First.Length)] + " " + Last[rng.Next(Last.Length)] + " Jr.";
        }

        static string NewId(CareerSave c, string prefix) { return prefix + "_s" + c.season + "_" + (c.world.nextId++); }

        static int[] Spread(int ovr, int n, int spec, System.Random rng, int boost)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = Mathf.Clamp(ovr + rng.Next(-3, 4) + (i == spec ? boost : 0), 30, 99);
            return a;
        }

        static DriverDef NewDriver(CareerSave c, System.Random rng)
        {
            int ovr = rng.Next(58, 69);
            int pot = Math.Min(95, ovr + rng.Next(12, 27));
            var st = Spread(ovr, 5, 4, rng, 2);
            var potential = new int[5];
            for (int i = 0; i < 5; i++) potential[i] = Mathf.Clamp(pot + rng.Next(-3, 4), st[i], 99);
            var used = new HashSet<int>(c.world.drivers.Select(q => q.raceNumber));
            foreach (var ds in c.drivers) if (ds.filled) used.Add(ds.def.raceNumber);
            int num = Enumerable.Range(2, 98).Where(x => !used.Contains(x)).OrderBy(x => rng.Next()).FirstOrDefault();
            var p = new DriverDef
            {
                id = NewId(c, "d"), name = NewName(c, rng), nationality = Nats[rng.Next(Nats.Length)], age = rng.Next(18, 21), raceNumber = num > 0 ? num : -1,
                stats = st, potential = potential, teamId = "", buyoutFee = -1, contractSeasons = rng.Next(1, 4)
            };
            p.salaryPerRace = Rules.MarketSalary(p);
            p.signingBonus = Rules.RoundTo(p.salaryPerRace * 4, 10000);
            c.world.drivers.Add(p);
            return p;
        }

        static EngineerDef NewEngineer(CareerSave c, System.Random rng)
        {
            var p = new EngineerDef
            {
                id = NewId(c, "e"), name = NewName(c, rng), nationality = Nats[rng.Next(Nats.Length)], age = rng.Next(25, 32),
                stats = Spread(rng.Next(58, 71), 6, rng.Next(6), rng, 6), teamId = "", buyoutFee = -1, contractSeasons = rng.Next(1, 4)
            };
            p.salaryPerRace = Rules.MarketSalary(p);
            p.signingBonus = Rules.RoundTo(p.salaryPerRace * 4, 10000);
            c.world.engineers.Add(p);
            return p;
        }

        static CoordinatorDef NewCoordinator(CareerSave c, System.Random rng)
        {
            var p = new CoordinatorDef
            {
                id = NewId(c, "c"), name = NewName(c, rng), nationality = Nats[rng.Next(Nats.Length)], age = rng.Next(30, 39),
                stats = Spread(rng.Next(60, 71), 3, rng.Next(3), rng, 4), teamId = "", buyoutFee = -1, contractSeasons = rng.Next(1, 4)
            };
            p.salaryPerRace = Rules.MarketSalary(p);
            p.signingBonus = Rules.RoundTo(p.salaryPerRace * 4, 10000);
            c.world.coordinators.Add(p);
            return p;
        }

        // ================================================================== helpers

        static double Gauss(System.Random r)
        {
            double u1 = Math.Max(1e-9, r.NextDouble()), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
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
