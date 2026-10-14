using System;
using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    public enum Tab { Dashboard, Drivers, Car, Staff, Sponsors, Transfers, Standings, History, Calendar }

    /// <summary>
    /// Lobby frame: top bar and bottom navigation stay put, only the 1872x880 content area changes.
    /// </summary>
    public class LobbyScreen : IScreen
    {
        public Tab tab = Tab.Dashboard;
        public PersonKind transferKind = PersonKind.Driver;
        public int historySeason = -1;

        public static readonly string[] TabNames = { "DASHBOARD", "DRIVERS", "CAR", "STAFF", "SPONSORS", "TRANSFERS", "STANDINGS", "HISTORY", "CALENDAR" };

        public static LobbyScreen Current;
        CareerSave C { get { return GameApp.I.Career; } }

        public void Build(RectTransform root)
        {
            Current = this;
            TopBar(root);
            NavBar(root);
            var content = Ui.Group(root, "Content", 24, 100, 1872, 880);
            switch (tab)
            {
                case Tab.Dashboard: DashboardTab.Build(content, C); break;
                case Tab.Drivers: DriversTab.Build(content, C); break;
                case Tab.Car: CarTab.Build(content, C); break;
                case Tab.Staff: StaffTab.Build(content, C); break;
                case Tab.Sponsors: SponsorsTab.Build(content, C); break;
                case Tab.Transfers: TransfersTab.Build(content, C, this); break;
                case Tab.Standings: StandingsTab.Build(content, C); break;
                case Tab.History: HistoryTab.Build(content, C, this); break;
                case Tab.Calendar: CalendarTab.Build(content, C); break;
            }
        }

        public void Go(Tab t) { tab = t; GameApp.I.RefreshCurrent(); }

        public static void Refresh() { GameApp.I.RefreshCurrent(); }

        void TopBar(RectTransform root)
        {
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TopBar").rectTransform;
            var home = Ui.Button(bar, "", BtnStyle.Dark, 24, 16, 48, 48, Home);
            Ui.Icon(home.transform, PixelArt.HouseIcon(), 8, 8, 32, 32);
            var settings = Ui.Button(bar, "", BtnStyle.Dark, 84, 16, 48, 48, OpenSettings);
            Ui.Icon(settings.transform, PixelArt.GearIcon(), 8, 8, 32, 32);

            Ui.Ellipsis(bar, C.teamName.ToUpperInvariant(), Theme.ScreenTitle, true, Theme.TextPrimary, 510, 0, 900, 80, TextAnchor.MiddleCenter);

            Ui.Label(bar, "TEAM BALANCE", Theme.Small, true, Theme.TextSecondary, 1420, 10, 250, 22);
            Ui.Label(bar, Theme.Money(C.balance), Theme.BigNumber, true, C.balance < 0 ? Theme.Red : Theme.Green, 1420, 32, 250, 38);

            var cfg = ConfigLoader.Config;
            string champ = string.IsNullOrEmpty(cfg.championshipName) ? "Championship " + Theme.NotConfigured.ToLowerInvariant() : cfg.championshipName.ToUpperInvariant();
            Ui.Ellipsis(bar, champ, Theme.Small, true, Theme.TextSecondary, 1690, 10, 1920 - 24 - 1690, 22, TextAnchor.MiddleRight);
            Ui.Label(bar, "SEASON " + C.season, Theme.Body, true, Theme.TextPrimary, 1690, 34, 1920 - 24 - 1690, 30, TextAnchor.MiddleRight);
        }

        void NavBar(RectTransform root)
        {
            float w = (1872 - 8 * 8) / 9f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                var b = Ui.Button(root, TabNames[i], BtnStyle.Dark, 24 + i * (w + 8), 1000, w, 56, () => Go(t), 18);
                b.SetActive(tab == t);
            }
        }

        void Home()
        {
            // Home finishes saving before leaving; it never deletes or resets anything.
            GameApp.I.SaveNow(() => { GameApp.I.Career = null; GameApp.I.Show(new OpeningScreen()); });
        }

        public static void OpenSettings()
        {
            var a = SaveSystem.LoadAudio();
            var career = GameApp.I.Career;
            int level = Rules.DifficultyIndex(career);
            int startLevel = level;
            float extra = career != null ? 130 : 0;
            var w = GameApp.I.OpenWindow("SETTINGS", 900, 220 + extra, "APPLY", win =>
            {
                SaveSystem.SaveAudio(a);
                if (career != null && level != startLevel)
                    GameApp.I.Commit(cs => cs.difficulty = level, () => { win.Close(); Refresh(); });
                else win.Close();
            });
            float bw = w.bodyWidth;
            if (career != null)
            {
                var levelRow = Ui.Group(w.body, "Level", 0, 0, bw, 50);
                System.Action draw = null;
                draw = () =>
                {
                    Ui.Clear(levelRow);
                    ChooseTeamScreen.DifficultyRow(levelRow, 0, 0, bw, level, i => { level = i; draw(); });
                };
                draw();
                Ui.Label(w.body, "For this career only. Changes how strong the rival teams are; your own team is not changed.", Theme.Small, false, Theme.TextSecondary, 0, 54, bw, 24);
                Ui.Box(w.body, Theme.EmptyBar, 0, 100, bw, 2);
            }
            var audio = Ui.Group(w.body, "Audio", 0, extra, bw, 220);
            w.body = audio;
            AudioRow(w.body, 0, bw, "MUSIC VOLUME", a.music, "No music in the game yet");
            AudioRow(w.body, 100, bw, "SOUND EFFECTS VOLUME", a.sfx, "No sound effects in the game yet");
            Ui.Label(w.body, "Shared by all saves. Does not affect team progress.", Theme.Small, false, Theme.TextSecondary, 0, 196, bw, 24);
        }

        static void AudioRow(Transform body, float y, float w, string label, int value, string disabledReason)
        {
            Ui.Label(body, label, Theme.Body, true, Theme.TextPrimary, 0, y, w, 28);
            Ui.Label(body, value.ToString(), Theme.Body, true, Theme.DisabledText, 0, y, w, 28, TextAnchor.MiddleRight);
            // A real slider, disabled until an audio channel exists.
            var track = Ui.Box(body, Theme.EmptyBar, 0, y + 40, w, 10, "Track");
            var fillArea = Ui.Stretch(Ui.Rect(track.transform, "FillArea"));
            var fill = Ui.Stretch(Ui.Rect(fillArea, "Fill"));
            var fillImg = fill.gameObject.AddComponent<Image>(); fillImg.color = Theme.DisabledText;
            var handleArea = Ui.Stretch(Ui.Rect(track.transform, "HandleArea"));
            var handle = Ui.Rect(handleArea, "Handle"); handle.sizeDelta = new Vector2(16, 24);
            var hImg = handle.gameObject.AddComponent<Image>(); hImg.color = Theme.DisabledText;
            var s = track.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = hImg;
            s.minValue = 0; s.maxValue = 100; s.wholeNumbers = true; s.value = value;
            s.transition = Selectable.Transition.None;
            s.interactable = false;
            Ui.Label(body, disabledReason, Theme.Small, false, Theme.TextSecondary, 0, y + 58, w, 24);
        }
    }

    /// <summary>Shared helpers for the lobby tabs.</summary>
    public static class LobbyKit
    {
        /// <summary>Row with name on the left and value on the right (Finances / Team Reports / Career).</summary>
        public static void ValueRow(Transform p, float x, float y, float w, string label, string value, Color valueColor, int index)
        {
            Ui.Box(p, index % 2 == 0 ? Theme.RowA : Theme.RowB, x, y, w, 48);
            Ui.Label(p, label, Theme.Body, false, Theme.TextPrimary, x + 16, y, w * 0.6f, 48);
            Ui.Ellipsis(p, value, Theme.Body, true, valueColor, x + w * 0.4f, y, w * 0.6f - 16, 48, TextAnchor.MiddleRight);
        }

        public static string PerRace(long v) { return Theme.Money(v) + " / RACE"; }

        /// <summary>Confirmation window: exact cost, current balance, balance afterward, benefit and duration.</summary>
        public static Window Confirm(string title, CareerSave c, long cost, string benefit, string duration, Action<CareerSave> apply, string confirmLabel = "CONFIRM", string[] extraLines = null)
        {
            int extra = extraLines != null ? extraLines.Length : 0;
            float h = 4 * 44 + (duration != null ? 44 : 0) + extra * 36 + 20;
            Window w = null;
            w = GameApp.I.OpenWindow(title, 600, h, confirmLabel, win =>
            {
                GameApp.I.Commit(apply, () => { win.Close(); LobbyScreen.Refresh(); });
            });
            float y = 0, bw = w.bodyWidth;
            Line(w.body, ref y, bw, "Cost", Theme.Money(cost), Theme.Red);
            Line(w.body, ref y, bw, "Current balance", Theme.Money(c.balance), Theme.TextPrimary);
            Line(w.body, ref y, bw, "Balance afterward", Theme.Money(c.balance - cost), c.balance - cost < 0 ? Theme.Red : Theme.Green);
            Line(w.body, ref y, bw, "Benefit", benefit, Theme.Green);
            if (duration != null) Line(w.body, ref y, bw, "Duration", duration, Theme.TextPrimary);
            if (extraLines != null)
                foreach (var s in extraLines) { Ui.Label(w.body, s, Theme.Small, false, Theme.TextSecondary, 0, y, bw, 32); y += 36; }
            if (cost > c.balance) w.Block("Not enough money");
            return w;
        }

        public static void Line(Transform body, ref float y, float w, string label, string value, Color c)
        {
            Ui.Label(body, label, Theme.Body, false, Theme.TextSecondary, 0, y, w * 0.45f, 40);
            Ui.Ellipsis(body, value, Theme.Body, true, c, w * 0.45f, y, w * 0.55f, 40, TextAnchor.MiddleRight);
            y += 44;
        }

        public static void Vacant(Transform p, float w, float h, string slotName, PersonKind kind)
        {
            Ui.Label(p, "Not Filled", Theme.ScreenTitle, true, Theme.Yellow, 0, h / 2 - 60, w, 44, TextAnchor.MiddleCenter);
            Ui.Button(p, "RECRUIT", BtnStyle.Red, w / 2 - 120, h / 2, 240, 48, () =>
            {
                var l = LobbyScreen.Current;
                l.transferKind = kind;
                l.Go(Tab.Transfers);
            });
        }
    }
}
