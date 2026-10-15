using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>
    /// The race control desk: running order on the left, the whole circuit in the middle, your two drivers
    /// stacked on the right, session controls along the bottom.
    /// </summary>
    public class RaceScreen : IScreen
    {
        public readonly RaceState s;
        Track t;
        public RaceScreen(RaceState state) { s = state; }

        public static RaceScreen Current;

        // layout constants (design pixels)
        const float ListX = 16, ListY = 96, ListW = 280, ListH = 968;
        const float CenterX = 312, CenterW = 1216;
        const float TrackY = 192, TrackH = 800;
        const float PanelX = 1544, PanelW = 360, PanelH = 476;

        int selected = -1;
        int listMode;              // 0 gap, 1 laps, 2 tyres/pits
        bool interval;             // gap to car ahead instead of leader
        float uiTimer;

        // track view
        RectTransform trackArea;
        float scale, offX, offY, minX, maxY;
        readonly List<CarView> views = new List<CarView>();
        RectTransform lightsBox;
        Image[] lights;

        // list
        Text listTitle, listSub;
        readonly List<RowView> rows = new List<RowView>();
        FtmButton[] modeBtns;
        RectTransform cutLine;
        FtmButton intervalBtn;

        // header
        Text flagText, selText, sessionLine, forecastText, wetText;
        Image flagBox;
        // race control
        RectTransform rcBanner; Text rcText; int rcShown = -1; float rcTimer; double rcLastTime = -1;
        RectTransform scTag; LineGraphic yellowLine; float yellowDrawnAt = -1;
        Image rainTint; RectTransform rainRoot; readonly List<RectTransform> drops = new List<RectTransform>();
        FtmButton[] speedBtns;

        DriverPanel[] panels = new DriverPanel[2];
        FollowCam follow;
        bool following;
        FtmButton wholeBtn, followBtn, simBtn;
        bool leaving;
        RaceRunner runner;

        class CarView { public RectTransform dot, ring, tag, line; public Image dotImg; public FtmButton tagBtn; public Text tagText; }
        class RowView { public RectTransform root; public Image bg, posBox, strip, outline; public Text pos, name, data, extra; public FtmButton btn; }

        public void Build(RectTransform root)
        {
            Current = this;
            t = Track.Load(s.trackId);
            var c = GameApp.I.Career;
            Ui.FillBox(root, Theme.Background);
            TeamHeader(root, c);
            DriverList(root);
            CenterHeader(root);
            TrackView(root);
            ControlStrip(root);
            for (int k = 0; k < 2; k++)
            {
                int idx = s.cars.FindIndex(x => x.mine == k);
                panels[k] = new DriverPanel(this, root, idx, PanelX, ListY + k * (PanelH + 16));
            }
            if (selected < 0) selected = s.cars.FindIndex(x => x.mine == 0);
            runner = root.gameObject.AddComponent<RaceRunner>();
            runner.screen = this;
            RefreshAll();
        }

        // ================================================================ top bar

        void TeamHeader(RectTransform root, CareerSave c)
        {
            var bar = Ui.Box(root, Theme.TopBar, 0, 0, 1920, 80, "TeamHeader").rectTransform;
            var home = Ui.Button(bar, "", BtnStyle.Dark, 24, 16, 48, 48, PauseMenu);
            Ui.Icon(home.transform, PixelArt.HouseIcon(), 8, 8, 32, 32);
            var set = Ui.Button(bar, "", BtnStyle.Dark, 84, 16, 48, 48, LobbyScreen.OpenSettings);
            Ui.Icon(set.transform, PixelArt.GearIcon(), 8, 8, 32, 32);
            Ui.Ellipsis(bar, c.teamName.ToUpperInvariant(), Theme.ScreenTitle, true, Theme.TextPrimary, 510, 0, 900, 80, TextAnchor.MiddleCenter);
            Ui.Label(bar, "TEAM BALANCE", Theme.Small, true, Theme.TextSecondary, 1420, 10, 250, 22);
            Ui.Label(bar, Theme.Money(c.balance), Theme.BigNumber, true, c.balance < 0 ? Theme.Red : Theme.Green, 1420, 32, 250, 38);
            Ui.Label(bar, "ROUND " + s.round + " / " + Rules.Rounds, Theme.Small, true, Theme.TextSecondary, 1690, 10, 206, 22, TextAnchor.MiddleRight);
            Ui.Label(bar, "SEASON " + s.season, Theme.Body, true, Theme.TextPrimary, 1690, 34, 206, 30, TextAnchor.MiddleRight);
        }

        // ================================================================ left list

        void DriverList(RectTransform root)
        {
            var p = Ui.Panel(root, ListX, ListY, ListW, ListH, null, Theme.TopBar);
            listTitle = Ui.Label(p, "RACE", Theme.PanelTitle, true, Theme.TextPrimary, 16, 10, ListW - 32, 30);
            listSub = Ui.Label(p, "", Theme.Small, true, Theme.TextSecondary, 16, 40, ListW - 32, 24);
            string[] modes = { "GAP", "LAPS", "TYRES / PITS" };
            modeBtns = new FtmButton[3];
            float bw = (ListW - 32 - 8) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int m = i;
                modeBtns[i] = Ui.Button(p, modes[i], BtnStyle.Dark, 16 + i * (bw + 4), 72, bw, 28, () => { listMode = m; RefreshList(); }, 12);
            }
            intervalBtn = Ui.Button(p, "LEADER", BtnStyle.Dark, ListW - 16 - 70, 106, 70, 22, () => { interval = !interval; RefreshList(); }, 11);
            Ui.Label(p, "POS  DRIVER", 12, true, Theme.TextSecondary, 16, 106, 160, 22);
            float y0 = 134;
            cutLine = Ui.Box(p, Theme.Red, 8, y0, ListW - 16, 3, "CutLine").rectTransform;
            for (int i = 0; i < s.cars.Count; i++)
            {
                var r = new RowView();
                var btn = Ui.Button(p, "", BtnStyle.Dark, 8, y0 + i * 34, ListW - 16, 32, null);
                btn.transition = Selectable.Transition.None;
                r.btn = btn;
                r.root = (RectTransform)btn.transform;
                r.bg = btn.fillImg;
                r.posBox = Ui.Box(r.root, Theme.TableHeader, 2, 2, 28, 28);
                r.pos = Ui.Label(r.posBox.transform, "", 14, true, Theme.Dark, 0, 0, 28, 28, TextAnchor.MiddleCenter);
                r.strip = Ui.Box(r.root, Color.white, 34, 4, 4, 24);
                r.name = Ui.Label(r.root, "", 15, true, Theme.TextPrimary, 44, 0, 60, 32);
                r.data = Ui.Label(r.root, "", 13, false, Theme.TextPrimary, 100, 0, 110, 32, TextAnchor.MiddleRight);
                r.extra = Ui.Label(r.root, "", 12, true, Theme.TextSecondary, 212, 0, 50, 32, TextAnchor.MiddleRight);
                int rowIndex = i;
                btn.onClick.AddListener(() => { var ord = RaceSim.Order(s); if (rowIndex < ord.Count) Select(ord[rowIndex]); });
                rows.Add(r);
            }
            cutLine.SetAsLastSibling();
        }

        void RefreshList()
        {
            for (int i = 0; i < 3; i++) modeBtns[i].SetActive(listMode == i);
            intervalBtn.gameObject.SetActive(listMode == 0 && s.phase >= 2 && s.session == Session.Race);
            intervalBtn.SetLabel(interval ? "AHEAD" : "LEADER");
            string speed = s.simulating ? "SIMULATING" : s.speed == 0 ? "PAUSED" : s.speed + "x";
            listSub.color = Theme.Yellow;
            if (s.session < Session.Race) { RefreshSessionList(speed); return; }
            cutLine.gameObject.SetActive(false);
            listTitle.text = "RACE";
            int leadLap = Mathf.Min(s.totalLaps, RaceSim.Order(s).Select(i => s.cars[i].lap).DefaultIfEmpty(0).Max() + 1);
            listSub.text = s.phase < 2 ? "FORMATION  •  " + speed : (s.chequered ? "FINISH" : "LAP " + leadLap + " / " + s.totalLaps) + "  •  " + speed;

            var order = RaceSim.Order(s);
            float prevGap = 0;
            for (int p = 0; p < rows.Count; p++)
            {
                var r = rows[p];
                if (p >= order.Count) { r.root.gameObject.SetActive(false); continue; }
                int i = order[p];
                var car = s.cars[i];
                r.pos.text = (p + 1).ToString();
                r.strip.color = Theme.Hex(car.color);
                r.name.text = car.tla;
                Color rowColor = car.mine >= 0 ? Theme.Darken(Theme.Red, 0.6f) : (p % 2 == 0 ? Theme.RowA : Theme.RowB);
                r.btn.customFill = rowColor;
                r.btn.outlineOverride = i == selected ? Color.white : rowColor;
                r.btn.Apply();
                r.name.color = Theme.TextPrimary; r.data.color = Theme.TextPrimary;
                string data = "", extra = "";
                int lapsDown = 0;
                float gap = 0;
                if (s.phase >= 2) gap = RaceSim.GapToLeader(s, t, i, out lapsDown);
                string status = StatusShort(car);
                if (listMode == 0)
                {
                    if (status != null) data = status;
                    else if (s.phase < 2) data = "GRID";
                    else if (p == 0) data = "LEADER";
                    else if (lapsDown > 0) data = "+" + lapsDown + (lapsDown == 1 ? " LAP" : " LAPS");
                    else data = "+" + (interval ? Mathf.Max(0, gap - prevGap) : gap).ToString("0.0");
                    extra = Tyre.Letter[CurComp(car)];
                }
                else if (listMode == 1)
                {
                    data = status ?? RaceSim.Lap(car.lastLap);
                    extra = "L" + car.lap;
                }
                else
                {
                    var set = car.set >= 0 ? car.sets[car.set] : null;
                    data = status ?? (set != null ? Tyre.Letter[set.compound] + " " + Mathf.FloorToInt(car.tyreLapAge) + " laps" : "--");
                    extra = car.stops + " ST";
                }
                r.data.text = data;
                r.extra.color = listMode == 0 ? Theme.Hex(Tyre.Hex[CurComp(car)]) : Theme.TextSecondary;
                // race control markers: pending penalty, drive-through, under investigation
                if (car.status != 3)
                {
                    float pen = RaceControl.PendingSeconds(s, i);
                    if (RaceControl.PendingDriveThrough(s, i)) { extra = "DT"; r.extra.color = Theme.Red; }
                    else if (pen > 0 && listMode != 1) { extra = "+" + pen.ToString("0") + "s"; r.extra.color = Theme.Red; }
                    else if (RaceControl.UnderInvestigation(s, i) && listMode != 1) { extra = "INV"; r.extra.color = Theme.Yellow; }
                }
                r.extra.text = extra;
                if (lapsDown == 0) prevGap = gap;
            }
        }

        void RefreshSessionList(string speed)
        {
            bool quali = Session.IsQuali(s);
            listTitle.text = Session.Names[s.session];
            listSub.text = (s.sessionLeft > 0 ? Session.TimeLeft(s) + " LEFT" : "CHEQUERED FLAG") + "  •  " + speed;
            var order = RaceSim.Order(s);
            float fastest = order.Count > 0 ? Session.SegBest(s, s.cars[order[0]]) : 0;
            int active = Session.Active(s), safe = active - Session.Cut(s);
            bool showCut = quali && s.session != Session.Q3;
            cutLine.gameObject.SetActive(showCut);
            if (showCut) cutLine.anchoredPosition = new Vector2(8, -(134 + safe * 34 - 3));
            for (int p = 0; p < rows.Count; p++)
            {
                var r = rows[p];
                if (p >= order.Count) { r.root.gameObject.SetActive(false); continue; }
                int i = order[p];
                var car = s.cars[i];
                r.pos.text = (p + 1).ToString();
                r.strip.color = Theme.Hex(car.color);
                r.name.text = car.tla;
                Color rowColor = car.mine >= 0 ? Theme.Darken(Theme.Red, 0.6f) : (p % 2 == 0 ? Theme.RowA : Theme.RowB);
                if (quali && car.qOutSeg > 0) rowColor = Theme.Darken(rowColor, 0.6f);
                r.btn.customFill = rowColor;
                r.btn.outlineOverride = i == selected ? Color.white : rowColor;
                r.btn.Apply();
                float best = quali && car.qOutSeg > 0 ? (car.qOutSeg == 2 ? car.q2 : car.q1) : Session.SegBest(s, car);
                string status = SessionStatus(car);
                string data, extra;
                if (listMode == 1) { data = car.lastLap > 0 ? RaceSim.Lap(car.lastLap) : status ?? "--"; extra = car.sLaps + " L"; }
                else if (listMode == 2)
                {
                    var set = car.set >= 0 ? car.sets[car.set] : null;
                    data = set != null ? Tyre.Name[set.compound].ToUpperInvariant() + " " + Mathf.RoundToInt(set.wear * 100) + "%" : "--";
                    extra = car.setsUsedNew + " NEW";
                }
                else
                {
                    if (best <= 0) data = status ?? "NO TIME";
                    else if (p == 0 || (quali && car.qOutSeg > 0)) data = RaceSim.Lap(best);
                    else data = "+" + (best - fastest).ToString("0.000");
                    extra = quali && car.qOutSeg > 0 ? "OUT" : showCut && p >= safe ? "RISK" : status != null && best > 0 ? StatusAbbrev(car) : Tyre.Letter[CurComp(car)];
                }
                r.data.text = data;
                r.extra.text = extra;
                r.extra.color = extra == "RISK" || extra == "OUT" ? Theme.Red : listMode == 0 && extra.Length == 1 ? Theme.Hex(Tyre.Hex[CurComp(car)]) : Theme.TextSecondary;
                r.name.color = Theme.TextPrimary; r.data.color = Theme.TextPrimary;
            }
        }

        static string SessionStatus(CarState car)
        {
            if (car.qOutSeg > 0) return "OUT";
            switch (car.status)
            {
                case 5: return "GARAGE";
                case 3: return "STOPPED";
                case 1: case 2: return "PIT LANE";
            }
            if (car.outLap) return "OUT LAP";
            if (car.pitQueued) return "IN LAP";
            return null;
        }

        static string StatusAbbrev(CarState car)
        {
            switch (car.status) { case 5: return "G"; case 3: return "X"; case 1: case 2: return "P"; }
            return car.outLap ? "OL" : car.pitQueued ? "IL" : "F";
        }

        static int CurComp(CarState car) { return car.set >= 0 ? car.sets[car.set].compound : 1; }

        static string StatusShort(CarState car)
        {
            switch (car.status)
            {
                case 1: return car.driveThroughPending && !car.pitQueued ? "DRIVE THRU" : "PIT LANE";
                case 2: return car.serviceLeft > car.lastService ? "PENALTY" : "IN PIT";
                case 3: return "OUT";
                case 4: return null;
            }
            if (car.tyreLapAge < 1f && car.stops > 0) return null;
            return null;
        }

        // ================================================================ centre header

        void CenterHeader(RectTransform root)
        {
            var h = Ui.Panel(root, CenterX, ListY, CenterW, 80, null, Theme.TopBar);
            Ui.Label(h, s.grandPrix.ToUpperInvariant(), Theme.PanelTitle, true, Theme.TextPrimary, 176, 4, CenterW - 580, 30, TextAnchor.MiddleCenter);
            sessionLine = Ui.Label(h, "", Theme.Small, false, Theme.TextSecondary, 176, 30, CenterW - 580, 20, TextAnchor.MiddleCenter);
            flagBox = Ui.Box(h, Theme.Green, 12, 8, 150, 38);
            flagText = Ui.Label(flagBox.transform, "GREEN", Theme.Body, true, Theme.Dark, 0, 0, 150, 38, TextAnchor.MiddleCenter);
            // weather: the forecast is what the teams see; the wetness is measured on track
            forecastText = Ui.Label(h, "", 12, true, Theme.TextSecondary, CenterW - 390, 6, 378, 20, TextAnchor.MiddleRight);
            forecastText.supportRichText = true;
            wetText = Ui.Label(h, "", 12, true, Theme.TextSecondary, CenterW - 390, 26, 378, 20, TextAnchor.MiddleRight);
            wetText.supportRichText = true;
            Ui.Box(h, Theme.Panel, 2, 52, CenterW - 4, 26);
            selText = Ui.Label(h, "", Theme.Small, true, Theme.TextPrimary, 14, 52, CenterW - 160, 26);
            Ui.Button(h, "DETAILS", BtnStyle.Dark, CenterW - 110, 54, 100, 22, () => Details(selected), 12);
        }

        void RefreshHeader()
        {
            bool sess = s.session < Session.Race;
            bool cheq = sess ? s.sessionLeft <= 0 : (s.chequered || s.phase >= 3);
            string flag = cheq ? "CHEQUERED" : string.IsNullOrEmpty(s.flag) ? "GREEN" : s.flag;
            bool restart = !sess && s.phase == 1 && s.firstCross.Count > 0;
            if (!sess && s.phase < 2) flag = restart ? "RESTART" : "FORMATION";
            flagText.text = flag;
            flagBox.color = FlagColor(flag);
            flagText.color = flag == "RED FLAG" ? Color.white : Theme.Dark;
            if (sess)
            {
                float cut = Session.CutoffTime(s);
                sessionLine.text = Session.Names[s.session] + "  •  " + Session.TimeLeft(s) + " REMAINING" + (cut > 0 ? "  •  CUT-OFF " + RaceSim.Lap(cut) : "") + "  •  " + s.circuit;
            }
            else if (s.scMode == RaceControl.Red)
                sessionLine.text = "RACE SUSPENDED  •  " + (s.rain >= 0.75f && s.redTimer <= 0 ? "WAITING FOR THE RAIN TO EASE" : "RESTART IN " + Session.TimeLeftOf(Mathf.Max(0, s.redTimer))) + "  •  FREE TYRE CHANGE";
            else if (s.scMode == RaceControl.SC)
                sessionLine.text = "SAFETY CAR  •  NO OVERTAKING" + (s.scIn ? "  •  SAFETY CAR IN THIS LAP" : "") + "  •  " + s.circuit;
            else if (s.scMode == RaceControl.VSC)
                sessionLine.text = "VIRTUAL SAFETY CAR  •  DELTA SPEED  •  NO OVERTAKING";
            else sessionLine.text = "ROUND " + s.round + " / " + Rules.Rounds + "  •  " + s.totalLaps + " LAPS  •  " + s.circuit;
            // weather
            string fc = "FORECAST ";
            foreach (int m in new[] { 5, 10, 15 })
            {
                int pct = Mathf.RoundToInt(RaceControl.Forecast(s, m) * 100f);
                string col = pct >= 60 ? "#4FC3F7" : pct >= 30 ? "#F5D84A" : "#9AA3AD";
                fc += "  " + m + "' <color=" + col + ">" + (pct < 10 ? "DRY" : pct + "% RAIN") + "</color>";
            }
            forecastText.text = fc;
            int wet = Mathf.RoundToInt(s.wetness * 100f);
            string cond = s.rain > 0.6f ? "HEAVY RAIN" : s.rain > 0.05f ? "RAIN" : s.wetness > 0.07f ? "WET" : s.wetness > 0.02f ? "DAMP" : "DRY";
            wetText.text = "NOW <color=" + (s.rain > 0.05f ? "#4FC3F7" : "#FFFFFF") + ">" + cond + "</color>  •  TRACK WETNESS <color=" + (wet >= 7 ? "#4FC3F7" : "#FFFFFF") + ">" + wet + "%</color>";
            simBtn.gameObject.SetActive(sess && !s.sessionEnded);
            if (selected >= 0)
            {
                var car = s.cars[selected];
                int pos = RaceSim.Order(s).IndexOf(selected) + 1;
                selText.text = car.name.ToUpperInvariant() + "   •   " + car.teamName + "   •   P" + pos + "   •   " + StatusLong(car);
            }
            for (int i = 0; i < speedBtns.Length; i++) speedBtns[i].SetActive(SpeedOf(i) == s.speed);
        }

        public static Color FlagColor(string flag)
        {
            switch (flag)
            {
                case "CHEQUERED": return Color.white;
                case "RED FLAG": return Theme.Red;
                case "YELLOW": case "VSC": case "SAFETY CAR": case "FORMATION": case "RESTART": return Theme.Yellow;
                default: return Theme.Green;
            }
        }

        public static string StatusLong(CarState car)
        {
            switch (car.status)
            {
                case 1: return car.driveThroughPending && !car.pitQueued ? "Drive-through penalty" : car.pitQueued ? "Pit entry" : "Pit exit";
                case 2: return car.serviceLeft > car.lastService ? "Serving a time penalty" : "Pit service";
                case 3: return string.IsNullOrEmpty(car.retireReason) ? "Stopped (" + car.issue + ")" : "Retired (" + car.retireReason + ")";
                case 4: return "Finished";
                case 5: return car.qOutSeg > 0 ? "Eliminated" : "In the garage";
            }
            if (car.outLap) return "Out lap";
            if (car.lapValid) return "Flying lap";
            if (car.dist < 0) return "On the grid";
            if (car.stops > 0 && car.tyreLapAge < 0.4f) return "Out lap";
            return "On track";
        }

        // ================================================================ track

        void TrackView(RectTransform root)
        {
            var area = Ui.Panel(root, CenterX, TrackY, CenterW, TrackH, null, Theme.Hex("#4F942E"));
            trackArea = area;
            // fit the full circuit with 48px clearance
            var all = new List<Vector2>(t.pts);
            all.AddRange(t.PitPolyline());
            minX = all.Min(p => p.x); float maxX = all.Max(p => p.x);
            float minY = all.Min(p => p.y); maxY = all.Max(p => p.y);
            float w = CenterW - 96 - 40, h = TrackH - 96 - 30;
            scale = Mathf.Min(w / (maxX - minX), h / (maxY - minY));
            offX = 48 + 20 + (w - (maxX - minX) * scale) / 2f;
            offY = 48 + 15 + (h - (maxY - minY) * scale) / 2f;

            var pitGo = Ui.Group(area, "Pit", 0, 0, CenterW, TrackH).gameObject;
            pitGo.AddComponent<CanvasRenderer>();
            var pit = pitGo.AddComponent<LineGraphic>();
            pit.color = Theme.Hex("#C9CED3"); pit.width = 3; pit.raycastTarget = false;
            pit.SetPoints(t.PitPolyline().Select(ToScreen), false);
            var lineGo = Ui.Group(area, "Track", 0, 0, CenterW, TrackH).gameObject;
            lineGo.AddComponent<CanvasRenderer>();
            var line = lineGo.AddComponent<LineGraphic>();
            line.color = Color.white; line.width = 6; line.raycastTarget = false;
            line.SetPoints(t.pts.Select(ToScreen), true);
            // yellow-flag sector highlight
            var yGo = Ui.Group(area, "YellowZone", 0, 0, CenterW, TrackH).gameObject;
            yGo.AddComponent<CanvasRenderer>();
            yellowLine = yGo.AddComponent<LineGraphic>();
            yellowLine.color = Theme.Yellow; yellowLine.width = 9; yellowLine.raycastTarget = false;
            yGo.SetActive(false);

            // sector boundaries and the start/finish line
            Mark(area, t.sectorAt[0], Theme.Yellow, "S2");
            Mark(area, t.sectorAt[1], Theme.Yellow, "S3");
            Mark(area, 0, Color.black, null);
            var sf = ToScreen(t.Pos(0));
            var tag = Ui.Box(area, Color.black, sf.x + 10, sf.y - 30, 110, 20, "SF");
            Ui.Label(tag.transform, "START / FINISH", 11, true, Color.white, 0, 0, 110, 20, TextAnchor.MiddleCenter);
            // direction arrow
            var a = ToScreen(t.Pos(60)); var b = ToScreen(t.Pos(110));
            var arrow = Ui.Label(area, ">", 20, true, Color.white, 0, 0, 20, 20, TextAnchor.MiddleCenter);
            arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Vector2 mid = (a + b) / 2f + (Vector2)(Quaternion.Euler(0, 0, 90) * (b - a).normalized) * 14f;
            arrow.rectTransform.anchoredPosition = new Vector2(mid.x, -mid.y);
            arrow.rectTransform.localEulerAngles = new Vector3(0, 0, -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);

            for (int i = 0; i < s.cars.Count; i++)
            {
                var car = s.cars[i];
                var v = new CarView();
                Color tc = Theme.Hex(car.color);
                v.line = Ui.Box(area, Color.white, 0, 0, 1, 2, "Conn").rectTransform;
                v.line.pivot = new Vector2(0, 0.5f);
                v.ring = Ui.Icon(area, PixelArt.Circle(), 0, 0, 20, 20, Color.white).rectTransform;
                v.dotImg = Ui.Icon(area, PixelArt.Circle(), 0, 0, 12, 12, tc);
                v.dot = v.dotImg.rectTransform;
                int idx = i;
                var tagBtn = Ui.Button(area, "", BtnStyle.Dark, 0, 0, 40, 22, () => Select(idx));
                tagBtn.customFill = tc; tagBtn.Apply();
                v.tag = (RectTransform)tagBtn.transform;
                v.tagBtn = tagBtn;
                v.tagText = Ui.Label(v.tag, car.tla, 14, true, Theme.TextOn(tc), 0, 0, 40, 22, TextAnchor.MiddleCenter);
                v.tagText.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (car.tla.Length > 3) { v.tag.sizeDelta = new Vector2(54, 22); v.tagText.rectTransform.sizeDelta = new Vector2(54, 22); tagBtn.fillImg.rectTransform.sizeDelta = new Vector2(50, 18); }
                views.Add(v);
            }

            // Safety Car marker
            var scImg = Ui.Box(area, Theme.Yellow, 0, 0, 34, 20, "SC");
            Ui.Label(scImg.transform, "SC", 13, true, Theme.Dark, 0, 0, 34, 20, TextAnchor.MiddleCenter);
            scTag = scImg.rectTransform;
            scTag.gameObject.SetActive(false);

            // follow camera (hidden until FOLLOW SELECTED is pressed)
            follow = new FollowCam(s, t, area, CenterW, TrackH, Select);

            // start lights
            lightsBox = Ui.Box(area, Theme.TopBar, CenterW / 2 - 170, 20, 340, 80, "Lights").rectTransform;
            lights = new Image[5];
            for (int i = 0; i < 5; i++) lights[i] = Ui.Icon(lightsBox, PixelArt.Circle(), 20 + i * 64, 12, 56, 56, Theme.Hex("#3A1010"));

            // rain: a darker sky and falling streaks over the track view
            rainTint = Ui.Box(area, new Color(0.1f, 0.16f, 0.28f, 0f), 0, 0, CenterW, TrackH, "RainTint");
            rainRoot = Ui.Group(area, "Rain", 0, 0, CenterW, TrackH);
            for (int i = 0; i < 70; i++)
            {
                var d = Ui.Box(rainRoot, new Color(0.75f, 0.85f, 1f, 0.45f), 0, 0, 2, 18, "Drop").rectTransform;
                d.localEulerAngles = new Vector3(0, 0, -12f);
                d.anchoredPosition = new Vector2(UnityEngine.Random.Range(0f, CenterW), -UnityEngine.Random.Range(0f, TrackH));
                drops.Add(d);
            }
            rainRoot.gameObject.SetActive(false);

            // race control messages
            var banner = Ui.Box(area, Theme.TopBar, 16, TrackH - 16 - 40, CenterW - 32, 40, "RaceControl");
            rcBanner = banner.rectTransform;
            Ui.Box(rcBanner, Theme.Yellow, 0, 0, 8, 40);
            Ui.Label(rcBanner, "RACE CONTROL", 12, true, Theme.Yellow, 20, 0, 130, 40);
            rcText = Ui.Label(rcBanner, "", Theme.Small, true, Theme.TextPrimary, 150, 0, CenterW - 32 - 160, 40);
            rcBanner.gameObject.SetActive(false);
            // don't replay old messages when a saved race is reopened
            rcShown = s.control.Count - 1;
            rcLastTime = s.control.Count > 0 ? s.control[s.control.Count - 1].time : -1;
        }

        void RefreshRaceControl(float realDt)
        {
            // newest race-control message, shown for a few seconds
            if (s.control.Count > 0)
            {
                var last = s.control[s.control.Count - 1];
                if (last.time != rcLastTime || s.control.Count - 1 != rcShown)
                {
                    rcLastTime = last.time; rcShown = s.control.Count - 1;
                    rcText.text = "LAP " + last.lap + "   " + last.text.ToUpperInvariant();
                    if (s.session < Session.Race) rcText.text = last.text.ToUpperInvariant();
                    rcTimer = 6f;
                }
            }
            rcTimer -= realDt;
            // rain and wet-track tint (above the follow camera, below the banner and lights)
            var tc = rainTint.color; tc.a = 0.32f * Mathf.Clamp01(s.wetness + s.rain * 0.5f); rainTint.color = tc;
            rainTint.rectTransform.SetAsLastSibling();
            bool raining = s.rain > 0.05f;
            rainRoot.gameObject.SetActive(raining);
            if (raining)
            {
                rainRoot.SetAsLastSibling();
                int shown = Mathf.Clamp(Mathf.RoundToInt(drops.Count * s.rain), 8, drops.Count);
                float fall = (s.speed > 0 ? 900f : 0f) * realDt;
                for (int k = 0; k < drops.Count; k++)
                {
                    var d = drops[k];
                    d.gameObject.SetActive(k < shown);
                    var pos = d.anchoredPosition + new Vector2(fall * 0.2f, -fall);
                    if (-pos.y > TrackH) pos = new Vector2(UnityEngine.Random.Range(-60f, CenterW), 20f);
                    d.anchoredPosition = pos;
                }
            }
            lightsBox.SetAsLastSibling();
            rcBanner.gameObject.SetActive(rcTimer > 0);
            rcBanner.SetAsLastSibling();

            // yellow sector and the Safety Car (whole-track view only)
            bool yellow = !following && s.yellowTimer > 0 && s.phase == 2;
            yellowLine.gameObject.SetActive(yellow);
            if (yellow && yellowDrawnAt != s.yellowAt)
            {
                yellowDrawnAt = s.yellowAt;
                var pts = new List<Vector2>();
                for (float d = -250f; d <= 60f; d += 10f) pts.Add(ToScreen(t.Pos(t.Wrap(s.yellowAt + d))));
                yellowLine.SetPoints(pts, false);
            }
            bool sc = !following && s.session == Session.Race && s.scMode == RaceControl.SC && !s.scInPit;
            scTag.gameObject.SetActive(sc);
            if (sc)
            {
                var p = ToScreen(t.Pos(s.scDist));
                scTag.anchoredPosition = new Vector2(p.x - 17, -(p.y - 10));
                scTag.SetAsLastSibling();
            }
        }

        void Mark(RectTransform area, float lapS, Color c, string label)
        {
            var p = ToScreen(t.Pos(lapS));
            int i = Mathf.Clamp((int)(lapS / t.step), 0, t.n - 1);
            var nrm = t.normalIn[i];
            var q = ToScreen(t.Pos(lapS) + nrm * (10f / scale));
            float ang = Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg;
            var bar = Ui.Box(area, c, p.x, p.y, 18, 4, "Mark").rectTransform;
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(p.x, -p.y);
            bar.localEulerAngles = new Vector3(0, 0, -ang);
            if (label != null) Ui.Label(area, label, 11, true, c, p.x + 8, p.y + 6, 24, 14);
        }

        Vector2 ToScreen(Vector2 m) { return new Vector2(offX + (m.x - minX) * scale, offY + (maxY - m.y) * scale); }

        /// <summary>Sim seconds since the last simulation step: used to draw cars smoothly between steps.</summary>
        float Lead { get { return (runner != null && s.speed > 0 && s.phase == 2 && !s.simulating) ? Mathf.Min(runner.acc, RaceSim.TickSeconds) : 0f; } }

        /// <summary>
        /// Moves every car's drawn position along the track each frame: it advances at the car's own speed and
        /// eases toward where the simulation says it is, so cars drive smoothly at 1x-5x instead of hopping
        /// from step to step (and never slide backwards when they get stuck behind someone).
        /// </summary>
        public static void UpdateVisuals(RaceState s, float realDt, float lead)
        {
            bool running = s.phase == 2 && s.speed > 0 && !s.simulating;
            float simDt = running ? realDt * RaceSim.SimSecondsPer1x * s.speed : 0f;
            float k = 1f - Mathf.Exp(-9f * realDt);
            float pitLen = t_PitLen(s);
            foreach (var car in s.cars)
            {
                bool inPit = car.status == 1 || car.status == 2 || car.status == 5;
                bool moving = car.status == 0 || car.status == 1;
                double target = (inPit ? car.pitS : car.dist) + (moving && running ? car.speed * lead : 0);
                double cur = inPit ? car.vPit : car.vDist;
                if (!car.vInit || inPit != car.vInPit || Math.Abs(target - cur) > 250 || target < cur - 30 || car.status >= 3 || (!inPit && car.dist < 0))
                    cur = target;                                       // first frame, pit entry/exit, restarts, the grid
                else
                {
                    double before = cur;
                    if (moving) cur += car.speed * simDt;              // keep driving at the car's speed
                    cur += (target - cur) * k;                          // and settle onto the true position
                    if (cur < before) cur = before;                     // never roll backwards
                }
                if (inPit) car.vPit = Mathf.Min(pitLen, (float)cur); else car.vDist = cur;
                car.vInPit = inPit;
                car.vInit = true;
                float latTarget = car.status == 0 && car.overtaking && car.dist >= 0 ? 3f : 0f;
                car.vLat = Mathf.MoveTowards(car.vLat, latTarget, realDt * 6f);
            }
        }

        static float t_PitLen(RaceState s) { var tr = Track.Load(s.trackId); return tr != null ? tr.pitLen : float.MaxValue; }

        Vector2 CarScreen(CarState car)
        {
            if (car.status == 1 || car.status == 2 || car.status == 5)
                return ToScreen(t.PitPos(Mathf.Min(t.pitLen, car.vInit ? car.vPit : car.pitS)));
            var p = ToScreen(t.Pos(car.vInit ? car.vDist : car.dist));
            if (car.dist < 0)   // two-wide grid
            {
                int i = Mathf.Clamp((int)(t.Wrap(car.dist) / t.step), 0, t.n - 1);
                var side = t.normalIn[i] * (car.grid % 2 == 0 ? 6f : -6f);
                p = ToScreen(t.Pos(car.dist) + side);
            }
            return p;
        }

        static readonly Vector2[] TagOffsets =
        {
            new Vector2(10, -30), new Vector2(10, 8), new Vector2(-50, -30), new Vector2(-50, 8),
            new Vector2(14, -54), new Vector2(-54, -54), new Vector2(14, 32), new Vector2(-54, 32),
            new Vector2(40, -12), new Vector2(-80, -12), new Vector2(30, -78), new Vector2(-70, 56)
        };

        void RefreshTrack()
        {
            if (following)
            {
                lightsBox.gameObject.SetActive(s.phase == 1);
                lightsBox.SetAsLastSibling();
                if (s.phase == 1)
                    for (int i = 0; i < 5; i++) lights[i].color = i < s.lightsOn ? Theme.Red : Theme.Hex("#3A1010");
                return;
            }
            var placed = new List<Rect>();
            var order = RaceSim.Order(s);
            foreach (int i in order)
            {
                var car = s.cars[i];
                var v = views[i];
                var p = CarScreen(car);
                bool gone = car.status == 3;
                v.dot.anchoredPosition = new Vector2(p.x - 6, -(p.y - 6));
                v.dotImg.color = gone ? new Color(0.3f, 0.3f, 0.3f, 0.6f) : Theme.Hex(car.color);
                v.ring.gameObject.SetActive(i == selected);
                v.ring.anchoredPosition = new Vector2(p.x - 10, -(p.y - 10));
                bool garage = car.status == 5;
                v.dotImg.color = garage ? Theme.Darken(Theme.Hex(car.color), 0.7f) : v.dotImg.color;
                v.tag.gameObject.SetActive(!gone && !garage);
                if (gone || garage) { v.line.gameObject.SetActive(false); continue; }
                Vector2 size = v.tag.sizeDelta;
                Rect best = default(Rect); bool found = false;
                // keep the tag where it was while that spot is still free, so tags don't flick around
                for (int k = -1; k < TagOffsets.Length && !found; k++)
                {
                    int slot = k < 0 ? car.vTag : k;
                    if (slot < 0 || slot >= TagOffsets.Length) continue;
                    var o = TagOffsets[slot];
                    var r = new Rect(p.x + o.x, p.y + o.y, size.x, size.y);
                    r.x = Mathf.Clamp(r.x, 4, CenterW - size.x - 4);
                    r.y = Mathf.Clamp(r.y, 4, TrackH - size.y - 4);
                    if (!placed.Any(q => q.Overlaps(r))) { best = r; found = true; car.vTag = slot; }
                }
                if (!found) best = new Rect(Mathf.Clamp(p.x + 10, 4, CenterW - size.x - 4), Mathf.Clamp(p.y - 30, 4, TrackH - size.y - 4), size.x, size.y);
                placed.Add(best);
                v.tag.anchoredPosition = new Vector2(best.x, -best.y);
                v.tagBtn.outlineOverride = i == selected ? Color.white : Theme.Hex(car.color);
                v.tagBtn.Apply();
                // connector from the dot to the tag
                Vector2 target = new Vector2(Mathf.Clamp(p.x, best.xMin, best.xMax), Mathf.Clamp(p.y, best.yMin, best.yMax));
                Vector2 d = target - p;
                bool show = d.magnitude > 8;
                v.line.gameObject.SetActive(show);
                if (show)
                {
                    v.line.anchoredPosition = new Vector2(p.x, -p.y);
                    v.line.sizeDelta = new Vector2(d.magnitude, 2);
                    v.line.localEulerAngles = new Vector3(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    v.line.GetComponent<Image>().color = Theme.Hex(car.color);
                }
            }
            // keep the selected driver's tag on top
            if (selected >= 0) views[selected].tag.SetAsLastSibling();

            lightsBox.gameObject.SetActive(s.phase == 1);
            if (s.phase == 1)
                for (int i = 0; i < 5; i++) lights[i].color = i < s.lightsOn ? Theme.Red : Theme.Hex("#3A1010");
        }

        // ================================================================ controls

        static int SpeedOf(int i) { return i == 0 ? 0 : i == 1 ? 1 : i == 2 ? 3 : 5; }

        void ControlStrip(RectTransform root)
        {
            var strip = Ui.Panel(root, CenterX, 1008, CenterW, 56, null, Theme.TopBar);
            wholeBtn = Ui.Button(strip, "WHOLE TRACK", BtnStyle.Dark, 8, 8, 140, 40, () => SetFollow(false), 13);
            followBtn = Ui.Button(strip, "FOLLOW SELECTED", BtnStyle.Dark, 154, 8, 160, 40, () => SetFollow(true), 13);
            wholeBtn.SetActive(true);
            string[] labels = { "PAUSE", "1x", "3x", "5x" };
            Color[] fills = { Theme.Panel, Theme.Green, Theme.Yellow, Theme.Red };
            speedBtns = new FtmButton[4];
            float bw = 96, x0 = CenterW / 2f - (4 * bw + 3 * 8) / 2f;
            for (int i = 0; i < 4; i++)
            {
                int sp = SpeedOf(i);
                var b = Ui.Button(strip, labels[i], BtnStyle.Dark, x0 + i * (bw + 8), 8, bw, 40, () => SetSpeed(sp), 16);
                b.customFill = fills[i];
                b.activeOutlineOnly = true;
                b.Apply();
                speedBtns[i] = b;
            }
            simBtn = Ui.Button(strip, "SIMULATE SESSION", BtnStyle.Yellow, CenterW - 8 - 170 - 8 - 190, 8, 190, 40, AskSimulate, 13);
            Ui.Button(strip, "RADIO HISTORY", BtnStyle.Dark, CenterW - 8 - 170, 8, 170, 40, () => RadioHistory(-1), 13);
        }

        /// <summary>Camera choice only: never changes speed, timing or driver instructions.</summary>
        void SetFollow(bool on)
        {
            if (on && selected < 0) return;
            following = on;
            follow.Show(on);
            if (on) { follow.root.SetAsLastSibling(); lightsBox.SetAsLastSibling(); }
            wholeBtn.SetActive(!on);
            followBtn.SetActive(on);
            follow.Refresh(selected, 1f, Lead);
        }

        void AskSimulate()
        {
            if (s.session >= Session.Race) return;
            var w = PausingWindow("SIMULATE " + Session.Names[s.session], 600, 100, "SIMULATE", win => { s.simulating = true; win.Close(); }, "CANCEL");
            Ui.Label(w.body, "The coordinator runs both of your cars for the rest of the session instantly. " + (Session.IsPractice(s) ? "Laps still count toward tyre knowledge." : "Your qualifying runs follow the coordinator's plan."), Theme.Body, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 100, TextAnchor.UpperLeft);
        }

        void SetSpeed(int sp)
        {
            s.speed = sp;
            RefreshHeader();
            RefreshList();
        }

        public void Select(int idx)
        {
            selected = idx;
            RefreshList();
            RefreshHeader();
            RefreshTrack();
        }

        // ================================================================ windows (pause while open, restore after)

        Window PausingWindow(string title, float width, float bodyH, string confirm = null, Action<Window> onConfirm = null, string cancel = "CLOSE")
        {
            int before = s.speed;
            s.speed = 0;
            var w = GameApp.I.OpenWindow(title, width, bodyH, confirm, onConfirm, cancel);
            w.onClosed += () => { s.speed = before; RefreshHeader(); };
            RefreshHeader();
            return w;
        }

        void PauseMenu()
        {
            var w = PausingWindow("PAUSED", 600, 110, "SAVE AND EXIT", win =>
            {
                s.speed = 0;
                RaceFlow.Persist(GameApp.I.Career, s, () => { GameApp.I.Career = null; GameApp.I.Show(new OpeningScreen()); });
            }, "RESUME");
            Ui.Label(w.body, "Save and Exit keeps the session exactly as it is: timing, laps, positions, tyres and your instructions. Returning to the career resumes it here.", Theme.Body, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 110, TextAnchor.UpperLeft);
        }

        void Details(int idx)
        {
            if (idx < 0) return;
            var car = s.cars[idx];
            bool own = car.mine >= 0;
            var lines = new List<string[]>();
            int pos = RaceSim.Order(s).IndexOf(idx) + 1;
            lines.Add(new[] { "Team", car.teamName });
            lines.Add(new[] { "Position", "P" + pos + " (started P" + car.grid + ")" });
            lines.Add(new[] { "Status", StatusLong(car) });
            lines.Add(new[] { "Current tyres", car.set >= 0 ? Tyre.Name[car.sets[car.set].compound] + (own ? "  (" + Mathf.RoundToInt((1 - Mathf.Min(1, car.sets[car.set].wear)) * 100) + "% life)" : "") : "--" });
            lines.Add(new[] { "Completed stops", car.stops.ToString() });
            lines.Add(new[] { "Last lap / best lap", RaceSim.Lap(car.lastLap) + "  /  " + RaceSim.Lap(car.bestLap) });
            lines.Add(new[] { "Gap ahead", GapText(idx, -1) });
            lines.Add(new[] { "Gap behind", GapText(idx, 1) });
            lines.Add(new[] { "Penalties", PenaltyText(idx) });
            if (own)
            {
                lines.Add(new[] { "Drive style", Style.Label[car.style] + (car.styleManual ? "  (Manual - you)" : "  (Auto - coordinator)") });
                lines.Add(new[] { "Pit timing", car.pitManual ? "Manual - you" : "Auto - coordinator" });
                lines.Add(new[] { "Next tyres", (car.nextSet >= 0 ? Tyre.Name[car.sets[car.nextSet].compound] : "Coordinator's choice") + (car.nextManual ? "  (Manual)" : "  (Auto)") });
                int stint = car.stops;
                lines.Add(new[] { "Planned pit window", stint < car.plan.Count - 1 ? "Around lap " + car.plan[stint].endLap : "No more stops planned" });
                lines.Add(new[] { "Fuel", "Running to the end (" + Mathf.RoundToInt(100f * (1 - (float)car.lap / s.totalLaps)) + "% load)" });
                lines.Add(new[] { "Car condition", car.status == 3 ? "Retired" : "OK" });
            }
            float bodyH = lines.Count * 40 + (own ? 60 : 30);
            var w = PausingWindow(car.name.ToUpperInvariant(), 900, bodyH);
            float y = 0;
            foreach (var l in lines) { LobbyKit.Line(w.body, ref y, w.bodyWidth, l[0], l[1], Theme.TextPrimary); y -= 4; }
            if (own)
                Ui.Button(w.body, "RETURN ALL TO COORDINATOR", BtnStyle.Red, 0, y + 6, 360, 44, () => { RaceSim.Issue(s, idx, "auto", 7); w.Close(); RefreshAll(); }, 14);
            else Ui.Label(w.body, "Rival team: public timing only. Their strategy is private.", Theme.Small, false, Theme.TextSecondary, 0, y + 4, w.bodyWidth, 24);
        }

        string PenaltyText(int idx)
        {
            var parts = new List<string>();
            foreach (var p in s.penalties.Where(x => x.car == idx))
            {
                if (!p.decided) parts.Add("Under investigation (" + p.reason.ToLowerInvariant() + ")");
                else if (!p.guilty) continue;
                else if (p.driveThrough) parts.Add("Drive-through" + (p.served ? " (served)" : " (to serve)"));
                else parts.Add(p.seconds + "s " + p.reason.ToLowerInvariant() + (p.served ? " (served)" : " (pending)"));
            }
            if (s.cars[idx].trackLimits > 0 && s.session == Session.Race) parts.Add(s.cars[idx].trackLimits + " track limits");
            return parts.Count == 0 ? "None" : string.Join(", ", parts);
        }

        string GapText(int idx, int dir)
        {
            var order = RaceSim.Order(s);
            int p = order.IndexOf(idx), q = p + dir;
            if (q < 0 || q >= order.Count || s.phase < 2) return "--";
            int ld1, ld2;
            float g1 = RaceSim.GapToLeader(s, t, idx, out ld1), g2 = RaceSim.GapToLeader(s, t, order[q], out ld2);
            if (ld1 != ld2) return "Lapped traffic";
            return Mathf.Abs(g1 - g2).ToString("0.0") + "s  (" + s.cars[order[q]].tla + ")";
        }

        public void RadioHistory(int filter)
        {
            var msgs = filter == 2 ? new List<RadioMsg>() : s.radio.Where(m => filter < 0 || s.cars[m.car].mine == filter).Reverse().ToList();
            float rowH = 56;
            int rows = filter == 2 ? s.control.Count : msgs.Count;
            var w = PausingWindow("RADIO HISTORY", 900, Mathf.Max(80, 56 + rows * rowH));
            string[] f = { "ALL", "DRIVER 1", "DRIVER 2", "RACE CONTROL" };
            for (int i = 0; i < 4; i++)
            {
                int v = i - 1;
                var b = Ui.Button(w.body, f[i], BtnStyle.Dark, i * 150, 0, i == 3 ? 180 : 140, 40, () => { w.Close(); RadioHistory(v); }, 14);
                b.SetActive(filter == v);
            }
            float y = 56;
            if (filter == 2)
            {
                if (s.control.Count == 0) Ui.Label(w.body, "No messages from race control yet.", Theme.Body, false, Theme.TextSecondary, 0, y, w.bodyWidth, 30);
                for (int k = s.control.Count - 1; k >= 0; k--)
                {
                    var m = s.control[k];
                    Color kc = m.kind == "red" || m.kind == "penalty" ? Theme.Red : m.kind == "weather" ? Theme.Hex("#4FC3F7") : m.kind == "info" ? Theme.TextSecondary : Theme.Yellow;
                    Ui.Box(w.body, Theme.RowA, 0, y, w.bodyWidth, rowH - 4);
                    Ui.Box(w.body, kc, 0, y, 4, rowH - 4);
                    Ui.Label(w.body, "RACE CONTROL" + (s.session == Session.Race ? "   •   Lap " + m.lap : "") + "   •   " + m.kind.ToUpperInvariant(), 12, true, kc, 14, y + 2, w.bodyWidth - 20, 20);
                    Ui.Ellipsis(w.body, m.text, Theme.Small, false, Theme.TextPrimary, 14, y + 22, w.bodyWidth - 24, 26);
                    y += rowH;
                }
                return;
            }
            if (msgs.Count == 0) Ui.Label(w.body, "No radio yet.", Theme.Body, false, Theme.TextSecondary, 0, y, w.bodyWidth, 30);
            foreach (var m in msgs)
            {
                var car = s.cars[m.car];
                Ui.Box(w.body, Theme.RowA, 0, y, w.bodyWidth, rowH - 4);
                Ui.Box(w.body, Theme.Hex(car.color), 0, y, 4, rowH - 4);
                Ui.Label(w.body, m.from + " > " + m.to + "   •   Lap " + m.lap + (m.suggestion ? "   •   SUGGESTION" : ""), 12, true, m.safety ? Theme.Yellow : Theme.TextSecondary, 14, y + 2, w.bodyWidth - 20, 20);
                Ui.Ellipsis(w.body, m.text, Theme.Small, false, Theme.TextPrimary, 14, y + 22, w.bodyWidth - 24, 26);
                y += rowH;
            }
        }

        // ================================================================ tick

        public void Tick(float realDt)
        {
            if (s.phase == 1) RaceSim.Lights(s, realDt);
            if (s.phase == 2 && s.speed > 0)
            {
                runner.acc += realDt * RaceSim.SimSecondsPer1x * s.speed;
                int steps = 0;
                while (runner.acc >= RaceSim.TickSeconds && steps < 400 && s.phase == 2)
                {
                    RaceSim.Step(s, t, RaceSim.TickSeconds);
                    runner.acc -= RaceSim.TickSeconds;
                    steps++;
                }
                if (steps >= 400) runner.acc = 0;
            }
            UpdateVisuals(s, realDt, Lead);
            RefreshTrack();
            if (following) follow.Refresh(selected, realDt, Lead);
            RefreshRaceControl(realDt);
            uiTimer -= realDt;
            if (uiTimer <= 0)
            {
                uiTimer = 0.15f;
                RefreshList();
                RefreshHeader();
                foreach (var p in panels) if (p != null) p.Refresh(realDt);
            }
            if (s.session < Session.Race)
            {
                if (s.simulating)
                {
                    int n = 0;
                    while (!s.sessionEnded && n < 3000) { RaceSim.Step(s, t, 0.25f); n++; }
                }
                if (s.sessionEnded && !leaving)
                {
                    leaving = true;
                    s.simulating = false;
                    RaceFlow.Persist(GameApp.I.Career, s, () => GameApp.I.Show(new SessionSummaryScreen(s)));
                }
                return;
            }
            if (s.phase == 3 && !s.committed && !committing)
            {
                committing = true;
                RaceFlow.Commit(s, () => GameApp.I.Show(new ResultsScreen(s)));
            }
        }

        bool committing;

        void RefreshAll()
        {
            RefreshList();
            RefreshHeader();
            RefreshTrack();
            foreach (var p in panels) if (p != null) p.Refresh(0);
        }

        public void PersistForPause()
        {
            if (s.committed) return;
            var c = GameApp.I.Career;
            if (c == null) return;
            c.activeRace = JsonUtility.ToJson(s);
            string err;
            SaveSystem.TrySave(c, out err);
        }

        public Track TrackData { get { return t; } }
    }
}
