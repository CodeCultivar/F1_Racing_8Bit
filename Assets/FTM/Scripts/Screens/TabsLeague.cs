using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    // =====================================================================
    public static class SponsorsTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            Ui.Label(root, "SPONSOR OVERVIEW", Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 700, 60);
            Ui.Label(root, "ACTIVE SPONSORS  " + c.sponsors.Count(s => s.active) + " / 6", Theme.PanelTitle, true, Theme.TextPrimary, 0, 0, 1872, 60, TextAnchor.MiddleCenter);
            Ui.Label(root, "New offers after every race", Theme.Small, true, Theme.TextPrimary, 1272, 0, 600, 60, TextAnchor.MiddleRight);
            float w = (1872 - 40) / 3f;
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 3) * (w + 20), y = 80 + (i / 3) * (390 + 20);
                Slot(root, c, i, x, y, w, 390);
            }
        }

        /// <summary>
        /// The offers visible for a slot right now: a random 1-3 (config) picked from that slot's pool.
        /// The pick is seeded by career + season + round + slot, so it stays the same until the next race
        /// (reopening the slot or reloading never re-rolls it).
        /// </summary>
        public static List<SponsorOfferDef> OffersFor(CareerSave c, int slotIndex)
        {
            var cfg = ConfigLoader.Config;
            var pool = cfg.sponsorOffers
                .Where(o => !string.IsNullOrEmpty(o.id))
                .Where(o => o.slots == null || o.slots.Length == 0 || o.slots.Contains(slotIndex + 1))
                .OrderBy(o => o.id, StringComparer.Ordinal)
                .ToList();
            unchecked
            {
                int seed = (int)c.createdTicks ^ (int)(c.createdTicks >> 32);
                seed = seed * 31 + c.season * 1009 + c.nextRound * 97 + (slotIndex + 1) * 7919;
                var rng = new System.Random(seed);
                for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
                int lo = Mathf.Max(0, cfg.sponsorOffersMin), hi = Mathf.Max(lo, cfg.sponsorOffersMax);
                int count = rng.Next(lo, hi + 1);
                var activeIds = new HashSet<string>(c.sponsors.Where(s => s.active).Select(s => s.offerId));
                return pool.Take(count).Where(o => !activeIds.Contains(o.id)).ToList();
            }
        }

        static void Slot(Transform root, CareerSave c, int i, float x, float y, float w, float h)
        {
            var s = c.sponsors[i];
            var offers = OffersFor(c, i);
            var b = Ui.Button(root, "", BtnStyle.Dark, x, y, w, h, () => Open(c, i));
            var t = b.transform;
            Ui.Label(t, "SLOT " + (i + 1), Theme.PanelTitle, true, Theme.TextPrimary, 20, 18, w - 40, 30);
            if (!s.active)
            {
                Ui.Label(t, "+", 64, false, Theme.TextSecondary, 0, h / 2 - 70, w, 90, TextAnchor.MiddleCenter, true);
                Ui.Label(t, offers.Count == 0 ? "No Offers" : offers.Count + (offers.Count == 1 ? " OFFER" : " OFFERS"), Theme.Body, true, offers.Count == 0 ? Theme.TextSecondary : Theme.Yellow, 0, h / 2 + 40, w, 30, TextAnchor.MiddleCenter);
            }
            else
            {
                Color sc = Theme.Hex(string.IsNullOrEmpty(s.color) ? "#C7D0D8" : s.color);
                Ui.Icon(t, PixelArt.Logo((s.offerId.GetHashCode() & 0x7fffffff), sc), w / 2 - 40, 64, 80, 80);
                Ui.Ellipsis(t, s.name, Theme.ScreenTitle, true, Theme.TextPrimary, 20, 156, w - 40, 40, TextAnchor.MiddleCenter);
                Ui.Label(t, "GUARANTEED  " + LobbyKit.PerRace(s.guaranteedPerRace), Theme.Body, true, Theme.Green, 20, 212, w - 40, 30, TextAnchor.MiddleCenter);
                if (s.bonusAmount > 0)
                    Ui.Ellipsis(t, "BONUS  " + Theme.Money(s.bonusAmount) + "  -  " + s.bonusCondition, Theme.Small, true, Theme.Yellow, 20, 246, w - 40, 26, TextAnchor.MiddleCenter);
                Ui.Label(t, s.racesRemaining + (s.racesRemaining == 1 ? " RACE REMAINING" : " RACES REMAINING"), Theme.Small, false, Theme.TextSecondary, 20, 280, w - 40, 26, TextAnchor.MiddleCenter);
            }
        }

        static void Open(CareerSave c, int i)
        {
            var s = c.sponsors[i];
            var offers = OffersFor(c, i);
            if (s.active) { Contract(c, i, offers); return; }
            if (offers.Count == 0) { GameApp.I.Message("SLOT " + (i + 1), "No offers available"); return; }
            OfferList(c, i, offers, false);
        }

        static void Contract(CareerSave c, int i, List<SponsorOfferDef> offers)
        {
            var s = c.sponsors[i];
            bool canReplace = offers.Count > 0 && s.terminationFee >= 0;
            var w = GameApp.I.OpenWindow(s.name.ToUpperInvariant(), 600, 4 * 44 + 30, canReplace ? "REPLACE SPONSOR" : null, win => { win.Close(); OfferList(c, i, offers, true); }, "CLOSE");
            float y = 0, bw = w.bodyWidth;
            LobbyKit.Line(w.body, ref y, bw, "Guaranteed", LobbyKit.PerRace(s.guaranteedPerRace), Theme.Green);
            LobbyKit.Line(w.body, ref y, bw, "Bonus", s.bonusAmount > 0 ? Theme.Money(s.bonusAmount) + " - " + s.bonusCondition : "None", Theme.Yellow);
            LobbyKit.Line(w.body, ref y, bw, "Races remaining", s.racesRemaining.ToString(), Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Termination fee", s.terminationFee < 0 ? Theme.NotConfigured : Theme.Money(s.terminationFee), Theme.Red);
            if (offers.Count > 0 && s.terminationFee < 0)
                Ui.Label(w.body, "Replacing this sponsor needs a termination fee in GameConfig.json.", Theme.Small, false, Theme.TextSecondary, 0, y, bw, 26);
        }

        static void OfferList(CareerSave c, int i, List<SponsorOfferDef> offers, bool replacing)
        {
            float rowH = 96;
            var w = GameApp.I.OpenWindow("SLOT " + (i + 1) + " OFFERS", 900, offers.Count * (rowH + 8), null, null, "CANCEL");
            float y = 0, bw = w.bodyWidth;
            foreach (var o in offers)
            {
                var offer = o;
                Ui.Box(w.body, Theme.RowA, 0, y, bw, rowH);
                Ui.Ellipsis(w.body, o.name, Theme.PanelTitle, true, Theme.TextPrimary, 16, y + 8, bw - 240, 32);
                Ui.Label(w.body, "Guaranteed " + (o.guaranteedPerRace < 0 ? Theme.NotConfigured : LobbyKit.PerRace(o.guaranteedPerRace)) + "    •    " + (o.durationRaces < 0 ? "Duration " + Theme.NotConfigured.ToLowerInvariant() : o.durationRaces + " races"), Theme.Small, true, Theme.Green, 16, y + 42, bw - 240, 22);
                Ui.Ellipsis(w.body, o.bonusAmount > 0 ? "Bonus " + Theme.Money(o.bonusAmount) + " - " + o.bonusCondition : "No bonus", Theme.Small, false, Theme.Yellow, 16, y + 64, bw - 240, 22);
                Ui.Button(w.body, "VIEW TERMS", BtnStyle.Red, bw - 16 - 200, y + 24, 200, 48, () => Terms(c, i, offer, replacing), 16);
                y += rowH + 8;
            }
        }

        static void Terms(CareerSave c, int i, SponsorOfferDef o, bool replacing)
        {
            var cur = c.sponsors[i];
            long fee = replacing ? cur.terminationFee : 0;
            int lines = 5 + (replacing ? 2 : 0);
            var w = GameApp.I.OpenWindow(o.name.ToUpperInvariant(), 600, lines * 44, "ACCEPT", win =>
            {
                GameApp.I.Commit(cs =>
                {
                    cs.balance -= fee;
                    cs.sponsors[i] = new SponsorState
                    {
                        active = true, offerId = o.id, name = o.name, color = o.color, guaranteedPerRace = o.guaranteedPerRace,
                        bonusAmount = o.bonusAmount, bonusCondition = o.bonusCondition, racesRemaining = o.durationRaces, terminationFee = o.terminationFee
                    };
                }, () => { GameApp.I.CloseAllWindows(); LobbyScreen.Refresh(); });
            });
            float y = 0, bw = w.bodyWidth;
            if (replacing)
            {
                LobbyKit.Line(w.body, ref y, bw, "Replaces", cur.name, Theme.Yellow);
                LobbyKit.Line(w.body, ref y, bw, "Termination fee", Theme.Money(fee), Theme.Red);
            }
            LobbyKit.Line(w.body, ref y, bw, "Guaranteed", o.guaranteedPerRace < 0 ? Theme.NotConfigured : LobbyKit.PerRace(o.guaranteedPerRace), Theme.Green);
            LobbyKit.Line(w.body, ref y, bw, "Bonus", o.bonusAmount > 0 ? Theme.Money(o.bonusAmount) : "None", Theme.Yellow);
            LobbyKit.Line(w.body, ref y, bw, "To earn bonus", string.IsNullOrEmpty(o.bonusCondition) ? "--" : o.bonusCondition, Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Deal length", o.durationRaces < 0 ? Theme.NotConfigured : o.durationRaces + " races", Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Balance afterward", Theme.Money(c.balance - fee), Theme.TextPrimary);
            if (o.guaranteedPerRace < 0 || o.durationRaces < 0) w.Block("This offer's terms are " + Theme.NotConfigured.ToLowerInvariant());
            else if (fee > c.balance) w.Block("Not enough money for the termination fee");
        }
    }

    // =====================================================================
    public static class TransfersTab
    {
        public static void Build(Transform root, CareerSave c, LobbyScreen lobby)
        {
            string[] cats = { "DRIVERS", "ENGINEERS", "COORDINATORS" };
            float bw = (1872 - 24) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var k = (PersonKind)i;
                var b = Ui.Button(root, cats[i], BtnStyle.Dark, i * (bw + 12), 0, bw, 48, () => { lobby.transferKind = k; LobbyScreen.Refresh(); });
                b.SetActive(lobby.transferKind == k);
            }
            var people = People.Pool(lobby.transferKind).Where(p => !string.IsNullOrEmpty(p.id) && !c.takenPeople.Contains(p.id) && !Rules.OnPlayerTeam(c, p.id)).ToList();
            People.BuildTable(root, 0, 68, 1872, 880 - 68, lobby.transferKind, people, c, p => Profile(c, p, lobby.transferKind));
        }

        class Spot { public int index; public string label; public PersonDef current; }

        static List<Spot> Spots(CareerSave c, PersonKind k)
        {
            var list = new List<Spot>();
            if (k == PersonKind.Driver) for (int i = 0; i < 2; i++) list.Add(new Spot { index = i, label = "DRIVER " + (i + 1), current = c.drivers[i].filled ? c.drivers[i].def : null });
            else if (k == PersonKind.Engineer) for (int i = 0; i < 3; i++) list.Add(new Spot { index = i, label = "ENGINEER " + (i + 1), current = c.engineers[i].filled ? c.engineers[i].def : null });
            else list.Add(new Spot { index = 0, label = "COORDINATOR", current = c.coordinator.filled ? c.coordinator.def : null });
            return list;
        }

        static void Profile(CareerSave c, PersonDef p, PersonKind k)
        {
            var spots = Spots(c, k);
            var vacant = spots.FirstOrDefault(s => s.current == null);
            int chosen = vacant != null ? vacant.index : -1;
            float bodyH = People.ProfileHeight(p) + 120;
            Window w = null;
            w = GameApp.I.OpenWindow("PROFILE", 900, bodyH, "SIGN", win => { win.Unblock(); Sign(c, p, k, chosen); });
            float y = People.BuildProfile(w.body, w.bodyWidth, p, c, Theme.Hex(c.teamColor)) + 16;
            Ui.Label(w.body, "SPOT ON YOUR TEAM", Theme.Small, true, Theme.TextSecondary, 0, y, 400, 24);
            y += 30;
            float sw = (w.bodyWidth - (spots.Count - 1) * 8) / spots.Count;
            var buttons = new List<FtmButton>();
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                string label = s.label + (s.current == null ? " (EMPTY)" : " - REPLACE " + Short(s.current.name));
                FtmButton b = null;
                b = Ui.Button(w.body, label, BtnStyle.Dark, i * (sw + 8), y, sw, 48, () =>
                {
                    chosen = s.index;
                    foreach (var o in buttons) o.SetActive(o == b);
                }, 14);
                b.redBorderWhenActive = true;
                b.SetActive(s.index == chosen);
                buttons.Add(b);
            }
        }

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var parts = name.Split(' ');
            return parts[parts.Length - 1].ToUpperInvariant();
        }

        static void Sign(CareerSave c, PersonDef p, PersonKind k, int spotIndex)
        {
            if (spotIndex < 0) { GameApp.I.Message("CHOOSE A SPOT", "Choose which spot on your team this person will take."); return; }
            var spot = Spots(c, k)[k == PersonKind.Coordinator ? 0 : spotIndex];
            bool fromTeam = !Rules.IsFreeAgent(c, p);
            long bonus = p.signingBonus;
            long buyout = fromTeam ? p.buyoutFee : 0;
            long release = spot.current != null ? ConfigLoader.Config.releaseFee : 0;

            string blocked = null;
            if (bonus < 0) blocked = "Signing bonus " + Theme.NotConfigured.ToLowerInvariant();
            else if (buyout < 0) blocked = "Buyout fee " + Theme.NotConfigured.ToLowerInvariant();
            else if (release < 0) blocked = "Release fee " + Theme.NotConfigured.ToLowerInvariant();
            else if (p.salaryPerRace < 0) blocked = "Salary " + Theme.NotConfigured.ToLowerInvariant();
            long total = Math.Max(0, bonus) + Math.Max(0, buyout) + Math.Max(0, release);

            int lines = 7 + (fromTeam ? 1 : 0) + (spot.current != null ? 2 : 0);
            var w = GameApp.I.OpenWindow("SIGN " + p.name.ToUpperInvariant(), 600, lines * 44, "CONFIRM SIGNING", win =>
            {
                GameApp.I.Commit(cs => Apply(cs, p, k, spot.index, total), () => { GameApp.I.CloseAllWindows(); LobbyScreen.Refresh(); });
            });
            float y = 0, bw = w.bodyWidth;
            LobbyKit.Line(w.body, ref y, bw, "Spot", spot.label, Theme.TextPrimary);
            if (spot.current != null)
            {
                LobbyKit.Line(w.body, ref y, bw, "Replacing", spot.current.name, Theme.Yellow);
                LobbyKit.Line(w.body, ref y, bw, "Release fee", Rules.FmtMoney(release), Theme.Red);
            }
            LobbyKit.Line(w.body, ref y, bw, "Signing bonus", Rules.FmtMoney(bonus), Theme.Red);
            if (fromTeam) LobbyKit.Line(w.body, ref y, bw, "Buyout fee (" + Rules.TeamNameOf(c, p) + ")", Rules.FmtMoney(buyout), Theme.Red);
            LobbyKit.Line(w.body, ref y, bw, "Salary", p.salaryPerRace < 0 ? Theme.NotConfigured : LobbyKit.PerRace(p.salaryPerRace), Theme.Red);
            LobbyKit.Line(w.body, ref y, bw, "Contract", People.Contract(p.contractSeasons), Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Total signing cost", blocked == null ? Theme.Money(total) : Theme.NotConfigured, Theme.Red);
            LobbyKit.Line(w.body, ref y, bw, "Current balance", Theme.Money(c.balance), Theme.TextPrimary);
            LobbyKit.Line(w.body, ref y, bw, "Balance afterward", blocked == null ? Theme.Money(c.balance - total) : "--", c.balance - total < 0 ? Theme.Red : Theme.Green);
            if (blocked != null) w.Block(blocked);
            else if (total > c.balance) w.Block("Not enough money to sign");
        }

        static void Apply(CareerSave cs, PersonDef p, PersonKind k, int index, long total)
        {
            cs.balance -= total;
            string releasedId = null;
            PersonDef releasedDef = null;
            if (k == PersonKind.Driver && cs.drivers[index].filled) releasedDef = cs.drivers[index].def;
            else if (k == PersonKind.Engineer && cs.engineers[index].filled) releasedDef = cs.engineers[index].def;
            else if (k == PersonKind.Coordinator && cs.coordinator.filled) releasedDef = cs.coordinator.def;
            if (k == PersonKind.Driver)
            {
                var src = (DriverDef)p;
                if (cs.drivers[index].filled) releasedId = cs.drivers[index].def.id;
                cs.drivers[index] = new DriverState { filled = true, def = Rules.Clone(src) };
            }
            else if (k == PersonKind.Engineer)
            {
                if (cs.engineers[index].filled) releasedId = cs.engineers[index].def.id;
                cs.engineers[index] = new EngineerState { filled = true, def = Rules.Clone((EngineerDef)p) };
            }
            else
            {
                if (cs.coordinator.filled) releasedId = cs.coordinator.def.id;
                cs.coordinator = new CoordinatorState { filled = true, def = Rules.Clone((CoordinatorDef)p) };
            }
            if (!cs.takenPeople.Contains(p.id)) cs.takenPeople.Add(p.id);
            if (releasedId != null) cs.takenPeople.Remove(releasedId); // released person returns to the pool as a free agent
            var w = cs.world;
            if (w != null && w.active)
            {
                // the team you signed them from hires a replacement straight away
                var from = w.teams.FirstOrDefault(t => t.drivers.Contains(p.id) || t.engineers.Contains(p.id) || t.coordinator == p.id);
                if (from != null)
                {
                    from.drivers.Remove(p.id); from.engineers.Remove(p.id); if (from.coordinator == p.id) from.coordinator = "";
                    SeasonRollover.FillSeats(cs, w, from, new System.Random(SeasonRollover.Hash(p.id) ^ cs.season));
                }
                if (releasedDef != null)
                {
                    var back = Rules.ClonePerson(releasedDef);   // keeps the ratings they earned with you
                    back.teamId = ""; back.buyoutFee = -1;
                    back.salaryPerRace = Rules.MarketSalary(back);
                    back.signingBonus = Rules.RoundTo(back.salaryPerRace * 6, 10000);
                    if (back.contractSeasons <= 0) back.contractSeasons = 2;
                    SeasonRollover.PutInWorld(w, back);
                }
            }
        }
    }

    // =====================================================================
    public static class StandingsTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            var cfg = ConfigLoader.Config;
            Ui.Label(root, string.IsNullOrEmpty(cfg.championshipName) ? "Championship " + Theme.NotConfigured.ToLowerInvariant() : cfg.championshipName.ToUpperInvariant(), Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1872, 60);
            bool has = Rules.HasResults(c);
            List(Ui.Panel(root, 0, 80, 926, 800, "Team Standings"), Rules.TeamStandings(c), has);
            List(Ui.Panel(root, 946, 80, 926, 800, "Driver Standings"), Rules.DriverStandings(c), has);
        }

        static void List(Transform p, List<Rules.StandingRow> rows, bool has)
        {
            var t = Table.Create(p, 20, Ui.UnderTitle, 886, 800 - Ui.UnderTitle - 20, new[] { "POS", "NAME", "POINTS" }, new[] { 10f, 65f, 25f }, 48);
            if (rows.Count == 0) { t.Message("Teams " + Theme.NotConfigured.ToLowerInvariant()); return; }
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = t.AddRow(r.mine ? Theme.Darken(Theme.Red, 0.55f) : (Color?)null);
                t.Cell(row, 0, has ? (i + 1).ToString() : "-", Theme.TextPrimary, true);
                Ui.Box(row, r.color, t.colX[1] + 6, 10, 6, t.rowH - 20);
                Ui.Ellipsis(row, r.name, Theme.Body, r.mine, Theme.TextPrimary, t.colX[1] + 22, 0, t.colW[1] - 30, t.rowH);
                t.Cell(row, 2, r.points.ToString(), Theme.TextPrimary, true, TextAnchor.MiddleRight);
            }
        }
    }

    // =====================================================================
    public static class HistoryTab
    {
        public static void Build(Transform root, CareerSave c, LobbyScreen lobby)
        {
            var cfg = ConfigLoader.Config;
            if (lobby.historySeason < 1 || lobby.historySeason > c.season) lobby.historySeason = c.season;
            int season = lobby.historySeason;
            Ui.Label(root, string.IsNullOrEmpty(cfg.championshipName) ? "HISTORY" : cfg.championshipName.ToUpperInvariant(), Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1200, 60);
            var prev = Ui.Button(root, "<", BtnStyle.Dark, 1872 - 48 - 200 - 48 - 16, 6, 48, 48, () => { lobby.historySeason--; LobbyScreen.Refresh(); }, 20);
            prev.SetEnabled(season > 1);
            Ui.Label(root, "SEASON " + season, Theme.PanelTitle, true, Theme.TextPrimary, 1872 - 48 - 200 - 8, 6, 200, 48, TextAnchor.MiddleCenter);
            var next = Ui.Button(root, ">", BtnStyle.Dark, 1872 - 48, 6, 48, 48, () => { lobby.historySeason++; LobbyScreen.Refresh(); }, 20);
            next.SetEnabled(season < c.season);

            var srec = Rules.Season(c, season);
            string title = "Season Results" + (srec != null && srec.teamPosition > 0 ? "  •  Final position P" + srec.teamPosition + (srec.teamChampion ? "  •  CHAMPIONS" : "") : "");
            Results(Ui.Panel(root, 0, 80, 1228, 800, title), c, season);
            Career(Ui.Panel(root, 1248, 80, 624, 800, "Team Career"), c, 624, 800);
        }

        static void Results(Transform p, CareerSave c, int season)
        {
            var t = Table.Create(p, 20, Ui.UnderTitle, 1188, 800 - Ui.UnderTitle - 20, new[] { "ROUND", "GRAND PRIX", "DRIVER 1", "DRIVER 2", "TEAM POINTS" }, new[] { 10f, 42f, 15f, 15f, 18f });
            var rec = Rules.Season(c, season);
            if (rec == null || rec.results.Count == 0) { t.Message("No completed results for this season"); return; }
            foreach (var r in rec.results.OrderBy(q => q.round))
            {
                var row = t.AddRow();
                t.Cell(row, 0, r.round.ToString());
                t.Cell(row, 1, r.grandPrix, Theme.TextPrimary, true);
                t.Cell(row, 2, r.driver1Pos > 0 ? "P" + r.driver1Pos : "DNF");
                t.Cell(row, 3, r.driver2Pos > 0 ? "P" + r.driver2Pos : "DNF");
                t.Cell(row, 4, r.teamPoints.ToString(), Theme.Green, true, TextAnchor.MiddleRight);
            }
        }

        static void Career(Transform p, CareerSave c, float w, float h)
        {
            var mine = new HashSet<string>();
            foreach (var d in c.drivers) if (d.filled) mine.Add(d.def.id);
            int races = 0, poles = 0, wins = 0, podiums = 0;
            string myTeam = Rules.PlayerTeamId(c);
            foreach (var s in c.seasons)
                foreach (var r in s.results)
                {
                    races++;
                    var ours = r.classification.Where(x => x.teamId == myTeam).ToList();
                    if (ours.Any(x => x.pole)) poles++;
                    if (ours.Any(x => x.position == 1)) wins++;
                    podiums += ours.Count(x => x.position >= 1 && x.position <= 3);
                }
            int teamTitles = c.seasons.Count(s => s.teamChampion);
            int driverTitles = c.seasons.Count(s => s.driverChampion);
            var ts = TimeSpan.FromSeconds(c.playtimeSeconds);
            string play = (int)ts.TotalHours + "h " + ts.Minutes.ToString("00") + "m";

            float y = Ui.UnderTitle; int i = 0;
            Action<string, string> row = (l, v) => { LobbyKit.ValueRow(p, 20, y, w - 40, l, v, Theme.TextPrimary, i++); y += 52; };
            row("Races", races.ToString());
            row("Pole Positions", poles.ToString());
            row("Wins", wins.ToString());
            row("Podiums", podiums.ToString());
            row("Team Championships", teamTitles.ToString());
            row("Driver Championships", driverTitles.ToString());
            row("Playtime", play);

            var del = Ui.Button(p, "DELETE TEAM", BtnStyle.Red, 20, h - 20 - 48, w - 40, 48, () => Delete(c));
            if (c.slot < 1 || c.slot > SaveSystem.Slots) { del.SetEnabled(false); }
        }

        static void Delete(CareerSave c)
        {
            var w = GameApp.I.OpenWindow("DELETE TEAM", 600, 120, "DELETE TEAM", win =>
            {
                SaveSystem.Delete(c.slot);
                GameApp.I.Career = null;
                GameApp.I.Show(new CareerSlotsScreen());
            });
            Ui.Label(w.body, "Are you sure? This permanently deletes the career in SLOT " + c.slot + ":\n\n" + c.teamName + "  -  Season " + c.season, Theme.Body, false, Theme.TextPrimary, 0, 0, w.bodyWidth, 120, TextAnchor.UpperLeft);
        }
    }

    // =====================================================================
    public static class CalendarTab
    {
        public static void Build(Transform root, CareerSave c)
        {
            Ui.Label(root, "CHAMPIONSHIP CALENDAR", Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1872, 60);
            var panel = Ui.Panel(root, 0, 80, 1872, 800);
            var t = Table.Create(panel, 20, 20, 1832, 760, new[] { "ROUND", "GRAND PRIX", "EVENT", "STATUS", "WINNER" }, new[] { 8f, 32f, 25f, 15f, 20f });
            var cal = ConfigLoader.Config.calendar;
            if (cal.Count == 0) { t.Message("Calendar " + Theme.NotConfigured.ToLowerInvariant()); return; }
            var rec = Rules.Season(c, c.season);
            for (int i = 0; i < cal.Count; i++)
            {
                int round = i + 1;
                var e = cal[i];
                var result = rec != null ? rec.results.FirstOrDefault(r => r.round == round) : null;
                bool isNext = round == c.nextRound && result == null;
                var row = t.AddRow(isNext ? Theme.Yellow : (Color?)null);
                Color tc = isNext ? Theme.Dark : Theme.TextPrimary;
                t.Cell(row, 0, round.ToString(), tc, true);
                t.Cell(row, 1, string.IsNullOrEmpty(e.grandPrix) ? "--" : e.grandPrix, tc, true);
                t.Cell(row, 2, (e.laps < 0 ? "-- LAPS" : e.laps + " LAPS") + " • WEEKEND " + round, tc);
                t.Cell(row, 3, result != null ? "Completed" : isNext ? "Next" : "Pending", tc, isNext);
                t.Cell(row, 4, result != null ? result.winner : "--", tc);
            }
        }
    }
}
