using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    public static class CareerFactory
    {
        static CareerSave Blank(int slot)
        {
            var cfg = ConfigLoader.Config;
            var c = new CareerSave();
            c.slot = slot;
            c.rulesVersion = cfg.rulesVersion;
            c.createdTicks = c.lastPlayedTicks = DateTime.UtcNow.Ticks;
            c.seasons.Add(new SeasonRecord { season = 1 });
            c.difficulty = ChooseTeamScreen.Difficulty;
            return c;
        }

        public static CareerSave FromTeam(TeamDef t, int slot)
        {
            var c = Blank(slot);
            c.baseTeamId = t.id;
            c.teamName = t.name;
            c.teamColor = t.color;
            c.logo = t.logo;
            c.balance = t.startingBudget;
            c.carParts = (int[])t.carParts.Clone();
            for (int i = 0; i < 2; i++) { c.drivers[i].filled = true; c.drivers[i].def = Rules.Clone(Rules.FindDriver(t.drivers[i])); }
            for (int i = 0; i < 3; i++) { c.engineers[i].filled = true; c.engineers[i].def = Rules.Clone(Rules.FindEngineer(t.engineers[i])); }
            c.coordinator.filled = true; c.coordinator.def = Rules.Clone(Rules.FindCoordinator(t.coordinator));
            c.pitCrew = t.pitCrew >= 0 ? t.pitCrew : 50;
            c.takenPeople.AddRange(t.drivers); c.takenPeople.AddRange(t.engineers); c.takenPeople.Add(t.coordinator);
            return c;
        }

        public static CareerSave Custom(int slot, string name, int logo, List<DriverDef> d, List<EngineerDef> e, CoordinatorDef co, long fees)
        {
            var cfg = ConfigLoader.Config;
            var c = Blank(slot);
            c.baseTeamId = "custom";
            c.teamName = name;
            c.teamColor = string.IsNullOrEmpty(cfg.customTeam.color) ? "#B8C7CE" : cfg.customTeam.color;
            c.logo = logo;
            c.balance = cfg.customTeam.startingBudget - fees;   // signing fees applied once, here
            c.carParts = (int[])cfg.customTeam.carParts.Clone();
            c.pitCrew = cfg.customTeam.pitCrew >= 0 ? cfg.customTeam.pitCrew : 50;
            for (int i = 0; i < 2; i++) { c.drivers[i].filled = true; c.drivers[i].def = Rules.Clone(d[i]); c.takenPeople.Add(d[i].id); }
            for (int i = 0; i < 3; i++) { c.engineers[i].filled = true; c.engineers[i].def = Rules.Clone(e[i]); c.takenPeople.Add(e[i].id); }
            c.coordinator.filled = true; c.coordinator.def = Rules.Clone(co); c.takenPeople.Add(co.id);
            return c;
        }

        /// <summary>Saves a brand-new career and opens the Dashboard.</summary>
        public static bool CreateAndOpen(CareerSave c, Window w)
        {
            string err;
            if (SaveSystem.TrySave(c, out err)) { GameApp.I.OpenCareer(c); return true; }
            if (w != null) w.Unblock();
            GameApp.I.Message("SAVE FAILED", "The team was not created and nothing was charged.\n" + err);
            return false;
        }
    }

    // =====================================================================
    public class OpeningScreen : IScreen
    {
        public void Build(RectTransform root)
        {
            Ui.FillBox(root, Theme.TopBar);
            Ui.Label(root, "IDLE FORMULA\nTEAM MANAGER", Theme.TitleGame, false, Theme.TextPrimary, 0, 216 - 70, 1920, 140, TextAnchor.MiddleCenter, true).lineSpacing = 1.4f;
            Ui.Icon(root, PixelArt.Car(Theme.Red), 960 - 128, 330, 256, 120);

            Ui.Button(root, "PLAY", BtnStyle.Red, 960 - 160, 540 - 32, 320, 64, Play, 24);
            Ui.Button(root, "CAREER SLOTS", BtnStyle.Dark, 960 - 160, 864 - 28, 320, 56, () => GameApp.I.Show(new CareerSlotsScreen()), 18);

            if (!string.IsNullOrEmpty(ConfigLoader.LoadError))
                Ui.Label(root, ConfigLoader.LoadError, Theme.Small, true, Theme.Yellow, 24, 1030, 1400, 30);
#if UNITY_EDITOR
            Ui.Button(root, "DEV: PREVIEW LOBBY", BtnStyle.Dark, 1920 - 24 - 240, 1080 - 24 - 40, 240, 40, DevPreview, 14);
#endif
        }

        static void Play()
        {
            int slot = SaveSystem.MostRecentSlot();
            if (slot > 0)
            {
                var c = SaveSystem.Load(slot);
                if (c != null) { GameApp.I.OpenCareer(c); return; }
            }
            GameApp.I.Show(new ChooseTeamScreen(1, false));
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: opens the lobby with an empty, unconfigured team so the layout can be checked.</summary>
        static void DevPreview()
        {
            var c = new CareerSave { slot = 99, teamName = "DEV PREVIEW (NOT A SAVE SLOT)", teamColor = "#F20D32", logo = 0, baseTeamId = "preview", rulesVersion = ConfigLoader.Config.rulesVersion };
            c.carParts = new[] { -1, -1, -1, -1, -1, -1 };
            c.seasons.Add(new SeasonRecord { season = 1 });
            GameApp.I.OpenCareer(c);
        }
#endif
    }

    // =====================================================================
    public class CareerSlotsScreen : IScreen
    {
        public void Build(RectTransform root)
        {
            Ui.FillBox(root, Theme.TopBar);
            Ui.Button(root, "BACK", BtnStyle.Dark, 24, 24, 160, 48, () => GameApp.I.Show(new OpeningScreen()));
            Ui.Label(root, "IDLE FORMULA TEAM MANAGER", 28, false, Theme.TextPrimary, 0, 120, 1920, 60, TextAnchor.MiddleCenter, true);
            Ui.Label(root, "CAREER SLOTS", Theme.ScreenTitle, true, Theme.TextSecondary, 0, 200, 1920, 50, TextAnchor.MiddleCenter);

            float w = (1920 - 48 - 40) / 3f, h = 1080 - 360 - 24;
            for (int i = 0; i < SaveSystem.Slots; i++)
            {
                int slot = i + 1;
                float x = 24 + i * (w + 20);
                var c = SaveSystem.Load(slot);
                var p = Ui.Panel(root, x, 360, w, h, "SLOT " + slot);
                if (c == null)
                {
                    Ui.Label(p, "Empty Slot", Theme.ScreenTitle, true, Theme.TextSecondary, 0, h / 2 - 80, w, 50, TextAnchor.MiddleCenter);
                    Ui.Button(p, "CREATE A NEW TEAM", BtnStyle.Red, 40, h - 20 - 48 - 20, w - 80, 48, () => GameApp.I.Show(new ChooseTeamScreen(slot, true)));
                }
                else
                {
                    Color tc = Theme.Hex(c.teamColor);
                    Ui.Ellipsis(p, c.teamName, Theme.ScreenTitle, true, Theme.TextPrimary, 20, 70, w - 40, 44, TextAnchor.MiddleCenter);
                    Ui.Icon(p, PixelArt.Logo(c.logo, tc), w / 2 - 48, 130, 96, 96);
                    Ui.Icon(p, PixelArt.Car(tc), w / 2 - 128, 250, 256, 120);
                    int rounds = Rules.Rounds;
                    string round = rounds > 0 ? "ROUND " + Mathf.Min(c.nextRound, rounds) + " OF " + rounds : "ROUND " + c.nextRound;
                    Ui.Label(p, "SEASON " + c.season + "   •   " + round, Theme.Body, true, Theme.TextSecondary, 0, 400, w, 30, TextAnchor.MiddleCenter);
                    Ui.Label(p, Theme.Money(c.balance), Theme.BigNumber, true, Theme.Green, 0, 440, w, 40, TextAnchor.MiddleCenter);
                    var cc = c;
                    Ui.Button(p, "CONTINUE", BtnStyle.Red, 40, h - 20 - 48 - 20, w - 80, 48, () => GameApp.I.OpenCareer(cc));
                }
            }
        }
    }

    // =====================================================================
    public class ChooseTeamScreen : IScreen
    {
        readonly int slot;
        readonly bool fromSlots;
        int selected = -1;

        /// <summary>Opponent level chosen for the next new career (kept between visits).</summary>
        public static int Difficulty = -1;

        public ChooseTeamScreen(int slot, bool fromSlots)
        {
            this.slot = slot; this.fromSlots = fromSlots;
            if (Difficulty < 0) Difficulty = Rules.DifficultyIndex(null);
        }

        public static void DifficultyRow(Transform root, float x, float y, float w, int current, System.Action<int> pick)
        {
            var list = ConfigLoader.Config.difficulties;
            Ui.Label(root, "OPPONENT LEVEL", Theme.Small, true, Theme.TextSecondary, x, y, 170, 44);
            if (list.Count == 0) { Ui.Label(root, Theme.NotConfigured, Theme.Body, false, Theme.TextSecondary, x + 180, y, 300, 44); return; }
            float bw = (w - 180 - (list.Count - 1) * 8) / list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var b = Ui.Button(root, list[i].name.ToUpperInvariant(), BtnStyle.Dark, x + 180 + i * (bw + 8), y, bw, 44, () => pick(idx), 16);
                b.SetActive(i == current);
            }
        }

        public void Build(RectTransform root)
        {
            Ui.FillBox(root, Theme.TopBar);
            Ui.Button(root, "BACK", BtnStyle.Dark, 24, 24, 160, 48, () => GameApp.I.Show(fromSlots ? (IScreen)new CareerSlotsScreen() : new OpeningScreen()));
            Ui.Label(root, "CHOOSE YOUR TEAM", Theme.ScreenTitle, true, Theme.TextPrimary, 0, 24, 1920, 48, TextAnchor.MiddleCenter);
            Ui.Label(root, "SLOT " + slot, Theme.Body, true, Theme.TextSecondary, 1920 - 224, 24, 200, 48, TextAnchor.MiddleRight);

            var cfg = ConfigLoader.Config;
            DifficultyRow(root, 24 + 1872 / 2f - 560, 88, 1120, Difficulty, i => { Difficulty = i; GameApp.I.RefreshCurrent(); });
            int off = cfg.difficulties.Count > 0 ? cfg.difficulties[Mathf.Clamp(Difficulty, 0, cfg.difficulties.Count - 1)].rivalOffset : 0;
            Ui.Label(root, (off > 0 ? "+" : "") + off + " to every rival rating", Theme.Small, false, Theme.TextSecondary, 1920 - 24 - 220, 88, 220, 44, TextAnchor.MiddleRight);
            float top = 148, w = (1872 - 60) / 4f, h = (1080 - top - 24 - 40) / 3f;
            for (int i = 0; i < 12; i++)
            {
                float x = 24 + (i % 4) * (w + 20), y = top + (i / 4) * (h + 20);
                if (i < 11) TeamCard(root, i, i < cfg.teams.Count ? cfg.teams[i] : null, x, y, w, h);
                else CustomCard(root, x, y, w, h);
            }
        }

        FtmButton Card(Transform root, int index, float x, float y, float w, float h, bool enabled)
        {
            var b = Ui.Button(root, "", BtnStyle.Dark, x, y, w, h, null);
            b.redBorderWhenActive = true;
            b.active = selected == index;
            b.SetEnabled(enabled);
            return b;
        }

        static void StatLine(Transform card, float y, float w, string label, string value)
        {
            Ui.Label(card, label, Theme.Small, false, Theme.TextSecondary, 16, y, w - 32, 22);
            Ui.Label(card, value, Theme.Small, true, value == Theme.NotConfigured ? Theme.TextSecondary : Theme.TextPrimary, 16, y, w - 32, 22, TextAnchor.MiddleRight);
        }

        void TeamCard(Transform root, int index, TeamDef t, float x, float y, float w, float h)
        {
            bool ok = Rules.TeamConfigured(t);
            var b = Card(root, index, x, y, w, h, ok);
            var tr = b.transform;
            Color tc = t != null ? Theme.Hex(t.color) : Theme.EmptyBar;
            Ui.Icon(tr, PixelArt.Logo(t != null && t.logo >= 0 ? t.logo : index, tc), w / 2 - 20, 12, 40, 40);
            Ui.Ellipsis(tr, t != null && !string.IsNullOrEmpty(t.name) ? t.name.ToUpperInvariant() : "TEAM " + (index + 1), Theme.Body, true, Theme.TextPrimary, 12, 54, w - 24, 26, TextAnchor.MiddleCenter);
            Ui.Icon(tr, PixelArt.Car(tc), w / 2 - 64, 84, 128, 60);
            if (!ok)
            {
                Ui.Label(tr, Theme.NotConfigured, Theme.Body, true, Theme.TextSecondary, 0, 160, w, 30, TextAnchor.MiddleCenter);
                Ui.Label(tr, "Add this team's values in GameConfig.json", Theme.Small, false, Theme.TextSecondary, 0, 190, w, 24, TextAnchor.MiddleCenter);
                return;
            }
            var drivers = t.drivers.Select(Rules.FindDriver).ToList();
            var engs = t.engineers.Select(Rules.FindEngineer).ToList();
            var co = Rules.FindCoordinator(t.coordinator);
            float sy = 146;
            StatLine(tr, sy, w, "Car overall", Rules.Fmt(Rules.Overall(t.carParts)));
            StatLine(tr, sy + 22, w, "Driver average", Rules.Fmt(Rules.AvgOveralls(drivers.Select(d => d.stats).ToList())));
            StatLine(tr, sy + 44, w, "Engineer average", Rules.Fmt(Rules.AvgOveralls(engs.Select(e => e.stats).ToList())));
            StatLine(tr, sy + 66, w, "Coordinator overall", Rules.Fmt(Rules.Overall(co.stats)));
            Ui.Label(tr, "Starting budget", Theme.Small, false, Theme.TextSecondary, 16, sy + 92, w - 32, 26);
            Ui.Label(tr, Theme.Money(t.startingBudget), Theme.Body, true, Theme.Green, 16, sy + 92, w - 32, 26, TextAnchor.MiddleRight);
            b.onClick.AddListener(() => { selected = index; GameApp.I.RefreshCurrent(); TeamDetails(t); });
        }

        void CustomCard(Transform root, float x, float y, float w, float h)
        {
            var cfg = ConfigLoader.Config;
            bool ok = Rules.CustomConfigured;
            var b = Card(root, 11, x, y, w, h, ok);
            var tr = b.transform;
            Color tc = Theme.Hex(string.IsNullOrEmpty(cfg.customTeam.color) ? "#B8C7CE" : cfg.customTeam.color);
            Ui.Label(tr, "?", 32, false, Theme.TextPrimary, w / 2 - 20, 12, 40, 40, TextAnchor.MiddleCenter, true);
            Ui.Label(tr, "CUSTOM TEAM", Theme.Body, true, Theme.TextPrimary, 12, 54, w - 24, 26, TextAnchor.MiddleCenter);
            Ui.Icon(tr, PixelArt.Car(tc), w / 2 - 64, 84, 128, 60);
            float sy = 146;
            StatLine(tr, sy, w, "Car overall", Rules.Fmt(Rules.Overall(cfg.customTeam.carParts)));
            StatLine(tr, sy + 22, w, "Drivers", "None");
            StatLine(tr, sy + 44, w, "Engineers", "None");
            StatLine(tr, sy + 66, w, "Coordinator", "None");
            Ui.Label(tr, "Starting budget", Theme.Small, false, Theme.TextSecondary, 16, sy + 92, w - 32, 26);
            Ui.Label(tr, Rules.FmtMoney(cfg.customTeam.startingBudget), Theme.Body, true, cfg.customTeam.startingBudget < 0 ? Theme.TextSecondary : Theme.Green, 16, sy + 92, w - 32, 26, TextAnchor.MiddleRight);
            if (!ok)
            {
                Ui.Label(tr, "Custom team " + Theme.NotConfigured.ToLowerInvariant(), Theme.Small, false, Theme.Yellow, 0, sy + 92 + 28, w, 24, TextAnchor.MiddleCenter);
                return;
            }
            b.onClick.AddListener(() =>
            {
                selected = 11; GameApp.I.RefreshCurrent();
                var win = GameApp.I.OpenWindow("CUSTOM TEAM", 900, 200, "CHOOSE TEAM", ww => { ww.Close(); GameApp.I.Show(new CustomTeamScreen(slot)); }, "BACK");
                Ui.Label(win.body, "Build your own team: pick a premade name and logo, then sign 2 drivers, 3 engineers and 1 coordinator.\n\nYou start with the biggest budget but no staff. Signing fees are charged once when you confirm the finished team.",
                    Theme.Body, false, Theme.TextSecondary, 0, 0, win.bodyWidth, 120, TextAnchor.UpperLeft);
                Ui.Label(win.body, "STARTING CASH   " + Theme.Money(cfg.customTeam.startingBudget), Theme.Body, true, Theme.Green, 0, 150, win.bodyWidth, 30);
            });
        }

        void TeamDetails(TeamDef t)
        {
            var drivers = t.drivers.Select(Rules.FindDriver).ToList();
            var engs = t.engineers.Select(Rules.FindEngineer).ToList();
            var co = Rules.FindCoordinator(t.coordinator);
            var everyone = new List<PersonDef>(); everyone.AddRange(drivers.Cast<PersonDef>()); everyone.AddRange(engs.Cast<PersonDef>()); everyone.Add(co);
            long payroll = Rules.Payroll(everyone);

            float rowH = 40, bodyH = 6 * rowH + 4 * 40 + 120;
            var w = GameApp.I.OpenWindow(t.name.ToUpperInvariant(), 900, bodyH, "CHOOSE TEAM", win =>
            {
                CareerFactory.CreateAndOpen(CareerFactory.FromTeam(t, slot), win);
            }, "BACK");
            float y = 0, bw = w.bodyWidth;
            Action<string> heading = s => { Ui.Label(w.body, s, Theme.Small, true, Theme.TextSecondary, 0, y, bw, 32); y += 36; };
            Action<PersonDef, string> row = (p, extra) =>
            {
                Ui.Box(w.body, Theme.RowA, 0, y, bw, rowH - 4);
                Ui.Ellipsis(w.body, People.NameLine(p), Theme.Body, true, Theme.TextPrimary, 12, y, bw * 0.5f, rowH - 4);
                Ui.Label(w.body, extra, Theme.Small, false, Theme.TextSecondary, bw * 0.5f, y, bw * 0.25f, rowH - 4);
                Ui.Label(w.body, p.salaryPerRace < 0 ? Theme.NotConfigured : Theme.Money(p.salaryPerRace) + " / RACE", Theme.Small, true, Theme.Red, bw * 0.7f, y, bw * 0.3f - 12, rowH - 4, TextAnchor.MiddleRight);
                y += rowH;
            };
            heading("DRIVERS");
            foreach (var d in drivers) row(d, "OVR " + Rules.Fmt(Rules.Overall(d.stats)) + "  POT " + Rules.Fmt(Rules.Overall(d.potential)));
            heading("ENGINEERS");
            foreach (var e in engs) row(e, "OVR " + Rules.Fmt(Rules.Overall(e.stats)));
            heading("COORDINATOR");
            row(co, "OVR " + Rules.Fmt(Rules.Overall(co.stats)));
            y += 12;
            Ui.Label(w.body, "STARTING CASH", Theme.Body, true, Theme.TextPrimary, 0, y, bw, 32);
            Ui.Label(w.body, Theme.Money(t.startingBudget), Theme.Body, true, Theme.Green, 0, y, bw, 32, TextAnchor.MiddleRight);
            y += 36;
            Ui.Label(w.body, "RECURRING PAYROLL", Theme.Body, true, Theme.TextPrimary, 0, y, bw, 32);
            Ui.Label(w.body, payroll < 0 ? Theme.NotConfigured : Theme.Money(payroll) + " / RACE", Theme.Body, true, payroll < 0 ? Theme.TextSecondary : Theme.Red, 0, y, bw, 32, TextAnchor.MiddleRight);
            w.onClosed += () => { selected = -1; };
        }
    }
}
