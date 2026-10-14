using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>One of your two driver panels on the race screen (identity, sectors, tyres, commands, radio, pit).</summary>
    public class DriverPanel
    {
        readonly RaceScreen screen;
        readonly RaceState s;
        readonly int idx;
        const float W = 360, H = 476, In = 16, IW = 328;

        Text status, timing, tyreLabel, modeLabel, queuedLabel;
        Text[] sectorTexts = new Text[3];
        RectTransform tyreBarFill;
        Image tyreBarImg;
        FtmButton[] tyreBtns = new FtmButton[5];
        FtmButton[] styleBtns = new FtmButton[3];
        FtmButton confirm, cancel, pitBtn, modeBtn;
        RectTransform radioArea;
        int staged = -1;

        // radio display queue (each popup stays at least 4 seconds of real time)
        int seen;
        readonly Queue<int> waiting = new Queue<int>();
        readonly List<int> showing = new List<int>();
        float holdLeft;

        public DriverPanel(RaceScreen scr, Transform root, int carIndex, float x, float y)
        {
            screen = scr; s = scr.s; idx = carIndex;
            var p = Ui.Panel(root, x, y, W, H, null, Theme.TopBar);
            if (idx < 0) { Ui.Label(p, "No driver", Theme.Body, true, Theme.TextSecondary, 0, 0, W, H, TextAnchor.MiddleCenter); return; }
            var car = s.cars[idx];
            Color tc = Theme.Hex(car.color);
            seen = s.radio.Count;
            // show the latest exchange straight away when (re)opening the screen
            var mineMsgs = Enumerable.Range(0, s.radio.Count).Where(i => s.radio[i].car == idx).ToList();
            foreach (var m in mineMsgs.Skip(Mathf.Max(0, mineMsgs.Count - 2))) showing.Add(m);

            // 1) identity and timing (76)
            Ui.Box(p, tc, In, In, 4, 76);
            Ui.Ellipsis(p, "D" + (car.mine + 1) + "  " + car.name.ToUpperInvariant(), 20, true, Theme.TextPrimary, In + 10, In, IW - 80, 26);
            Ui.Icon(p, PixelArt.Portrait(tc, car.mine + 1), In + IW - 56, In, 56, 56);
            status = Ui.Label(p, "", Theme.Small, true, Theme.Yellow, In + 10, In + 26, IW - 80, 22);
            timing = Ui.Label(p, "", 14, false, Theme.TextSecondary, In + 10, In + 50, IW - 70, 22);

            // 2) sectors (36)
            float sw = (IW - 12) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var b = Ui.Box(p, Theme.EmptyBar, In + i * (sw + 6), 98, sw, 36);
                Ui.Label(b.transform, "S" + (i + 1), 11, true, Theme.TextSecondary, 6, 0, 30, 36);
                sectorTexts[i] = Ui.Label(b.transform, "--.---", 14, true, Theme.TextPrimary, 26, 0, sw - 32, 36, TextAnchor.MiddleRight);
            }

            // 3) tyre condition (32)
            tyreLabel = Ui.Label(p, "", 14, true, Theme.TextPrimary, In, 140, IW, 20);
            var bar = Ui.Box(p, Theme.EmptyBar, In, 164, IW, 8);
            tyreBarImg = Ui.Box(bar.transform, Color.white, 0, 0, IW, 8);
            tyreBarFill = tyreBarImg.rectTransform;

            // 4) next tyre buttons (32)
            float tw = (IW - 4 * 6) / 5f;
            for (int c = 0; c < 5; c++)
            {
                int comp = c;
                var b = Ui.Button(p, Tyre.Letter[c], BtnStyle.Dark, In + c * (tw + 6), 178, tw, 32, () => PickTyre(comp), 14);
                b.customFill = Theme.Hex(Tyre.Hex[c]);
                b.Apply();
                tyreBtns[c] = b;
            }

            // 5) driving mode label (20)
            Ui.Label(p, "DRIVING MODE", 12, true, Theme.TextSecondary, In, 216, 140, 20);
            queuedLabel = Ui.Label(p, "", 11, true, Theme.Yellow, In + 100, 216, 120, 20);
            modeBtn = Ui.Button(p, "AUTO", BtnStyle.Dark, In + IW - 110, 216, 110, 20, ReturnStyle, 11);

            // 6) driving mode buttons (32)
            float bw = (IW - 12) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int st = i;
                styleBtns[i] = Ui.Button(p, Style.Label[i].ToUpperInvariant(), BtnStyle.Dark, In + i * (bw + 6), 242, bw, 32, () => { staged = st; Refresh(0); }, 13);
            }

            // 7) confirm row (32)
            confirm = Ui.Button(p, "CONFIRM", BtnStyle.Red, In, 280, (IW - 6) / 2f, 32, ConfirmStyle, 13);
            cancel = Ui.Button(p, "CANCEL", BtnStyle.Dark, In + (IW - 6) / 2f + 6, 280, (IW - 6) / 2f, 32, () => { staged = -1; Refresh(0); }, 13);

            // 8) team radio (96)
            var radio = Ui.Box(p, Theme.Panel, In, 318, IW, 96, "Radio");
            radioArea = radio.rectTransform;

            // 9) pit / send out (40)
            pitBtn = Ui.Button(p, "BOX THIS LAP", BtnStyle.Red, In, 420, IW, 40, PitPressed, 16);
        }

        CarState Car { get { return s.cars[idx]; } }

        public void Refresh(float realDt)
        {
            if (idx < 0) return;
            var car = Car;
            var t = screen.TrackData;
            int pos = RaceSim.Order(s).IndexOf(idx) + 1;
            string limited = s.session == Session.Race && car.status == 0 && s.phase == 2
                ? (s.scMode == RaceControl.SC ? "Safety Car - limited by race control" : s.scMode == RaceControl.VSC ? "VSC - limited by race control" : null)
                : s.session == Session.Race && s.scMode == RaceControl.Red && car.status < 3 ? "Red flag - stopped, free tyre change" : null;
            status.text = "P" + pos + "  •  " + (limited ?? RaceScreen.StatusLong(car));
            status.color = limited != null ? Theme.Yellow : Theme.Yellow;
            float pen = RaceControl.PendingSeconds(s, idx);
            if (s.session == Session.Race && car.status < 3 && (pen > 0 || RaceControl.PendingDriveThrough(s, idx)))
            {
                status.text += RaceControl.PendingDriveThrough(s, idx) ? "  •  DRIVE-THROUGH" : "  •  +" + pen.ToString("0") + "s PEN";
                status.color = Theme.Red;
            }
            float best = s.session < Session.Race ? Session.SegBest(s, car) : car.bestLap;
            timing.text = "BEST " + RaceSim.Lap(best) + "   LAST " + RaceSim.Lap(car.lastLap);
            for (int i = 0; i < 3; i++) sectorTexts[i].text = car.lastSectors[i] > 0 ? car.lastSectors[i].ToString("0.000") : "--.---";

            var set = car.set >= 0 ? car.sets[car.set] : null;
            if (set != null)
            {
                float life = Mathf.Clamp01(1f - set.wear);
                tyreLabel.text = Tyre.Name[set.compound].ToUpperInvariant() + "   " + Mathf.RoundToInt(life * 100) + "%   (" + Mathf.FloorToInt(car.tyreLapAge) + " laps)";
                tyreBarFill.sizeDelta = new Vector2(IW * life, 8);
                tyreBarImg.color = Theme.Hex(Tyre.Hex[set.compound]);
            }
            bool racing = car.status == 0 || car.status == 1;
            int nextComp = car.nextSet >= 0 ? car.sets[car.nextSet].compound : -1;
            for (int c = 0; c < 5; c++)
            {
                var b = tyreBtns[c];
                bool isNext = c == nextComp;
                b.SetLabel(isNext ? Tyre.Letter[c] + " NEXT" : Tyre.Letter[c]);
                b.outlineOverride = isNext ? (Color?)Color.white : null;
                int cc = c;
                b.SetEnabled(car.sets.Any(x => x.compound == cc) && (car.status < 2 || car.status == 5) && (s.session == Session.Race || car.qOutSeg == 0));
            }

            // drive style
            bool pending = RaceSim.HasPending(s, idx, "style");
            for (int i = 0; i < 3; i++)
            {
                var b = styleBtns[i];
                b.SetActive(car.style == i);
                b.outlineOverride = (staged == i && staged != car.style) ? (Color?)Theme.Yellow : null;
                b.SetEnabled(car.status != 3 && car.status != 4);
            }
            confirm.SetEnabled(staged >= 0 && staged != car.style && !pending && car.status != 3 && car.status != 4);
            cancel.SetEnabled(staged >= 0);
            modeBtn.SetLabel(car.styleManual ? "MANUAL > AUTO" : "AUTO");
            modeBtn.SetEnabled(car.styleManual && !RaceSim.HasPending(s, idx, "auto"));
            queuedLabel.text = (pending || s.commands.Any(x => x.car == idx && x.status == "pending")) && s.speed == 0 ? "Queued while paused" : pending ? "Sending..." : "";

            // pit button
            if (s.session < Session.Race) { SessionButton(car); RefreshRadio(realDt); return; }
            if (car.status == 3) { pitBtn.SetLabel("RETIRED"); pitBtn.SetEnabled(false); }
            else if (car.status == 4) { pitBtn.SetLabel("FINISHED"); pitBtn.SetEnabled(false); }
            else if (s.scMode == RaceControl.Red) { pitBtn.SetLabel("RED FLAG - PICK TYRES ABOVE"); pitBtn.SetEnabled(false); }
            else if (s.phase < 2) { pitBtn.SetLabel("ON THE GRID"); pitBtn.SetEnabled(false); }
            else if (car.status == 1) { pitBtn.SetLabel(car.pitQueued ? "PIT ENTRY" : "PIT EXIT"); pitBtn.SetEnabled(false); }
            else if (car.status == 2) { pitBtn.SetLabel("PIT SERVICE"); pitBtn.SetEnabled(false); }
            else if (car.pitQueued || RaceSim.HasPending(s, idx, "pit"))
            {
                pitBtn.SetLabel(RaceSim.PastPitDecision(s, car) ? "CANCEL PIT (NEXT LAP)" : "CANCEL PIT");
                pitBtn.style = BtnStyle.Dark;
                pitBtn.SetEnabled(!RaceSim.HasPending(s, idx, "cancelpit"));
            }
            else
            {
                pitBtn.SetLabel(RaceSim.PastPitDecision(s, car) ? "BOX NEXT LAP" : "BOX THIS LAP");
                pitBtn.style = BtnStyle.Red;
                pitBtn.SetEnabled(s.totalLaps - car.lap >= 1 && !s.chequered);
            }
            pitBtn.Apply();
            RefreshRadio(realDt);
        }

        void RefreshRadio(float realDt)
        {
            for (int i = seen; i < s.radio.Count; i++)
                if (s.radio[i].car == idx) waiting.Enqueue(i);
            seen = s.radio.Count;
            holdLeft -= realDt;
            bool changed = false;
            // safety messages jump the queue
            if (waiting.Count > 0)
            {
                var list = waiting.ToList();
                int safe = list.FindIndex(m => s.radio[m].safety);
                if (safe > 0) { var x = list[safe]; list.RemoveAt(safe); list.Insert(0, x); waiting.Clear(); foreach (var m in list) waiting.Enqueue(m); }
            }
            while (waiting.Count > 0 && (holdLeft <= 0 || s.radio[waiting.Peek()].safety && holdLeft < 3f))
            {
                showing.Add(waiting.Dequeue());
                if (showing.Count > 2) showing.RemoveAt(0);
                holdLeft = 4f;
                changed = true;
                if (waiting.Count > 0 && holdLeft > 0) break;
            }
            if (!changed && radioArea.childCount > 0) return;
            Ui.Clear(radioArea);
            float y = 2;
            if (showing.Count == 0) Ui.Label(radioArea, "Team radio", 12, false, Theme.TextSecondary, 8, 4, IW - 16, 20);
            foreach (int mi in showing)
            {
                var m = s.radio[mi];
                Ui.Box(radioArea, Theme.Hex(Car.color), 4, y + 2, 3, 42);
                Ui.Label(radioArea, m.from + " > " + m.to + "  •  L" + m.lap + (m.suggestion ? "  •  SUGGESTION" : ""), 11, true, m.safety ? Theme.Yellow : Theme.TextSecondary, 12, y, IW - 20, 16);
                var tx = Ui.Label(radioArea, m.text, 13, false, Theme.TextPrimary, 12, y + 15, IW - 20, 30, TextAnchor.UpperLeft);
                tx.verticalOverflow = VerticalWrapMode.Truncate;
                y += 47;
            }
        }

        void SessionButton(CarState car)
        {
            bool pendingOut = RaceSim.HasPending(s, idx, "sendout");
            if (car.qOutSeg > 0) { pitBtn.SetLabel("ELIMINATED"); pitBtn.SetEnabled(false); }
            else if (car.status == 3) { pitBtn.SetLabel("STOPPED"); pitBtn.SetEnabled(false); }
            else if (car.status == 5)
            {
                pitBtn.SetLabel(pendingOut ? "SENDING OUT..." : "SEND OUT");
                pitBtn.style = BtnStyle.Green;
                pitBtn.SetEnabled(!pendingOut && s.sessionLeft > s.estLap * 1.1f && !s.sessionEnded);
            }
            else if (car.status == 1 || car.status == 2) { pitBtn.SetLabel(car.pitQueued ? "PIT ENTRY" : "PIT EXIT"); pitBtn.SetEnabled(false); }
            else if (car.pitQueued) { pitBtn.SetLabel("IN LAP"); pitBtn.SetEnabled(false); }
            else
            {
                pitBtn.SetLabel("RETURN TO GARAGE");
                pitBtn.style = BtnStyle.Red;
                pitBtn.SetEnabled(!RaceSim.HasPending(s, idx, "garage"));
            }
            pitBtn.Apply();
        }

        static readonly int[] ProgramTyre = { Tyre.Hard, Tyre.Medium, Tyre.Medium, Tyre.Soft };

        void SendOutWindow()
        {
            var car = Car;
            if (Session.IsQuali(s))
            {
                int wc = RaceControl.BestCompound(s, s.wetness);
                int qc = car.nextSet >= 0 ? car.sets[car.nextSet].compound : wc >= 0 ? wc : Tyre.Soft;
                RaceSim.Issue(s, idx, "sendout", 30 + qc);
                Refresh(0);
                return;
            }
            string[] about = { "4 laps to learn the track and set-up", "5 laps measuring tyre wear", "10 laps on high fuel - best tyre data", "3 low-fuel laps on new softs" };
            var w = GameApp.I.OpenWindow("SEND OUT  •  " + car.name.ToUpperInvariant(), 600, 4 * 66 + 40, null, null, "CANCEL");
            int nextComp = car.nextSet >= 0 ? car.sets[car.nextSet].compound : -1;
            Ui.Label(w.body, "Choose the programme. " + (nextComp >= 0 ? "Tyres: your chosen " + Tyre.Name[nextComp].ToLowerInvariant() + " set." : "Tyres: the programme's usual compound."), Theme.Small, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 30);
            for (int i = 0; i < 4; i++)
            {
                int prog = i;
                int wetC = RaceControl.BestCompound(s, s.wetness);
                int comp = nextComp >= 0 ? nextComp : wetC >= 0 ? wetC : ProgramTyre[i];
                var b = Ui.Button(w.body, Session.Programs[i].ToUpperInvariant() + "   •   " + about[i], BtnStyle.Dark, 0, 40 + i * 66, w.bodyWidth, 58, () =>
                {
                    RaceSim.Issue(s, idx, "sendout", prog * 10 + comp);
                    w.Close();
                    Refresh(0);
                }, 14);
            }
        }

        void ConfirmStyle()
        {
            if (staged < 0 || staged == Car.style) return;
            RaceSim.Issue(s, idx, "style", staged);
            staged = -1;
            Refresh(0);
        }

        void ReturnStyle() { RaceSim.Issue(s, idx, "auto", 4); Refresh(0); }

        void PitPressed()
        {
            var car = Car;
            if (s.session < Session.Race)
            {
                if (car.status == 5) SendOutWindow();
                else if (car.status == 0 && !car.pitQueued) { RaceSim.Issue(s, idx, "garage", 0); Refresh(0); }
                return;
            }
            if (car.status != 0) return;
            if (car.pitQueued || RaceSim.HasPending(s, idx, "pit")) RaceSim.Issue(s, idx, "cancelpit", 0);
            else RaceSim.Issue(s, idx, "pit", 0);
            Refresh(0);
        }

        void PickTyre(int compound)
        {
            var car = Car;
            var sets = Enumerable.Range(0, car.sets.Count).Where(i => car.sets[i].compound == compound).ToList();
            var w = GameApp.I.OpenWindow("NEXT TYRES  •  " + car.name.ToUpperInvariant(), 600, 40 + sets.Count * 60, null, null, "CANCEL");
            Ui.Label(w.body, "Choose the set for " + car.name + "'s next stop. It does not make the driver pit.", Theme.Small, false, Theme.TextSecondary, 0, 0, w.bodyWidth, 30);
            float y = 40;
            foreach (int si in sets)
            {
                var set = car.sets[si];
                bool fitted = si == car.set;
                string label = Tyre.Name[compound] + "  •  " + (set.used ? Mathf.RoundToInt(Mathf.Min(1, set.wear) * 100) + "% worn" : "New") + (fitted ? "  •  fitted now" : "") + (si == car.nextSet ? "  •  NEXT" : "");
                int setIndex = si;
                var b = Ui.Button(w.body, label, BtnStyle.Dark, 0, y, w.bodyWidth, 52, () =>
                {
                    w.Close();
                    ConfirmTyre(setIndex);
                }, 16);
                b.SetEnabled(!fitted && set.wear < 0.95f);
                y += 60;
            }
        }

        void ConfirmTyre(int setIndex)
        {
            var car = Car;
            var set = car.sets[setIndex];
            int cur = car.set >= 0 ? car.sets[car.set].compound : -1;
            int usedCount = 0;
            for (int c = 0; c < 3; c++) if (((car.usedCompounds | (1 << set.compound)) & (1 << c)) != 0) usedCount++;
            var w = GameApp.I.OpenWindow("CONFIRM NEXT TYRES", 600, 120, "CONFIRM", win =>
            {
                RaceSim.Issue(s, idx, "tyre", setIndex);
                win.Close();
                Refresh(0);
            });
            Ui.Label(w.body, car.name + ": " + Tyre.Name[set.compound] + " (" + (set.used ? Mathf.RoundToInt(set.wear * 100) + "% worn" : "new") + ") at the next stop.", Theme.Body, true, Theme.TextPrimary, 0, 0, w.bodyWidth, 30);
            if (s.scMode == RaceControl.Red)
                Ui.Label(w.body, "Red flag: this set is fitted for free before the restart.", Theme.Small, true, Theme.Green, 0, 40, w.bodyWidth, 60, TextAnchor.UpperLeft);
            else if (!car.usedWet && Tyre.Dry(set.compound) && usedCount < 2 && set.compound == cur)
                Ui.Label(w.body, "Warning: this keeps " + car.name + " on one dry compound. Two different dry compounds are required in a dry race.", Theme.Small, true, Theme.Yellow, 0, 40, w.bodyWidth, 60, TextAnchor.UpperLeft);
        }
    }
}
