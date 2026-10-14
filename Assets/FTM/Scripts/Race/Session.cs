using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// Practice (FP1-FP3) and knockout qualifying (Q1-Q3). Cars leave the garage for planned runs:
    /// pit lane, out lap, timed laps, in lap, back to the garage. Practice laps teach the team how the
    /// tyres hold up at this track; qualifying sets the grid.
    /// </summary>
    public static class Session
    {
        public const int FP1 = 0, FP2 = 1, FP3 = 2, Q1 = 3, Q2 = 4, Q3 = 5, Race = 6;
        public static readonly string[] Names = { "PRACTICE 1", "PRACTICE 2", "PRACTICE 3", "QUALIFYING Q1", "QUALIFYING Q2", "QUALIFYING Q3", "RACE" };
        public static readonly string[] Short = { "FP1", "FP2", "FP3", "Q1", "Q2", "Q3", "Race" };
        public static readonly float[] Length = { 3600, 3600, 3600, 1080, 900, 780, 0 };
        public static readonly string[] Programs = { "Familiarization", "Tyre Test", "Long Run", "Qualifying Prep" };

        public static bool IsQuali(RaceState s) { return s.session >= Q1 && s.session <= Q3; }
        public static bool IsPractice(RaceState s) { return s.session <= FP3; }
        public static float Elapsed(RaceState s) { return s.sessionLength - s.sessionLeft; }

        /// <summary>How many cars drop out at the end of Q1 and of Q2 (6 with 22 cars, 7 with 24).</summary>
        public static int Cut(RaceState s) { return Mathf.Max(0, (s.cars.Count - 10) / 2); }

        public static int Active(RaceState s) { return s.cars.Count(c => c.qOutSeg == 0); }

        // ------------------------------------------------------------------ setup

        public static void Begin(RaceState s, Track t, int session)
        {
            s.session = session;
            s.sessionLength = s.sessionLeft = Length[session];
            s.sessionEnded = false;
            s.phase = 2;
            s.flag = "GREEN";
            s.chequered = false;
            s.simulating = false;
            int teams = s.cars.Max(c => c.teamIndex) + 1;
            foreach (var car in s.cars)
            {
                double baseLap = Math.Floor(Math.Max(0, car.dist) / t.L) + 1;
                float box = t.BoxAt(car.teamIndex, teams);
                car.pitS = box;
                car.dist = baseLap * t.L + t.pitIn + box;
                car.lap = (int)Math.Floor(car.dist / t.L);
                car.lastCp = int.MaxValue / 2;     // sessions don't use race checkpoints
                car.status = 5;
                car.speed = 0;
                car.pitQueued = false; car.pitManual = false;
                car.outLap = false; car.lapValid = false;
                car.sBest = 0; car.sBestAt = 0; car.sLaps = 0;
                car.lastLap = 0;
                car.style = Style.Balanced; car.styleManual = false;
                car.issue = car.issue ?? "";
                car.overtaking = false;
                car.runs = Coordinator.SessionRuns(s, t, car);
            }
        }

        public static string TimeLeft(RaceState s) { return TimeLeftOf(s.sessionLeft); }

        public static string TimeLeftOf(float left)
        {
            left = Mathf.Max(0, left);
            int m = (int)(left / 60), sec = (int)(left % 60);
            return m.ToString("00") + ":" + sec.ToString("00");
        }

        // ------------------------------------------------------------------ garage / runs

        public static int PickSet(CarState car, int compound, bool wantNew)
        {
            int best = -1;
            for (int i = 0; i < car.sets.Count; i++)
            {
                var st = car.sets[i];
                if (st.compound != compound || st.wear >= 0.9f) continue;
                if (wantNew && st.used) continue;
                if (!wantNew && !st.used) continue;
                if (best < 0 || st.wear < car.sets[best].wear) best = i;
            }
            if (best < 0)   // fall back to anything of that compound
                for (int i = 0; i < car.sets.Count; i++)
                    if (car.sets[i].compound == compound && car.sets[i].wear < 0.9f && (best < 0 || car.sets[i].wear < car.sets[best].wear)) best = i;
            return best;
        }

        public static void SendOut(RaceState s, int i, int program, int compound, bool wantNew, int laps, float fuel)
        {
            var car = s.cars[i];
            if (car.status != 5 || car.qOutSeg > 0 || s.sessionLeft <= 0) return;
            int set = car.nextSet >= 0 ? car.nextSet : PickSet(car, compound, wantNew);
            if (set < 0) set = PickSet(car, Tyre.Medium, false);
            if (set < 0) return;
            if (!car.sets[set].used) car.setsUsedNew++;
            car.set = set;
            car.sets[set].used = true;
            car.nextSet = -1; car.nextManual = false;
            car.tyreLapAge = 0;
            car.fuel = fuel;
            car.program = program;
            car.runLaps = Mathf.Max(1, laps);
            car.status = 1;
            car.pitQueued = false;
            car.outLap = true;
            car.lapValid = false;
            car.style = program == 3 || IsQuali(s) ? Style.Push : Style.Balanced;
            if (car.mine >= 0)
                RaceSim.Say(s, i, "Coordinator", car.name, Programs[program] + " run: " + Tyre.Name[car.sets[set].compound].ToLowerInvariant() + "s, " + car.runLaps + (car.runLaps == 1 ? " timed lap." : " laps."), false);
        }

        /// <summary>Coordinator: send cars out when their next planned run is due.</summary>
        public static void GarageLogic(RaceState s, Track t, int i)
        {
            var car = s.cars[i];
            if (car.status != 5 || car.qOutSeg > 0 || s.sessionLeft <= 0 || car.pitManual) return;
            var run = car.runs.FirstOrDefault(r => !r.done);
            if (run == null || Elapsed(s) < run.startAt) return;
            // damp but not wet enough for inters: practice runs wait in the garage for the track to dry
            if (IsPractice(s) && s.wetness > 0.04f && RaceControl.BestCompound(s, s.wetness) < 0 && s.sessionLeft > 600) return;
            run.done = true;
            // need time for the out lap plus the timed laps to start before the flag
            if (s.sessionLeft < s.estLap * 1.2f + 20) return;
            if (IsQuali(s) && run.startAt > 300 && SafelyThrough(s, i)) return;   // banker lap is enough: save the tyres
            // a wet track (or rain the forecast says is about to arrive) means intermediates or wets
            int comp = run.compound;
            bool fresh = run.newSet;
            float wetAhead = Mathf.Max(s.wetness, RaceControl.Forecast(s, 5) > 0.7f ? 0.15f : 0f);
            int wetComp = RaceControl.BestCompound(s, wetAhead);
            if (wetComp >= 0) { comp = wetComp; fresh = false; }
            SendOut(s, i, run.program, comp, fresh, run.laps, run.fuel);
        }

        static bool SafelyThrough(RaceState s, int i)
        {
            if (s.session == Q3) return false;
            var order = Order(s);
            int pos = order.IndexOf(i) + 1;
            int safe = Active(s) - Cut(s) - 4;
            return SegBest(s, s.cars[i]) > 0 && pos <= safe;
        }

        // ------------------------------------------------------------------ laps

        public static float SegBest(RaceState s, CarState c)
        {
            return s.session == Q1 ? c.q1 : s.session == Q2 ? c.q2 : s.session == Q3 ? c.q3 : c.sBest;
        }

        static double SegBestAt(RaceState s, CarState c)
        {
            return s.session == Q1 ? c.q1At : s.session == Q2 ? c.q2At : s.session == Q3 ? c.q3At : c.sBestAt;
        }

        /// <summary>Called when a car crosses the line during practice or qualifying.</summary>
        public static void OnLap(RaceState s, Track t, int i)
        {
            var car = s.cars[i];
            var set = car.set >= 0 ? car.sets[car.set] : null;
            if (car.status != 0) { car.lapValid = false; return; }      // crossing in the pit lane

            if (car.outLap)
            {
                car.outLap = false;
                car.lapValid = s.sessionLeft > 0;                        // no new attempt after the flag
                car.lapStartWear = set != null ? set.wear : 0;
            }
            else if (car.lapValid)
            {
                float lt = car.lastLap;
                car.sLaps++;
                // track limits: the lap still happened (tyre data counts) but the time is deleted
                bool deleted = s.rng.Chance(0.02f * (1f + Mathf.Max(0, 85f - car.dCons) / 25f) * (car.style == Style.Push ? 1.5f : 1f));
                if (deleted)
                {
                    RaceControl.Msg(s, "Lap time deleted for " + car.tla + " - track limits", "info");
                    if (car.mine >= 0) RaceSim.Say(s, i, "Coordinator", car.name, "That lap's been deleted - track limits.", false);
                }
                else
                {
                    if (car.sBest <= 0 || lt < car.sBest) { car.sBest = lt; car.sBestAt = s.time; }
                    if (s.session == Q1 && (car.q1 <= 0 || lt < car.q1)) { car.q1 = lt; car.q1At = s.time; }
                    if (s.session == Q2 && (car.q2 <= 0 || lt < car.q2)) { car.q2 = lt; car.q2At = s.time; }
                    if (s.session == Q3 && (car.q3 <= 0 || lt < car.q3)) { car.q3 = lt; car.q3At = s.time; }
                }
                // dry tyre data only means something on a (nearly) dry track
                if (set != null && set.compound <= Tyre.Hard && s.wetness < 0.1f)
                {
                    car.knowLaps[set.compound] += 1;
                    car.knowWear[set.compound] += set.wear - car.lapStartWear;
                }
                car.lapStartWear = set != null ? set.wear : 0;
                car.runLaps--;
                if (car.mine >= 0 && IsQuali(s) && !deleted)
                {
                    int pos = Order(s).IndexOf(i) + 1;
                    RaceSim.Say(s, i, "Coordinator", car.name, RaceSim.Lap(lt) + " - P" + pos + ".", false);
                }
            }
            if (s.sessionLeft <= 0 || car.runLaps <= 0 || (set != null && set.wear > 0.85f)) { car.pitQueued = true; car.lapValid = false; }
            // wrong tyres for the conditions: come in and change
            if (set != null && !car.pitQueued)
            {
                bool slickInWet = Tyre.Dry(set.compound) && s.wetness > 0.12f;
                bool wetOnDry = !Tyre.Dry(set.compound) && s.wetness < 0.03f;
                if (slickInWet || wetOnDry)
                {
                    car.pitQueued = true; car.lapValid = false;
                    if (car.mine >= 0) RaceSim.Say(s, i, "Coordinator", car.name, slickInWet ? "Box, box - it's too wet for slicks." : "Box - the track is dry, these will overheat.", false);
                }
            }

            // mistakes: a crash ends this car's session (it is repaired for the next one)
            float style = car.style == Style.Push ? 1.8f : 1f;
            float wet = 1f + 3f * s.wetness + (set != null && Tyre.Dry(set.compound) ? 10f * Mathf.Max(0, s.wetness - 0.08f) : 0f);
            double crash = 0.0004 * (1 + Mathf.Max(0, 88f - (car.dBrake + car.dCons) / 2f) / 8f) * style * wet;
            if (s.rng.Chance(crash))
            {
                car.status = 3;
                car.speed = 0;
                car.issue = "Crashed in " + Short[s.session];
                if (car.mine >= 0) RaceSim.Say(s, i, car.name, "Coordinator", "I've crashed. Sorry, the car is stuck.", true);
                RaceControl.Incident(s, t, i, true);
            }
        }

        /// <summary>Practice / qualifying order: best lap of the session (no time = bottom), eliminated cars last.</summary>
        public static List<int> Order(RaceState s)
        {
            var idx = Enumerable.Range(0, s.cars.Count).ToList();
            idx.Sort((a, b) =>
            {
                var x = s.cars[a]; var y = s.cars[b];
                if (IsQuali(s) && x.qOutSeg != y.qOutSeg)
                {
                    if (x.qOutSeg == 0) return -1;
                    if (y.qOutSeg == 0) return 1;
                    return y.qOutSeg.CompareTo(x.qOutSeg);        // Q2 eliminations above Q1 eliminations
                }
                float bx = IsQuali(s) && x.qOutSeg > 0 ? (x.qOutSeg == 2 ? x.q2 : x.q1) : SegBest(s, x);
                float by = IsQuali(s) && y.qOutSeg > 0 ? (y.qOutSeg == 2 ? y.q2 : y.q1) : SegBest(s, y);
                if ((bx > 0) != (by > 0)) return bx > 0 ? -1 : 1;
                if (bx <= 0) return x.grid.CompareTo(y.grid) != 0 ? x.grid.CompareTo(y.grid) : a.CompareTo(b);
                int cmp = bx.CompareTo(by);
                if (cmp != 0) return cmp;
                double ax = IsQuali(s) && x.qOutSeg > 0 ? 0 : SegBestAt(s, x), ay = IsQuali(s) && y.qOutSeg > 0 ? 0 : SegBestAt(s, y);
                return ax.CompareTo(ay);                          // equal times: who set it first
            });
            return idx;
        }

        /// <summary>Time to beat to get above the elimination line (0 if not applicable).</summary>
        public static float CutoffTime(RaceState s)
        {
            if (!IsQuali(s) || s.session == Q3) return 0;
            var order = Order(s);
            int line = Active(s) - Cut(s);   // last safe position
            if (line - 1 >= order.Count) return 0;
            var c = s.cars[order[line - 1]];
            return SegBest(s, c);
        }

        // ------------------------------------------------------------------ end of a session

        public static void Finish(RaceState s)
        {
            if (IsQuali(s) && s.session != Q3)
            {
                var order = Order(s).Where(i => s.cars[i].qOutSeg == 0).ToList();
                int keep = order.Count - Cut(s);
                for (int p = keep; p < order.Count; p++) s.cars[order[p]].qOutSeg = s.session == Q1 ? 1 : 2;
                if (s.session == Q1)
                {
                    float fastest = s.cars.Where(c => c.q1 > 0).Select(c => c.q1).DefaultIfEmpty(0).Min();
                    foreach (var c in s.cars)
                        if (fastest > 0 && (c.q1 <= 0 || c.q1 > fastest * 1.07f))
                            c.issue = (c.issue.Length > 0 ? c.issue + "; " : "") + "Outside 107% in Q1 - allowed to start by the stewards";
                }
            }
            s.sessionEnded = true;
            s.speed = 0;
        }

        /// <summary>Q3 order, then Q2 eliminations, then Q1 eliminations (equal times: who set it first).</summary>
        public static void MakeGrid(RaceState s)
        {
            var order = s.cars.Select((c, i) => i).ToList();
            Func<CarState, int> group = c => c.qOutSeg == 0 ? 0 : c.qOutSeg == 2 ? 1 : 2;
            Func<CarState, float> time = c => { float v = c.qOutSeg == 0 ? c.q3 : c.qOutSeg == 2 ? c.q2 : c.q1; return v > 0 ? v : 9999f; };
            Func<CarState, double> at = c => c.qOutSeg == 0 ? c.q3At : c.qOutSeg == 2 ? c.q2At : c.q1At;
            order = order.OrderBy(i => group(s.cars[i])).ThenBy(i => time(s.cars[i])).ThenBy(i => at(s.cars[i])).ToList();
            s.gridOrder.Clear();
            for (int p = 0; p < order.Count; p++) { s.cars[order[p]].grid = p + 1; s.gridOrder.Add(order[p]); }
        }

        /// <summary>Puts every car on its grid slot for the race (tyre wear from the weekend is kept).</summary>
        public static void StartRace(RaceState s, Track t)
        {
            MakeGrid(s);
            s.session = Race;
            s.phase = 0;
            s.sessionEnded = false;
            s.chequered = false;
            s.flag = "GREEN";
            s.firstCross.Clear();
            s.fastestLap = 0; s.fastestCar = -1;
            s.speed = 1;
            s.radio.Clear();
            s.commands.Clear();
            s.control.Clear();
            s.penalties.Clear();
            s.scMode = RaceControl.None; s.scIn = false; s.scInPit = false; s.yellowTimer = 0; s.redRainTimer = 0;
            // wet-weather sets used in practice are replaced for the race (extra allocation after a wet weekend)
            foreach (var car in s.cars)
                foreach (var ts in car.sets)
                    if (!Tyre.Dry(ts.compound) && ts.wear > 0.25f) { ts.wear = 0; ts.used = false; }
            int startWet = RaceControl.BestCompound(s, Mathf.Max(s.wetness, RaceControl.Forecast(s, 5) > 0.8f ? 0.12f : 0f));
            foreach (var car in s.cars)
            {
                car.status = 0;
                car.dist = -(5 + (car.grid - 1) * 8);
                car.lap = 0;
                car.speed = 0;
                car.fuel = 1f;
                car.lastCp = -1; car.lastCpTime = 0;
                car.lastLap = 0; car.bestLap = 0; car.lapStart = 0; car.lapCrossTime = 0;
                car.sectors = new float[3]; car.lastSectors = new float[3]; car.curSector = 0; car.sectorStart = 0;
                car.stops = 0; car.pitQueued = false; car.pitManual = false; car.nextManual = false; car.nextSet = -1;
                car.style = Style.Balanced; car.styleManual = false;
                car.outLap = false; car.lapValid = false;
                car.overtaking = false; car.slowTimer = 0;
                car.finishTime = 0; car.retireReason = ""; car.ledLap = false; car.bestPos = 99;
                car.trackLimits = 0; car.usedWet = false; car.driveThroughPending = false; car.penaltyAdded = 0; car.penaltyServing = 0;
                Coordinator.Plan(s, car, -1);
                RaceSim.FitStartSet(car);
                if (startWet >= 0) RaceSim.FitWetStart(car, startWet);
            }
        }

        // ------------------------------------------------------------------ tyre knowledge

        /// <summary>Confidence (0-1) in this team's estimate for a compound, from laps run by both cars.</summary>
        public static float Confidence(RaceState s, CarState car, int compound)
        {
            float laps = s.cars.Where(c => c.teamId == car.teamId).Sum(c => c.knowLaps[compound]);
            return Mathf.Clamp01(laps / 14f);
        }

        /// <summary>The team's estimate of a tyre's life (laps to ~85% wear at balanced pace).</summary>
        public static float EstimatedLife(RaceState s, CarState car, int compound)
        {
            float truth = Coordinator.Life(s, compound);
            float conf = Confidence(s, car, compound);
            return truth * (1f + car.lifeErr[compound] * (1f - conf));
        }
    }
}
