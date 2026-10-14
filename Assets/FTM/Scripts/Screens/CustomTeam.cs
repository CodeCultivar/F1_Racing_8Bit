using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>Identity -> Drivers -> Engineers -> Coordinator -> Review. Nothing is charged until Create Team.</summary>
    public class CustomTeamScreen : IScreen
    {
        class Draft
        {
            public int slot;
            public int nameIndex = -1;
            public int logo = -1;
            public string[] drivers = new string[2];
            public string[] engineers = new string[3];
            public string coordinator;
            public int step;
        }

        static Draft draft; // kept while you move between steps and screens ("Back keeps your selections")
        static readonly string[] Steps = { "IDENTITY", "DRIVERS", "ENGINEERS", "COORDINATOR", "REVIEW" };

        public CustomTeamScreen(int slot)
        {
            if (draft == null || draft.slot != slot) draft = new Draft { slot = slot };
        }

        GameConfig Cfg { get { return ConfigLoader.Config; } }
        Color TeamColor { get { return Theme.Hex(string.IsNullOrEmpty(Cfg.customTeam.color) ? "#B8C7CE" : Cfg.customTeam.color); } }

        public void Build(RectTransform root)
        {
            Ui.FillBox(root, Theme.Background);
            Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80);
            Ui.Button(root, "BACK", BtnStyle.Dark, 24, 16, 160, 48, Back);
            Ui.Label(root, "BUILD YOUR TEAM", Theme.ScreenTitle, true, Theme.TextPrimary, 0, 0, 1920, 80, TextAnchor.MiddleCenter);

            float bw = (1872 - 4 * 12) / 5f;
            for (int i = 0; i < Steps.Length; i++)
            {
                int s = i;
                var b = Ui.Button(root, (i + 1) + ". " + Steps[i], BtnStyle.Dark, 24 + i * (bw + 12), 100, bw, 48, () => { draft.step = s; GameApp.I.RefreshCurrent(); });
                b.SetActive(draft.step == i);
            }

            float top = 168, h = 1080 - top - 24;
            var main = Ui.Panel(root, 24, top, 1228, h, Steps[draft.step]);
            switch (draft.step)
            {
                case 0: Identity(main, 1228, h); break;
                case 1: Personnel(main, 1228, h, PersonKind.Driver); break;
                case 2: Personnel(main, 1228, h, PersonKind.Engineer); break;
                case 3: Personnel(main, 1228, h, PersonKind.Coordinator); break;
                default: Review(main, 1228, h); break;
            }
            Summary(Ui.Panel(root, 1272, top, 624, h, "SUMMARY"), 624, h);
        }

        void Back()
        {
            if (draft.step > 0) { draft.step--; GameApp.I.RefreshCurrent(); }
            else GameApp.I.Show(new ChooseTeamScreen(draft.slot, true));
        }

        // ---------------- Steps ----------------

        void Identity(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle;
            Ui.Label(p, "TEAM NAME", Theme.Small, true, Theme.TextSecondary, 20, y, 400, 24); y += 32;
            var names = Cfg.customTeamNames;
            if (names.Count == 0)
            {
                Ui.Label(p, "Premade team names: " + Theme.NotConfigured, Theme.Body, false, Theme.Yellow, 20, y, w - 40, 30);
                y += 50;
            }
            else
            {
                float cw = (w - 40 - 3 * 12) / 4f;
                for (int i = 0; i < names.Count; i++)
                {
                    int idx = i;
                    var b = Ui.Button(p, names[i], BtnStyle.Dark, 20 + (i % 4) * (cw + 12), y + (i / 4) * 60, cw, 48, () => { draft.nameIndex = idx; GameApp.I.RefreshCurrent(); }, 16);
                    b.redBorderWhenActive = true; b.SetActive(draft.nameIndex == i);
                }
                y += ((names.Count + 3) / 4) * 60 + 20;
            }

            Ui.Label(p, "LOGO  (placeholder pixel art)", Theme.Small, true, Theme.TextSecondary, 20, y, 600, 24); y += 32;
            for (int i = 0; i < PixelArt.LogoCount; i++)
            {
                int idx = i;
                var b = Ui.Button(p, "", BtnStyle.Dark, 20 + (i % 6) * 104, y + (i / 6) * 104, 92, 92, () => { draft.logo = idx; GameApp.I.RefreshCurrent(); });
                b.redBorderWhenActive = true; b.SetActive(draft.logo == i);
                Ui.Icon(b.transform, PixelArt.Logo(i, TeamColor), 14, 14, 64, 64);
            }
            Ui.Button(p, "NEXT: DRIVERS", BtnStyle.Red, w - 20 - 260, h - 20 - 48, 260, 48, () => { draft.step = 1; GameApp.I.RefreshCurrent(); });
        }

        string[] SlotsFor(PersonKind k)
        {
            return k == PersonKind.Driver ? draft.drivers : k == PersonKind.Engineer ? draft.engineers : new[] { draft.coordinator };
        }

        bool Selected(string id)
        {
            return draft.drivers.Contains(id) || draft.engineers.Contains(id) || draft.coordinator == id;
        }

        void Personnel(Transform p, float w, float h, PersonKind kind)
        {
            float y = Ui.UnderTitle;
            int need = kind == PersonKind.Driver ? 2 : kind == PersonKind.Engineer ? 3 : 1;
            int have = SlotsFor(kind).Count(s => !string.IsNullOrEmpty(s));
            Ui.Label(p, "Choose " + need + " " + People.KindName(kind).ToLowerInvariant() + (need > 1 ? "s" : "") + ".  Selected " + have + " / " + need + ".  Fees are charged only when you create the team.",
                Theme.Small, false, Theme.TextSecondary, 20, y, w - 40, 24);
            y += 36;
            var pool = People.Pool(kind).Where(x => !string.IsNullOrEmpty(x.id)).ToList();
            People.BuildTable(p, 20, y, w - 40, h - y - 20 - 48 - 20, kind, pool, null, person => Profile(person, kind), person => Selected(person.id));
            string nextLabel = kind == PersonKind.Coordinator ? "NEXT: REVIEW" : kind == PersonKind.Driver ? "NEXT: ENGINEERS" : "NEXT: COORDINATOR";
            Ui.Button(p, nextLabel, BtnStyle.Red, w - 20 - 260, h - 20 - 48, 260, 48, () => { draft.step++; GameApp.I.RefreshCurrent(); });
        }

        static long FeeFor(PersonDef p)
        {
            if (p.signingBonus < 0) return -1;
            if (!string.IsNullOrEmpty(p.teamId))
                return p.buyoutFee < 0 ? -1 : p.signingBonus + p.buyoutFee;
            return p.signingBonus;
        }

        void Profile(PersonDef person, PersonKind kind)
        {
            bool already = Selected(person.id);
            var slots = SlotsFor(kind);
            bool full = slots.All(s => !string.IsNullOrEmpty(s));
            long fee = FeeFor(person);
            float bodyH = People.ProfileHeight(person) + 80;
            var w = GameApp.I.OpenWindow("PROFILE", 900, bodyH, already ? "REMOVE" : "SELECT", win =>
            {
                Toggle(person, kind);
                win.Close();
                GameApp.I.RefreshCurrent();
            });
            float y = People.BuildProfile(w.body, w.bodyWidth, person, null, TeamColor);
            string feeText = "SIGNING COST  " + (fee < 0 ? Theme.NotConfigured : Theme.Money(fee));
            if (!string.IsNullOrEmpty(person.teamId)) feeText += "   (includes buyout from " + Rules.TeamNameOf(null, person) + ")";
            Ui.Label(w.body, feeText, Theme.Body, true, fee < 0 ? Theme.TextSecondary : Theme.Red, 0, y + 16, w.bodyWidth, 30);
            if (!already)
            {
                if (fee < 0) w.Block("Signing cost " + Theme.NotConfigured.ToLowerInvariant());
                else if (person.salaryPerRace < 0) w.Block("Salary " + Theme.NotConfigured.ToLowerInvariant());
                else if (full) w.Block("All " + People.KindName(kind).ToLowerInvariant() + " slots are filled. Remove someone first.");
            }
        }

        void Toggle(PersonDef p, PersonKind kind)
        {
            if (kind == PersonKind.Coordinator) { draft.coordinator = draft.coordinator == p.id ? null : p.id; return; }
            var arr = kind == PersonKind.Driver ? draft.drivers : draft.engineers;
            int at = Array.IndexOf(arr, p.id);
            if (at >= 0) { arr[at] = null; return; }
            for (int i = 0; i < arr.Length; i++) if (string.IsNullOrEmpty(arr[i])) { arr[i] = p.id; return; }
        }

        // ---------------- Review / summary ----------------

        List<PersonDef> Chosen()
        {
            var list = new List<PersonDef>();
            foreach (var id in draft.drivers) { var d = Rules.FindDriver(id); if (d != null) list.Add(d); }
            foreach (var id in draft.engineers) { var e = Rules.FindEngineer(id); if (e != null) list.Add(e); }
            var c = Rules.FindCoordinator(draft.coordinator); if (c != null) list.Add(c);
            return list;
        }

        long TotalFees(out bool known)
        {
            known = true; long t = 0;
            foreach (var p in Chosen()) { long f = FeeFor(p); if (f < 0) known = false; else t += f; }
            return t;
        }

        string TeamName { get { return draft.nameIndex >= 0 && draft.nameIndex < Cfg.customTeamNames.Count ? Cfg.customTeamNames[draft.nameIndex] : null; } }

        string BlockReason()
        {
            if (TeamName == null) return "Choose a team name";
            if (draft.logo < 0) return "Choose a logo";
            if (Chosen().Count < 6) return "Select all 6 people (" + Chosen().Count + " / 6)";
            bool known; long fees = TotalFees(out known);
            if (!known) return "A signing cost is not configured";
            if (fees > Cfg.customTeam.startingBudget) return "Not enough money for the signing fees";
            return null;
        }

        void Review(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle;
            Color tc = TeamColor;
            if (draft.logo >= 0) Ui.Icon(p, PixelArt.Logo(draft.logo, tc), 20, y, 64, 64);
            Ui.Label(p, TeamName ?? "No name chosen", Theme.ScreenTitle, true, TeamName == null ? Theme.TextSecondary : Theme.TextPrimary, 100, y, 700, 64);
            Ui.Icon(p, PixelArt.Car(tc), w - 20 - 192, y - 10, 192, 90);
            y += 100;
            string[] labels = { "DRIVER 1", "DRIVER 2", "ENGINEER 1", "ENGINEER 2", "ENGINEER 3", "COORDINATOR" };
            string[] ids = { draft.drivers[0], draft.drivers[1], draft.engineers[0], draft.engineers[1], draft.engineers[2], draft.coordinator };
            for (int i = 0; i < 6; i++)
            {
                PersonDef person = i < 2 ? (PersonDef)Rules.FindDriver(ids[i]) : i < 5 ? (PersonDef)Rules.FindEngineer(ids[i]) : Rules.FindCoordinator(ids[i]);
                Ui.Box(p, i % 2 == 0 ? Theme.RowA : Theme.RowB, 20, y, w - 40, 56);
                Ui.Label(p, labels[i], Theme.Small, true, Theme.TextSecondary, 36, y, 180, 56);
                if (person == null) Ui.Label(p, "Not Filled", Theme.Body, false, Theme.Yellow, 220, y, 500, 56);
                else
                {
                    Ui.Ellipsis(p, People.NameLine(person), Theme.Body, true, Theme.TextPrimary, 220, y, 480, 56);
                    Ui.Label(p, "OVR " + Rules.Fmt(Rules.Overall(People.StatsOf(person))), Theme.Body, false, Theme.TextSecondary, 720, y, 200, 56);
                    Ui.Label(p, person.salaryPerRace < 0 ? Theme.NotConfigured : Theme.Money(person.salaryPerRace) + " / RACE", Theme.Body, true, Theme.Red, 900, y, w - 40 - 900, 56, TextAnchor.MiddleRight);
                }
                y += 60;
            }
            string reason = BlockReason();
            var create = Ui.Button(p, "CREATE TEAM", BtnStyle.Red, w - 20 - 300, h - 20 - 48, 300, 48, Create);
            if (reason != null)
            {
                create.SetEnabled(false);
                Ui.Label(p, reason, Theme.Small, true, Theme.Yellow, 20, h - 20 - 48 - 30, w - 40, 26, TextAnchor.MiddleRight);
            }
        }

        void Create()
        {
            if (BlockReason() != null) return;
            bool known; long fees = TotalFees(out known);
            var w = GameApp.I.OpenWindow("CREATE TEAM", 600, 200, "CREATE TEAM", win =>
            {
                var d = draft.drivers.Select(Rules.FindDriver).ToList();
                var e = draft.engineers.Select(Rules.FindEngineer).ToList();
                var c = CareerFactory.Custom(draft.slot, TeamName, draft.logo, d, e, Rules.FindCoordinator(draft.coordinator), fees);
                var keep = draft;
                draft = null;
                if (!CareerFactory.CreateAndOpen(c, win)) draft = keep;
            });
            float bw = w.bodyWidth;
            long start = Cfg.customTeam.startingBudget;
            Line(w.body, 0, bw, "Starting cash", Theme.Money(start), Theme.Green);
            Line(w.body, 44, bw, "Signing fees (charged once)", "-" + Theme.Money(fees), Theme.Red);
            Line(w.body, 88, bw, "Balance afterward", Theme.Money(start - fees), Theme.TextPrimary);
            Ui.Label(w.body, "Your team will be saved to slot " + draft.slot + " and the Dashboard will open.", Theme.Small, false, Theme.TextSecondary, 0, 140, bw, 40);
        }

        static void Line(Transform t, float y, float w, string label, string value, Color c)
        {
            Ui.Label(t, label, Theme.Body, false, Theme.TextSecondary, 0, y, w, 40);
            Ui.Label(t, value, Theme.Body, true, c, 0, y, w, 40, TextAnchor.MiddleRight);
        }

        void Summary(Transform p, float w, float h)
        {
            float y = Ui.UnderTitle;
            Action<string, string, Color> row = (label, value, c) =>
            {
                Ui.Box(p, Theme.RowA, 20, y, w - 40, 48);
                Ui.Label(p, label, Theme.Body, false, Theme.TextSecondary, 36, y, w - 72, 48);
                Ui.Ellipsis(p, value, Theme.Body, true, c, w * 0.45f, y, w * 0.55f - 36, 48, TextAnchor.MiddleRight);
                y += 52;
            };
            row("Name", TeamName ?? "Not chosen", TeamName == null ? Theme.Yellow : Theme.TextPrimary);
            row("Logo", draft.logo >= 0 ? "#" + (draft.logo + 1) : "Not chosen", draft.logo >= 0 ? Theme.TextPrimary : Theme.Yellow);
            row("Drivers", draft.drivers.Count(s => !string.IsNullOrEmpty(s)) + " / 2", Theme.TextPrimary);
            row("Engineers", draft.engineers.Count(s => !string.IsNullOrEmpty(s)) + " / 3", Theme.TextPrimary);
            row("Coordinator", (string.IsNullOrEmpty(draft.coordinator) ? 0 : 1) + " / 1", Theme.TextPrimary);
            y += 16;
            long payroll = Rules.Payroll(Chosen());
            bool known; long fees = TotalFees(out known);
            long start = Cfg.customTeam.startingBudget;
            row("Projected payroll", payroll < 0 ? Theme.NotConfigured : Theme.Money(payroll) + " / RACE", Theme.Red);
            row("Starting cash", Rules.FmtMoney(start), Theme.Green);
            row("Signing fees", known ? Theme.Money(fees) : Theme.NotConfigured, Theme.Red);
            row("Remaining money", known && start >= 0 ? Theme.Money(start - fees) : Theme.NotConfigured, known && start - fees < 0 ? Theme.Red : Theme.Green);
        }
    }
}
