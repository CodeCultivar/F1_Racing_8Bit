using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// The race simulation. Every team uses the same physics and rules; ratings only change how fast a
    /// car can go through each part of the lap, how fast tyres wear and how often mistakes happen.
    /// Nothing is scripted: overtakes need track space, pace and a completed move.
    /// </summary>
    public static class RaceSim
    {
        public const float TickSeconds = 0.1f;        // fixed simulation step
        public const float SimSecondsPer1x = 6f;      // 1x is ~6x real time (a full race is ~15 minutes)
        const float BaseMult = 0.955f;
        const float GridSpacing = 8f;

        static GameConfig Cfg { get { return ConfigLoader.Config; } }

        // ===================================================================== setup

        public static string CannotStartReason(CareerSave c)
        {
            if (Rules.NextEvent(c) == null) return Rules.Rounds == 0 ? "Calendar not configured" : "Season complete";
            var e = Rules.NextEvent(c);
            if (Track.Load(e.trackId) == null) return "Track data missing for " + e.grandPrix;
            if (!c.drivers[0].filled || !c.drivers[1].filled) return "Sign 2 drivers first";
            if (!c.coordinator.filled) return "Sign a coordinator first";
            return null;
        }

        static int Bit(int compound) { return 1 << compound; }

        public static RaceState Create(CareerSave c)
        {
            var e = Rules.NextEvent(c);
            var track = Track.Load(e.trackId);
            var s = new RaceState
            {
                season = c.season, round = c.nextRound, grandPrix = e.grandPrix, circuit = e.circuit, trackId = e.trackId,
                totalLaps = Mathf.Max(1, e.laps), pitSpeedKmh = e.pitSpeedKmh > 0 ? e.pitSpeedKmh : 80,
                rulesVersion = c.rulesVersion
            };
            int ot = Rules.Level(e.overtaking), tw = Rules.Level(e.tireWear);
            s.overtakeEase = ot == 0 ? 1.0f : ot == 2 ? 0.2f : 0.5f;
            s.wearFactor = tw == 0 ? 0.78f : tw == 2 ? 1.28f : 1.0f;
            unchecked { s.rng.s = c.createdTicks * 6364136223846793005L + c.season * 1442695040888963407L + c.nextRound * 7919L + 1; }
            for (int i = 0; i < 5; i++) s.rng.Next();

            var teams = Rules.Championship(c);
            for (int ti = 0; ti < teams.Count; ti++)
            {
                var t = teams[ti];
                for (int k = 0; k < t.driverIds.Count; k++)
                {
                    var car = new CarState { teamId = t.id, teamName = t.name, color = "#" + ColorUtility.ToHtmlStringRGB(t.color), teamIndex = ti };
                    int[] carParts = t.car;
                    int[] ds = t.driverStats[k];
                    int[] co = t.coordStats;
                    DriverDef def = null;
                    if (t.isPlayer)
                    {
                        int slot = -1;
                        for (int m = 0; m < 2; m++) if (c.drivers[m].filled && c.drivers[m].def.id == t.driverIds[k]) slot = m;
                        car.mine = slot;
                        if (slot >= 0) def = c.drivers[slot].def;
                    }
                    if (def == null) def = Rules.FindDriver(t.driverIds[k]);
                    car.driverId = t.driverIds[k];
                    car.name = t.driverNames[k];
                    car.raceNumber = def != null ? def.raceNumber : -1;
                    car.carCorner = Avg(carParts, 0, 1, 2);
                    car.carStraight = 0.7f * At(carParts, 5) + 0.3f * At(carParts, 4);
                    car.carBrake = At(carParts, 3);
                    car.carGearbox = At(carParts, 4);
                    car.carReliability = Avg(carParts, 2, 3, 4, 5);
                    car.dBrake = At(ds, 0); car.dCorner = At(ds, 1); car.dSmooth = At(ds, 2); car.dCons = At(ds, 3); car.dFit = At(ds, 4);
                    car.pitCrew = t.pitCrew;
                    car.cAgg = co != null ? At(co, 0) : 55; car.cPas = co != null ? At(co, 1) : 55; car.cPlan = co != null ? At(co, 2) : 55;
                    // weekend allocation: 8 soft, 3 medium, 2 hard sets (they have to last FP1 to the race)
                    for (int n = 0; n < 8; n++) car.sets.Add(new TyreSet { compound = Tyre.Soft });
                    for (int n = 0; n < 3; n++) car.sets.Add(new TyreSet { compound = Tyre.Medium });
                    for (int n = 0; n < 2; n++) car.sets.Add(new TyreSet { compound = Tyre.Hard });
                    // wet-weather allocation
                    for (int n = 0; n < 5; n++) car.sets.Add(new TyreSet { compound = Tyre.Inter });
                    for (int n = 0; n < 2; n++) car.sets.Add(new TyreSet { compound = Tyre.Wet });
                    s.cars.Add(car);
                }
            }
            AssignTlas(s);

            // before running, each team's idea of tyre life here is off by up to ~40%; practice laps fix that
            foreach (var g in s.cars.GroupBy(x => x.teamId))
            {
                var err = new float[3];
                for (int c2 = 0; c2 < 3; c2++) err[c2] = Mathf.Clamp(s.rng.Normal() * 0.22f, -0.4f, 0.4f);
                foreach (var car in g) car.lifeErr = (float[])err.Clone();
            }
            float lapEst = 0;
            for (int k = 0; k < track.n; k++) lapEst += track.step / (track.vref[k] * BaseMult);
            s.estLap = lapEst;
            for (int i = 0; i < s.cars.Count; i++) s.cars[i].grid = i + 1;
            RaceControl.MakeWeather(s, e.rainChance > 0 ? Mathf.Clamp01(e.rainChance) : 0f);
            Session.Begin(s, track, Session.FP1);
            return s;
        }

        static void AssignTlas(RaceState s)
        {
            foreach (var car in s.cars)
            {
                var parts = (car.name ?? "").Split(' ');
                string last = parts.Length > 1 ? parts[parts.Length - 1] : parts[0];
                last = new string(last.Where(char.IsLetter).ToArray()).ToUpperInvariant();
                car.tla = last.Length >= 3 ? last.Substring(0, 3) : last.PadRight(3, 'X');
            }
            foreach (var g in s.cars.GroupBy(x => x.tla).Where(g => g.Count() > 1))
                foreach (var car in g) car.tla = car.tla + (car.raceNumber > 0 ? car.raceNumber.ToString() : "");
        }

        static float At(int[] a, int i) { return a != null && i < a.Length && a[i] >= 0 ? a[i] : 60f; }
        static float Avg(int[] a, params int[] idx) { float t = 0; foreach (var i in idx) t += At(a, i); return t / idx.Length; }

        public static void FitStartSet(CarState car)
        {
            int comp = car.plan.Count > 0 ? car.plan[0].compound : Tyre.Medium;
            int best = BestSet(car, comp, -1);
            car.set = best;
            car.sets[best].used = true;
            car.usedCompounds = Bit(comp);
            car.tyreLapAge = 0;
            car.nextSet = -1;
        }

        /// <summary>Wet race start: intermediates or wets instead of the planned slicks.</summary>
        public static void FitWetStart(CarState car, int compound)
        {
            int best = BestSet(car, compound, -1);
            if (best < 0) return;
            car.set = best;
            car.sets[best].used = true;
            car.usedCompounds = 0;
            car.usedWet = true;
            car.tyreLapAge = 0;
        }

        public static int BestSet(CarState car, int compound, int exclude)
        {
            int best = -1;
            for (int i = 0; i < car.sets.Count; i++)
            {
                if (i == exclude || car.sets[i].compound != compound) continue;
                if (best < 0 || car.sets[i].wear < car.sets[best].wear) best = i;
            }
            return best;
        }

        // ===================================================================== pace model

        static readonly float[] CompoundGrip = { 0.008f, 0.004f, 0f, 0f, 0f };   // wet compounds: see RaceControl.WeatherGrip

        public static float TyreGrip(int compound, float wear)
        {
            return CompoundGrip[compound] - 0.014f * wear - Mathf.Max(0, wear - 0.72f) * 0.09f;
        }

        /// <summary>Pace multiplier for a point of the lap. 1.0 = the reference speed of the circuit.</summary>
        public static float PaceMult(RaceState s, CarState car, byte kind)
        {
            float m = BaseMult;
            if (kind == 2) m += 0.0009f * (car.carCorner - 85f) + 0.0004f * (car.dCorner - 85f);
            else if (kind == 1) m += 0.0006f * (car.carBrake - 85f) + 0.0004f * (car.dBrake - 85f) + 0.0002f * (car.carCorner - 85f);
            else m += 0.0009f * (car.carStraight - 85f) + 0.0002f * (car.carGearbox - 85f);

            var set = car.set >= 0 ? car.sets[car.set] : null;
            if (set != null) m += TyreGrip(set.compound, set.wear) + RaceControl.WeatherGrip(set.compound, s.wetness);
            if (car.tyreLapAge < 0.6f) m -= 0.010f * (1f - car.tyreLapAge / 0.6f);      // cold tyres out of the pits
            m -= 0.012f * Mathf.Clamp01(car.fuel);                                    // fuel load
            if (s.session == Session.Race)
            {
                float progress = s.totalLaps > 0 ? Mathf.Clamp01((float)car.lap / s.totalLaps) : 0;
                m -= 0.004f * (1f - car.dFit / 100f) * progress;                       // fatigue over a long race
            }
            else if (car.outLap) m -= 0.07f;                                           // out lap: warming the tyres
            else if (car.pitQueued) m -= 0.06f;                                        // in lap: cooling down
            if (car.style == Style.Push) m += 0.004f;
            else if (car.style == Style.Conserve) m -= 0.006f;
            m += car.lapNoise;
            return m;
        }

        static float OneLapPace(CarState car, Track t)
        {
            float m = BaseMult + 0.0009f * (car.carCorner - 85f) * 0.45f + 0.0009f * (car.carStraight - 85f) * 0.35f
                    + 0.0006f * (car.carBrake - 85f) * 0.2f + 0.0004f * ((car.dCorner + car.dBrake) / 2f - 85f) * 0.65f;
            return m + CompoundGrip[Tyre.Soft];
        }

        public static float WearPerLap(RaceState s, CarState car, int compound)
        {
            float life = Coordinator.BaseLife[Mathf.Clamp(compound, 0, 2)] / s.wearFactor;
            float smooth = 1.15f - 0.3f * Mathf.Clamp01((car.dSmooth - 60f) / 40f);
            float style = car.style == Style.Push ? 1.35f : car.style == Style.Conserve ? 0.72f : 1f;
            return 0.85f / life * smooth * style * RaceControl.WeatherWear(compound, s.wetness);
        }

        static float Accel(float v) { return Mathf.Max(2f, 13f * (1f - (v / Track.VMax) * (v / Track.VMax)) + 1.5f); }

        // ===================================================================== lights

        /// <summary>Five red lights, one per second of real time, then a short random hold before they go out.</summary>
        public static void Lights(RaceState s, float realDt)
        {
            if (s.phase != 1) return;
            s.lightsTimer += realDt;
            if (s.lightsOn < 5)
            {
                if (s.lightsTimer >= 1f) { s.lightsOn++; s.lightsTimer = 0; if (s.lightsOn == 5) s.lightsTimer = -s.rng.Range(0.2f, 2.5f); }
            }
            else if (s.lightsTimer >= 0f)
            {
                s.lightsOn = 0;
                s.phase = 2;
                if (s.session == Session.Race && s.firstCross.Count == 0) s.raceStartTime = s.time;
                foreach (var car in s.cars) car.lapStart = s.time - s.rng.Range(0f, 0.25f) * (1.2f - car.dCons / 100f);
            }
        }

        // ===================================================================== main step

        public static void Step(RaceState s, Track t, float dt)
        {
            if (s.phase != 2 || s.sessionEnded) return;
            s.time += dt;
            bool sess = s.session < Session.Race;
            if (sess)
            {
                s.sessionLeft -= dt;
                if (s.sessionLeft <= 0 && s.flag != "CHEQUERED") s.flag = "CHEQUERED";
                for (int g = 0; g < s.cars.Count; g++) Session.GarageLogic(s, t, g);
            }
            ApplyCommands(s);
            RaceControl.Update(s, t, dt);
            if (!sess && s.scMode == RaceControl.Red)
            {
                foreach (var rc in s.cars) if (rc.status < 3) rc.speed = 0;   // everyone waits
                return;
            }
            if (s.phase != 2) return;   // red flag just ended: the restart lights are on

            int n = s.cars.Count;
            int raceLeader = !sess ? Order(s)[0] : -1;
            // physical order around the lap (for battles, dirty air and blue flags)
            var onTrack = new List<int>();
            for (int i = 0; i < n; i++) if (s.cars[i].status == 0) onTrack.Add(i);
            onTrack.Sort((a, b) => t.Wrap(s.cars[a].dist).CompareTo(t.Wrap(s.cars[b].dist)));
            var aheadOf = new Dictionary<int, int>();
            var gapTo = new Dictionary<int, float>();
            for (int k = 0; k < onTrack.Count; k++)
            {
                int me = onTrack[k], ah = onTrack[(k + 1) % onTrack.Count];
                if (ah == me) continue;
                float gap = t.Wrap(s.cars[ah].dist) - t.Wrap(s.cars[me].dist);
                if (gap < 0) gap += t.L;
                if (s.cars[me].dist < 0 || s.cars[ah].dist < 0) gap = (float)(s.cars[ah].dist - s.cars[me].dist);
                if (gap <= 0 || gap > 120f) continue;
                aheadOf[me] = ah; gapTo[me] = gap;
            }

            // target speeds
            var newDist = new double[n];
            for (int i = 0; i < n; i++)
            {
                var car = s.cars[i];
                newDist[i] = car.dist;
                if (car.status >= 3) { car.speed = 0; continue; }
                if (car.status != 0) continue;
                float lapS = t.Wrap(car.dist);
                byte kind = car.dist < 0 ? (byte)0 : t.Kind(lapS);
                float m = PaceMult(s, car, kind);
                float gapSec = 99f;
                int ah;
                if (aheadOf.TryGetValue(i, out ah)) gapSec = gapTo[i] / Mathf.Max(10f, car.speed);
                if (gapSec < 1.0f && car.dist > 0)
                {
                    if (kind == 0) m += 0.010f;          // Overtake Mode on the straights (2026 rules)
                    else if (kind == 2) m -= 0.005f;     // dirty air in corners
                }
                car.mult = m;
                float target = t.VRef(lapS) * m;
                if (car.slowTimer > 0) { target *= 0.5f; car.slowTimer -= dt; }
                var set = car.set >= 0 ? car.sets[car.set] : null;
                if (set != null && set.wear >= 1f) target *= 0.6f;
                if (!sess && s.chequered && car.lap >= s.totalLaps) target *= 0.5f;
                target = RaceControl.Cap(s, t, car, i, lapS, target, i == raceLeader);
                if (target > car.speed) car.speed = Mathf.Min(target, car.speed + Accel(car.speed) * dt);
                else car.speed = Mathf.Max(target, car.speed - 45f * dt);
                newDist[i] = car.dist + car.speed * dt;
            }

            // battles: nobody drives through anybody
            for (int i = 0; i < n; i++)
            {
                var car = s.cars[i];
                if (car.status != 0) continue;
                if (car.overtaking) { car.overtakeTimer -= dt; if (car.overtakeTimer <= 0) { car.overtaking = false; car.overtakeTarget = -1; } }
                int ah;
                if (!aheadOf.TryGetValue(i, out ah)) continue;
                var a = s.cars[ah];
                double aheadPos = car.dist + gapTo[i];
                double aheadNew = aheadPos + (newDist[ah] - a.dist);
                if (newDist[i] < aheadNew - 10.0) continue;
                bool lapping = a.dist < car.dist - 1.0;                // blue flag: the car ahead on the road is being lapped
                if (s.session < Session.Race && !(a.lapValid && car.lapValid)) lapping = true;   // practice/quali: cars not on a flying lap move aside
                if (lapping || (car.overtaking && car.overtakeTarget == ah)) continue;
                byte kind = car.dist < 0 ? (byte)0 : t.Kind(t.Wrap(car.dist));
                float adv = car.mult - a.mult;
                if (adv > 0 && kind != 2 && RaceControl.OvertakingAllowed(s, t, t.Wrap(car.dist)))
                {
                    float rate = 0.9f * s.overtakeEase * Mathf.Clamp(adv * 100f, 0f, 3f);
                    if (car.style == Style.Push) rate *= 1.4f;
                    if (a.style == Style.Push) rate *= 0.8f;
                    rate *= 0.8f + 0.4f * Mathf.Clamp01((car.dBrake - 70f) / 30f);
                    if (car.lap == 0) rate *= 2f;                       // first-lap shuffle
                    if (s.rng.Chance(rate * dt))
                    {
                        car.overtaking = true; car.overtakeTarget = ah; car.overtakeTimer = 5f;
                        if (!sess && car.lap > 0) RaceControl.OnPass(s, t, i, ah);
                        continue;
                    }
                }
                newDist[i] = aheadNew - 10.0;
                car.speed = Mathf.Min(car.speed, a.speed);
            }

            // move, pit lane, laps, timing
            for (int i = 0; i < n; i++)
            {
                var car = s.cars[i];
                if (car.status >= 3) continue;
                double before = car.dist;
                if (car.status == 0)
                {
                    float oldS = t.Wrap(before);
                    car.dist = Math.Max(before, newDist[i]);
                    float newS = t.Wrap(car.dist);
                    if ((car.pitQueued || (!sess && car.driveThroughPending)) && before >= 0 && oldS < t.pitIn && newS >= t.pitIn && (sess || !s.chequered))
                    {
                        car.status = 1;
                        car.pitS = newS - t.pitIn;
                        if (car.mine >= 0) Say(s, i, car.name, "Team", car.pitQueued ? "Pit entry." : "In for the drive-through.", false);
                    }
                }
                else PitLane(s, t, i, dt);

                float moved = (float)(car.dist - before);
                if (car.set >= 0 && moved > 0 && car.dist > 0)
                {
                    var set = car.sets[car.set];
                    float before01 = set.wear;
                    set.wear += moved / t.L * WearPerLap(s, car, set.compound);
                    car.tyreLapAge += moved / t.L;
                    if (car.mine >= 0 && before01 < 0.75f && set.wear >= 0.75f)
                        Say(s, i, car.name, "Coordinator", "These " + Tyre.Name[set.compound].ToLowerInvariant() + "s are going off.", false);
                    if (before01 < 1f && set.wear >= 1f)
                    {
                        if (car.mine >= 0) Say(s, i, car.name, "Coordinator", "Tyres are gone, I have no grip!", true);
                        car.pitQueued = true;   // physics: a destroyed tyre has to be changed
                    }
                }
                Timing(s, t, i, before, dt);
            }

            if (sess)
            {
                bool allIn = s.cars.All(c => c.status == 5 || c.status == 3);
                if (s.sessionLeft <= 0 && (allIn || s.sessionLeft < -600)) Session.Finish(s);
                return;
            }
            UpdatePositions(s);
            if (s.cars.All(c => c.status == 3 || c.status == 4)) s.phase = 3;
        }

        static void PitLane(RaceState s, Track t, int i, float dt)
        {
            var car = s.cars[i];
            float limit = s.pitSpeedKmh / 3.6f;
            int teams = s.cars.Max(c => c.teamIndex) + 1;
            float box = t.BoxAt(car.teamIndex, teams);
            if (car.status == 2)
            {
                car.speed = 0;
                car.serviceLeft -= dt;
                if (car.serviceLeft <= 0) FinishService(s, i);
                return;
            }
            float target = car.pitS < 60 ? Mathf.Lerp(car.speed, limit, 0.3f) : limit;
            bool beforeBox = car.pitQueued && car.pitS < box;
            if (beforeBox)
            {
                bool mateInBox = s.cars.Any(o => o != car && o.teamId == car.teamId && o.status == 2);
                if (mateInBox && car.pitS > box - 12) target = 0;            // queue behind your teammate
                else if (box - car.pitS < 25) target = Mathf.Min(target, Mathf.Max(3f, (box - car.pitS) * 1.2f));
            }
            car.speed = target;
            float step = car.speed * dt;
            if (beforeBox && car.pitS + step >= box)
            {
                step = box - car.pitS;
                car.pitS = box;
                car.dist += step;
                if (s.session < Session.Race)
                {
                    car.status = 5;            // back in the garage
                    car.speed = 0;
                    car.pitQueued = false;
                    car.pitManual = false;
                    car.lapValid = false;
                    return;
                }
                car.status = 2;
                car.speed = 0;
                // pit crew rating decides how long the car sits still, and how often a stop goes wrong
                float service = Rules.PitServiceTime(car.pitCrew) + Mathf.Abs(s.rng.Normal()) * (0.2f + (100f - car.pitCrew) * 0.006f);
                bool slow = s.rng.Chance(Rules.SlowStopChance(car.pitCrew));
                if (slow) service += s.rng.Range(2.5f, 9f);
                car.lastSlow = slow;
                car.lastService = service;
                // unserved time penalties: the car sits still for them before the crew may touch it
                car.penaltyServing = RaceControl.ServeAtStop(s, i);
                car.serviceLeft = service + car.penaltyServing;
                return;
            }
            car.pitS += step;
            car.dist += step;
            if (car.pitS >= t.pitLen)
            {
                car.status = 0;
                car.speed = limit;
                if (s.session == Session.Race)
                {
                    if (car.driveThroughPending) { car.driveThroughPending = false; RaceControl.ServeDriveThrough(s, i); }
                    // pit-lane speeding: the limiter is the driver's job
                    else if (s.scMode == RaceControl.None && s.rng.Chance(0.004f * (1f + Mathf.Max(0, 85f - car.dCons) / 15f)))
                        RaceControl.Give(s, i, "Speeding in the pit lane", 0, true);
                }
            }
        }

        static void FinishService(RaceState s, int i)
        {
            var car = s.cars[i];
            int next = car.nextSet >= 0 ? car.nextSet : Coordinator.ChooseNextSet(s, car);
            if (next < 0) next = car.set;
            car.set = next;
            car.sets[next].used = true;
            if (Tyre.Dry(car.sets[next].compound)) car.usedCompounds |= Bit(car.sets[next].compound);
            else car.usedWet = true;
            car.tyreLapAge = 0;
            car.stops++;
            car.penaltyServing = 0;
            // unsafe release: released into the path of a car already coming down the pit lane
            if (s.session == Session.Race)
            {
                var t = Track.Load(s.trackId);
                int teams = s.cars.Max(c => c.teamIndex) + 1;
                float box = t.BoxAt(car.teamIndex, teams);
                bool traffic = s.cars.Any(o => o != car && o.status == 1 && o.pitS > box - 14 && o.pitS < box - 2);
                if (traffic && s.rng.Chance(0.06f + (100f - car.pitCrew) * 0.003f)) RaceControl.Give(s, i, "Unsafe release", 5, false);
            }
            if (car.bestService <= 0 || car.lastService < car.bestService) car.bestService = car.lastService;
            car.pitQueued = false;
            car.pitManual = false;
            car.nextManual = false;
            car.nextSet = -1;
            car.status = 1;
            car.speed = 0;
            if (car.mine >= 0)
            {
                Say(s, i, car.name, "Coordinator", "Stop complete, exiting pits on " + Tyre.Name[car.sets[next].compound].ToLowerInvariant() + "s. (" + car.lastService.ToString("0.0") + "s)", false);
                if (car.lastSlow) Say(s, i, "Coordinator", car.name, "Sorry, problem with a wheel. " + car.lastService.ToString("0.0") + "s stop - we lost time there.", true);
            }
        }

        /// <summary>Exact moment the car passed `mark` during the last step (linear interpolation).</summary>
        static double Cross(RaceState s, double before, double after, double mark, float dt)
        {
            double moved = after - before;
            if (moved <= 1e-6) return s.time;
            double frac = Math.Max(0, Math.Min(1, (after - mark) / moved));
            return s.time - dt * frac;
        }

        static void Timing(RaceState s, Track t, int i, double before, float dt)
        {
            var car = s.cars[i];
            if (car.dist < 0) return;
            float cpLen = t.L / s.cpPerLap;
            int cp = (int)(car.dist / cpLen);
            if (cp > car.lastCp)
            {
                double tc = s.time;
                for (int k = car.lastCp + 1; k <= cp; k++)
                {
                    tc = Cross(s, before, car.dist, k * (double)cpLen, dt);
                    if (s.firstCross.Count == k) s.firstCross.Add(tc);
                }
                car.lastCp = cp;
                car.lastCpTime = tc;
            }
            // sectors
            float lapS = t.Wrap(car.dist);
            int sec = t.Sector(lapS);
            if (sec != car.curSector && before >= 0)
            {
                if (sec == car.curSector + 1)
                {
                    double tc = Cross(s, before, car.dist, car.lap * (double)t.L + t.sectorAt[car.curSector], dt);
                    car.sectors[car.curSector] = (float)(tc - car.sectorStart);
                    car.sectorStart = tc;
                }
                car.curSector = sec;
            }
            // laps
            int lapNow = (int)Math.Floor(car.dist / t.L);
            if (lapNow > car.lap && car.dist >= t.L * (car.lap + 1))
            {
                double tl = Cross(s, before, car.dist, lapNow * (double)t.L, dt);
                car.lap = lapNow;
                car.sectors[2] = (float)(tl - car.sectorStart);
                car.sectorStart = tl;
                car.curSector = 0;
                Array.Copy(car.sectors, car.lastSectors, 3);
                car.lastLap = (float)(tl - car.lapStart);
                car.lapStart = tl;
                car.lapCrossTime = tl;
                car.lapNoise = s.rng.Normal() * 0.0025f * (110f - car.dCons) / 40f;
                if (s.session < Session.Race) { Session.OnLap(s, t, i); return; }
                if (car.lap >= 2 && (car.bestLap <= 0 || car.lastLap < car.bestLap)) car.bestLap = car.lastLap;
                if (car.lap >= 2 && (s.fastestLap <= 0 || car.lastLap < s.fastestLap)) { s.fastestLap = car.lastLap; s.fastestCar = i; }
                car.fuel = 1f - Mathf.Clamp01((float)car.lap / s.totalLaps);
                OnLap(s, t, i);
            }
        }

        static void OnLap(RaceState s, Track t, int i)
        {
            var car = s.cars[i];
            var order = Order(s);
            int pos = order.IndexOf(i) + 1;
            if (pos == 1) { car.ledLap = true; RaceControl.LeaderCrossed(s); }

            if (!s.chequered && pos == 1 && car.lap >= s.totalLaps)
            {
                s.chequered = true;
                s.flag = "CHEQUERED";
            }
            if (s.chequered)
            {
                car.status = 4;
                car.finishTime = car.lapCrossTime > 0 ? car.lapCrossTime : s.time;
                if (car.mine >= 0) Say(s, i, "Coordinator", car.name, "Chequered flag. P" + pos + ". Bring it home.", false);
                return;
            }

            // mistakes and failures
            float style = car.style == Style.Push ? 2.2f : car.style == Style.Conserve ? 0.6f : 1f;
            var set = car.set >= 0 ? car.sets[car.set] : null;
            float worn = set != null && set.wear > 0.85f ? 2f : 1f;
            // a wet track multiplies mistakes, and slicks on a wet track even more
            float wet = 1f + 3f * s.wetness + (set != null && Tyre.Dry(set.compound) ? 10f * Mathf.Max(0, s.wetness - 0.08f) : 0f);
            if (s.scMode != RaceControl.None) wet *= 0.2f;
            double crash = 0.00035 * (1 + Mathf.Max(0, 88f - (car.dBrake + car.dCons) / 2f) / 8f) * style * worn * wet;
            if (s.rng.Chance(crash))
            {
                if (s.rng.Chance(0.45)) { Retire(s, i, "Accident"); return; }
                car.slowTimer = s.rng.Range(2f, 4f);
                if (car.mine >= 0) Say(s, i, car.name, "Coordinator", s.wetness > 0.2f ? "Aquaplaned, I nearly lost it!" : "Sorry, went wide. Lost some time.", false);
            }
            RaceControl.OnLap(s, i);
            double fail = 0.00022 * (1 + Mathf.Max(0, 88f - car.carReliability) / 6f);
            if (s.rng.Chance(fail))
            {
                string[] reasons = { "Engine", "Gearbox", "Hydraulics", "Brakes", "Suspension", "Power unit" };
                Retire(s, i, reasons[(int)(s.rng.Next() * reasons.Length) % reasons.Length]);
                return;
            }
            Coordinator.OnLap(s, t, car, i);
        }

        static void Retire(RaceState s, int i, string reason)
        {
            var car = s.cars[i];
            car.status = 3;
            car.retireReason = reason;
            car.speed = 0;
            car.pitQueued = false;
            if (car.mine >= 0) Say(s, i, car.name, "Coordinator", reason == "Accident" ? "I'm in the wall. I'm sorry." : "I've lost power... " + reason.ToLowerInvariant() + " problem. Retiring the car.", true);
            RaceControl.Incident(s, Track.Load(s.trackId), i, reason == "Accident");
        }

        // ===================================================================== order & gaps

        /// <summary>Race order: finished cars by finish, then by laps and progress; retired cars last by distance.</summary>
        public static List<int> Order(RaceState s)
        {
            if (s.session < Session.Race) return Session.Order(s);
            var idx = Enumerable.Range(0, s.cars.Count).ToList();
            idx.Sort((a, b) =>
            {
                var x = s.cars[a]; var y = s.cars[b];
                bool rx = x.status == 3, ry = y.status == 3;
                if (rx != ry) return rx ? 1 : -1;
                if (s.phase == 0 || (s.phase == 1 && s.firstCross.Count == 0)) return x.grid.CompareTo(y.grid);
                bool fx = x.status == 4, fy = y.status == 4;
                if (fx && fy) return x.lap != y.lap ? y.lap.CompareTo(x.lap) : x.finishTime.CompareTo(y.finishTime);
                if (fx != fy && x.lap != y.lap) return y.lap.CompareTo(x.lap);
                if (fx != fy) return fx ? -1 : 1;
                return y.dist.CompareTo(x.dist);
            });
            return idx;
        }

        static readonly Dictionary<int, int> lastPos = new Dictionary<int, int>();
        static readonly Dictionary<int, double> lastPosMsg = new Dictionary<int, double>();

        static void UpdatePositions(RaceState s)
        {
            var order = Order(s);
            for (int p = 0; p < order.Count; p++)
            {
                int i = order[p];
                var car = s.cars[i];
                if (p + 1 < car.bestPos) car.bestPos = p + 1;
                if (car.mine < 0 || s.time < 20 || car.status != 0) continue;
                int prev;
                int key = i + s.round * 100;
                if (lastPos.TryGetValue(key, out prev) && prev != p + 1)
                {
                    double last;
                    lastPosMsg.TryGetValue(key, out last);
                    if (s.time - last > 20)
                    {
                        lastPosMsg[key] = s.time;
                        if (p + 1 < prev) Say(s, i, car.name, "Coordinator", "Got him! Up to P" + (p + 1) + ".", false);
                        else Say(s, i, car.name, "Coordinator", "Lost a place. P" + (p + 1) + ".", false);
                    }
                }
                lastPos[key] = p + 1;
            }
        }

        /// <summary>Gap to the leader in seconds, or laps behind (returned as negative whole number).</summary>
        public static float GapToLeader(RaceState s, Track t, int i, out int lapsDown)
        {
            lapsDown = 0;
            var order = Order(s);
            if (order.Count == 0) return 0;
            var leader = s.cars[order[0]];
            var car = s.cars[i];
            if (order[0] == i) return 0;
            if (car.status == 4 && leader.status == 4)
            {
                lapsDown = leader.lap - car.lap;
                return (float)(car.finishTime - leader.finishTime);
            }
            double behind = leader.dist - car.dist;
            if (behind >= t.L) { lapsDown = (int)(behind / t.L); return 0; }
            if (car.lastCp < 0 || car.lastCp >= s.firstCross.Count) return 0;
            return (float)(car.lastCpTime - s.firstCross[car.lastCp]);
        }

        // ===================================================================== commands & radio

        public static void Say(RaceState s, int car, string from, string to, string text, bool safety, bool suggestion = false)
        {
            s.radio.Add(new RadioMsg { car = car, from = from, to = to, text = text, lap = s.cars[car].lap + 1, time = s.time, safety = safety, suggestion = suggestion });
            if (s.radio.Count > 400) s.radio.RemoveAt(0);
        }

        public static Command Issue(RaceState s, int car, string type, int value)
        {
            var c = new Command { id = s.nextCommandId++, car = car, type = type, value = value, issued = s.time };
            // a newer command of the same kind replaces an older pending one
            s.commands.RemoveAll(x => x.status == "pending" && x.car == car && x.type == type);
            s.commands.Add(c);
            return c;
        }

        public static bool HasPending(RaceState s, int car, string type)
        {
            return s.commands.Any(x => x.status == "pending" && x.car == car && x.type == type);
        }

        static string StyleOrder(int v) { return v == Style.Push ? "Push now." : v == Style.Conserve ? "Conserve the tyres." : "Back to a balanced pace."; }
        static string StyleReply(int v) { return v == Style.Push ? "Copy, pushing." : v == Style.Conserve ? "Copy, saving the tyres." : "Copy, balanced pace."; }

        static void ApplyCommands(RaceState s)
        {
            foreach (var cmd in s.commands)
            {
                if (cmd.status != "pending") continue;
                var car = s.cars[cmd.car];
                if (car.status == 3 || car.status == 4) { cmd.status = "rejected"; Say(s, cmd.car, "Coordinator", "You", car.name + " is no longer racing - instruction not sent.", false); continue; }
                cmd.status = "accepted";
                switch (cmd.type)
                {
                    case "style":
                        car.style = cmd.value; car.styleManual = true;
                        Say(s, cmd.car, "You", car.name, StyleOrder(cmd.value), false);
                        Say(s, cmd.car, car.name, "You", StyleReply(cmd.value), false);
                        break;
                    case "pit":
                        car.pitQueued = true; car.pitManual = true;
                        Say(s, cmd.car, "You", car.name, "Box this lap.", false);
                        Say(s, cmd.car, car.name, "You", PastPitDecision(s, car) ? "Copy, too late for this lap - boxing next lap." : "Copy, boxing.", false);
                        break;
                    case "cancelpit":
                        if (car.status != 0) { cmd.status = "rejected"; Say(s, cmd.car, car.name, "You", "Already in the pit lane.", false); break; }
                        car.pitQueued = false; car.pitManual = true;
                        Say(s, cmd.car, "You", car.name, "Stay out, stay out.", false);
                        Say(s, cmd.car, car.name, "You", "Copy, staying out.", false);
                        break;
                    case "tyre":
                        if (car.status == 2) { cmd.status = "rejected"; Say(s, cmd.car, "Coordinator", "You", "Too late - service has started.", false); break; }
                        car.nextSet = cmd.value; car.nextManual = true;
                        string comp = Tyre.Name[car.sets[cmd.value].compound].ToLowerInvariant();
                        Say(s, cmd.car, "You", car.name, comp.Substring(0, 1).ToUpperInvariant() + comp.Substring(1) + "s at the next stop.", false);
                        Say(s, cmd.car, car.name, "You", "Copy, " + comp + "s next stop.", false);
                        break;
                    case "sendout":
                    {
                        int prog = cmd.value / 10, outComp = cmd.value % 10;
                        int[] laps = { 4, 5, 10, 3 };
                        float[] fuel = { 0.5f, 0.4f, 0.8f, 0.12f };
                        bool quali = Session.IsQuali(s);
                        Session.SendOut(s, cmd.car, quali ? 3 : prog, outComp, quali || prog == 3, quali ? 1 : laps[prog], quali ? 0.08f : fuel[prog]);
                        car.pitManual = false;
                        break;
                    }
                    case "garage":
                        if (car.status == 0) { car.pitQueued = true; car.lapValid = false; car.pitManual = true; }
                        Say(s, cmd.car, "You", car.name, "Box, box. Back to the garage.", false);
                        Say(s, cmd.car, car.name, "You", "Copy, coming in.", false);
                        break;
                    case "auto":
                        if ((cmd.value & 1) != 0) car.pitManual = false;
                        if ((cmd.value & 2) != 0) { car.nextManual = false; car.nextSet = -1; }
                        if ((cmd.value & 4) != 0) car.styleManual = false;
                        Say(s, cmd.car, "You", car.name, "Follow the coordinator's plan.", false);
                        Say(s, cmd.car, car.name, "You", "Understood.", false);
                        break;
                }
            }
            if (s.commands.Count > 200) s.commands.RemoveRange(0, s.commands.Count - 200);
        }

        public static bool PastPitDecision(RaceState s, CarState car)
        {
            var t = Track.Load(s.trackId);
            float lapS = t.Wrap(car.dist);
            return car.dist >= 0 && lapS > t.pitIn - 30f && lapS < t.pitIn + 5f;
        }

        // ===================================================================== results

        public class Line
        {
            public int car, position, points;
            public bool classified;
            public string gap = "";
        }

        public static List<Line> Classification(RaceState s)
        {
            var raw = Order(s);
            // unserved time penalties are added to the race time, which can change the finishing order
            Func<int, double> adjusted = i => s.cars[i].finishTime + RaceControl.PendingSeconds(s, i) + s.cars[i].penaltyAdded;
            var order = raw.Select((i, k) => new { i, k })
                .OrderBy(x => s.cars[x.i].status == 3 ? 2 : s.cars[x.i].status == 4 ? 0 : 1)
                .ThenBy(x => s.cars[x.i].status == 4 ? -s.cars[x.i].lap : 0)
                .ThenBy(x => s.cars[x.i].status == 4 ? adjusted(x.i) : x.k)
                .ThenBy(x => x.k)
                .Select(x => x.i).ToList();
            var lines = new List<Line>();
            int winnerLaps = order.Count > 0 ? s.cars[order[0]].lap : 0;
            var leader = order.Count > 0 ? s.cars[order[0]] : null;
            double leaderTime = order.Count > 0 ? adjusted(order[0]) : 0;
            int pos = 0;
            foreach (int i in order)
            {
                var car = s.cars[i];
                bool classified = car.status == 4 || car.lap >= Mathf.CeilToInt(0.9f * winnerLaps);
                var l = new Line { car = i, classified = classified };
                if (classified)
                {
                    pos++;
                    l.position = pos;
                    l.points = pos <= Cfg.racePoints.Count ? Cfg.racePoints[pos - 1] : 0;
                }
                float pen = RaceControl.PendingSeconds(s, i) + car.penaltyAdded;
                if (car.status == 3) l.gap = "DNF (" + car.retireReason + ")";
                else if (pos == 1) l.gap = FormatTime((car.finishTime > 0 ? adjusted(i) : s.time) - s.raceStartTime);
                else if (leader != null && leader.lap > car.lap) l.gap = "+" + (leader.lap - car.lap) + (leader.lap - car.lap == 1 ? " LAP" : " LAPS");
                else l.gap = "+" + (adjusted(i) - leaderTime).ToString("0.000") + "s";
                if (car.status != 3 && pen > 0) l.gap += " (+" + pen.ToString("0") + "s pen)";
                lines.Add(l);
            }
            return lines;
        }

        public static string FormatTime(double secs)
        {
            if (secs <= 0) return "--:--.---";
            int h = (int)(secs / 3600); secs -= h * 3600;
            int m = (int)(secs / 60); double sec = secs - m * 60;
            return (h > 0 ? h + ":" + m.ToString("00") : m.ToString()) + ":" + sec.ToString("00.000");
        }

        public static string Lap(float secs) { return secs <= 0 ? "--:--.---" : FormatTime(secs); }
    }
}
