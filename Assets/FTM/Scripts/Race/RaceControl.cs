using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    [Serializable]
    public class ControlMsg
    {
        public double time;
        public int lap;
        public string text = "";
        public string kind = "";    // flag, sc, vsc, red, penalty, investigation, info, weather
    }

    [Serializable]
    public class Penalty
    {
        public int car;
        public int victim = -1;
        public string reason = "";
        public int seconds;            // 5 / 10 time penalty (0 = drive-through)
        public bool driveThrough;
        public bool decided, guilty;
        public double decideAt;
        public bool served;
        public int serveByLap;         // drive-through must be served within 3 laps
    }

    /// <summary>
    /// Race control: weather, flags, Safety Car / VSC, red flags and the stewards. Decisions come from
    /// things that actually happened in the simulation - never random penalties to spice up a race.
    /// </summary>
    public static class RaceControl
    {
        public const int None = 0, VSC = 1, SC = 2, Red = 3;

        // ================================================================== weather

        /// <summary>Builds the weekend's weather (rain intensity per minute) - hidden truth; teams only see forecasts.</summary>
        public static void MakeWeather(RaceState s, float rainChance)
        {
            int minutes = 600;
            var line = new float[minutes];
            int windows = 6;
            for (int w = 0; w < windows; w++)
            {
                if (!s.rng.Chance(rainChance / 2.2f)) continue;
                int start = (int)s.rng.Range(w * minutes / (float)windows, (w + 1) * minutes / (float)windows);
                int len = (int)s.rng.Range(10, 75);
                float peak = s.rng.Range(0.25f, 1f);
                for (int m = 0; m < len && start + m < minutes; m++)
                {
                    float ramp = Mathf.Min(1f, Mathf.Min(m, len - m) / 6f);
                    line[start + m] = Mathf.Max(line[start + m], peak * ramp * (0.85f + 0.15f * Mathf.Sin(m * 0.7f)));
                }
            }
            s.rainLine = line.ToList();
            s.rainChance = rainChance;
        }

        static int Minute(RaceState s, float offsetMin = 0)
        {
            if (s.rainLine == null || s.rainLine.Count == 0) return -1;
            return Mathf.Clamp((int)(s.time / 60.0 + offsetMin), 0, s.rainLine.Count - 1);
        }

        public static float RainNow(RaceState s)
        {
            int m = Minute(s);
            if (m < 0) return 0;
            float a = s.rainLine[m], b = s.rainLine[Mathf.Min(m + 1, s.rainLine.Count - 1)];
            return Mathf.Lerp(a, b, (float)(s.time / 60.0 - Math.Floor(s.time / 60.0)));
        }

        /// <summary>Chance of rain (0-1) the teams are shown for `minutes` ahead. Less reliable further out.</summary>
        public static float Forecast(RaceState s, int minutes)
        {
            int m = Minute(s, minutes);
            if (m < 0) return 0;
            float truth = 0;
            for (int k = -2; k <= 2; k++) truth = Mathf.Max(truth, s.rainLine[Mathf.Clamp(m + k, 0, s.rainLine.Count - 1)]);
            // deterministic "forecast error" for this minute, so the numbers don't flicker
            int h = (m * 7919 + minutes * 104729 + s.round * 31) & 0x7fffffff;
            float spread = (0.06f + minutes / 60f) * (truth > 0.05f ? 1f : 0.3f + 2f * s.rainChance);
            float noise = ((h % 1000) / 1000f - 0.5f) * spread;
            float p = truth > 0.05f ? 0.55f + truth * 0.4f : s.rainChance * 0.3f;
            return Mathf.Clamp01(p + noise);
        }

        static void UpdateWeather(RaceState s, Track t, float dt)
        {
            float rain = RainNow(s);
            bool wasRaining = s.rain > 0.05f;
            s.rain = rain;
            int running = s.cars.Count(c => c.status == 0);
            if (rain > 0.03f) s.wetness += rain * 0.004f * dt;
            else s.wetness -= (0.0003f + 0.000015f * running) * dt;   // a dry line forms faster with cars running
            s.wetness = Mathf.Clamp01(s.wetness);
            if (s.session == Session.Race && s.phase >= 2 && s.wetness > 0.1f) s.wasWet = true;
            bool raining = rain > 0.05f;
            if (raining && !wasRaining) Msg(s, "Rain reported at the circuit", "weather");
            if (!raining && wasRaining) Msg(s, "Rain has stopped - track drying", "weather");
        }

        /// <summary>Extra grip for a compound in these conditions (added to the pace multiplier).</summary>
        public static float WeatherGrip(int compound, float wet)
        {
            float g = -0.10f * wet;                                                    // everyone is slower in the wet
            if (compound <= Tyre.Hard) g -= 0.5f * wet;                                // slicks in standing water
            else if (compound == Tyre.Inter) g += -0.03f + 0.03f * Mathf.Clamp01(wet / 0.3f) - 0.15f * Mathf.Max(0, wet - 0.6f);
            else g += -0.06f + 0.06f * Mathf.Clamp01((wet - 0.3f) / 0.4f);
            return g;
        }

        public static float WeatherWear(int compound, float wet)
        {
            if (compound >= Tyre.Inter) return 1f + 3f * Mathf.Clamp01((0.25f - wet) / 0.25f);   // wets overheat on a dry track
            return wet > 0.1f ? 0.7f : 1f;
        }

        /// <summary>The compound that is fastest right now (and soon, using the forecast).</summary>
        public static int BestCompound(RaceState s, float wetAhead)
        {
            if (wetAhead > 0.65f) return Tyre.Wet;
            if (wetAhead > 0.07f) return Tyre.Inter;
            return -1;   // slicks
        }

        // ================================================================== messages

        public static void Msg(RaceState s, string text, string kind)
        {
            int lap = s.cars.Count > 0 ? s.cars.Max(c => c.lap) + 1 : 0;
            s.control.Add(new ControlMsg { time = s.time, lap = Mathf.Min(lap, Mathf.Max(1, s.totalLaps)), text = text, kind = kind });
            if (s.control.Count > 200) s.control.RemoveAt(0);
        }

        // ================================================================== per step

        public static void Update(RaceState s, Track t, float dt)
        {
            UpdateWeather(s, t, dt);
            if (s.yellowTimer > 0)
            {
                s.yellowTimer -= dt;
                if (s.yellowTimer <= 0 && s.scMode == None) { Msg(s, "Track clear", "flag"); if (s.flag == "YELLOW") s.flag = "GREEN"; }
            }
            if (s.session < Session.Race) return;

            // heavy rain can stop the race
            if (s.scMode != Red && s.rain > 0.95f && s.wetness > 0.97f) { s.redRainTimer += dt; if (s.redRainTimer > 240) StartRed(s, "Heavy rain - race suspended"); }
            else s.redRainTimer = 0;

            switch (s.scMode)
            {
                case VSC:
                    s.scTimer -= dt;
                    if (s.scTimer <= 0) { s.scMode = None; s.flag = "GREEN"; Msg(s, "VSC ending - green flag", "vsc"); }
                    break;
                case SC:
                    {
                        // the Safety Car runs ahead of the leader at a controlled pace
                        var leader = s.cars[RaceSim.Order(s)[0]];
                        float scS = t.Wrap(s.scDist);
                        float scSpeed = t.VRef(scS) * 0.55f;
                        if (s.scDist < leader.dist + 30) s.scDist = leader.dist + 30;
                        s.scDist += scSpeed * dt;
                        s.scTimer -= dt;
                        if (s.scTimer <= 0 && !s.scIn) { s.scIn = true; Msg(s, "Safety Car in this lap", "sc"); }
                        if (s.scIn && t.Wrap(s.scDist) > t.pitIn && t.Wrap(s.scDist) < t.pitIn + 60) { s.scInPit = true; }
                        break;
                    }
                case Red:
                    s.redTimer -= dt;
                    if (s.redTimer <= 0 && s.rain < 0.75f) Restart(s, t);
                    break;
            }
            if (s.flag != "CHEQUERED") s.flag = s.scMode == VSC ? "VSC" : s.scMode == SC ? "SAFETY CAR" : s.scMode == Red ? "RED FLAG" : s.yellowTimer > 0 ? "YELLOW" : "GREEN";
            Stewards(s);
        }

        /// <summary>Called when the race leader crosses the line: a Safety Car that has pitted releases the field.</summary>
        public static void LeaderCrossed(RaceState s)
        {
            if (s.scMode == SC && s.scIn && s.scInPit)
            {
                s.scMode = None; s.scIn = false; s.scInPit = false;
                s.flag = "GREEN";
                Msg(s, "Green flag - racing resumes", "sc");
            }
        }

        /// <summary>Speed limit from race control for a car at this point (or the target unchanged).</summary>
        public static float Cap(RaceState s, Track t, CarState car, int index, float lapS, float target, bool isLeader)
        {
            if (s.session == Session.Race)
            {
                if (s.scMode == Red) return 0;
                if (s.scMode == VSC) return Mathf.Min(target, t.VRef(lapS) * 0.62f);
                if (s.scMode == SC)
                {
                    float vmax = t.VRef(lapS) * 0.62f;          // nobody races under the Safety Car
                    target = Mathf.Min(target, vmax);
                    if (isLeader && !s.scInPit && s.scDist - car.dist < 60) target = Mathf.Min(target, t.VRef(t.Wrap(s.scDist)) * 0.55f);
                    return target;
                }
            }
            if (s.yellowTimer > 0 && InYellow(s, t, lapS)) return target * 0.9f;
            return target;
        }

        public static bool InYellow(RaceState s, Track t, float lapS)
        {
            float d = lapS - s.yellowAt;
            if (d < -t.L / 2) d += t.L;
            if (d > t.L / 2) d -= t.L;
            return d > -250f && d < 60f;
        }

        public static bool OvertakingAllowed(RaceState s, Track t, float lapS)
        {
            if (s.session == Session.Race && s.scMode != None) return false;
            if (s.yellowTimer > 0 && InYellow(s, t, lapS)) return false;
            return true;
        }

        // ================================================================== incidents

        /// <summary>A car has crashed or stopped on track. Race control decides yellow / VSC / Safety Car / red.</summary>
        public static void Incident(RaceState s, Track t, int index, bool crash)
        {
            var car = s.cars[index];
            s.yellowAt = t.Wrap(car.dist);
            s.yellowTimer = s.rng.Range(40f, 90f);
            if (s.flag == "GREEN") s.flag = "YELLOW";
            Msg(s, "Yellow flag: car " + car.tla + " stopped" + (crash ? " after an accident" : " on track"), "flag");
            if (s.session != Session.Race || s.chequered || s.scMode != None) return;
            double r = s.rng.Next();
            if (crash)
            {
                if (r < 0.04) StartRed(s, "Red flag - barrier repairs after " + car.tla + "'s accident");
                else if (r < 0.60) StartSC(s, t);
                else StartVSC(s);
            }
            else
            {
                if (r < 0.35) StartVSC(s);
                else if (r < 0.45) StartSC(s, t);
            }
        }

        static void StartVSC(RaceState s)
        {
            s.scMode = VSC;
            s.scTimer = s.rng.Range(90f, 180f);
            s.flag = "VSC";
            Msg(s, "Virtual Safety Car deployed", "vsc");
        }

        static void StartSC(RaceState s, Track t)
        {
            var leader = s.cars[RaceSim.Order(s)[0]];
            s.scMode = SC;
            s.scDist = leader.dist + 250;
            s.scTimer = s.rng.Range(3f, 5f) * s.estLap;
            s.scIn = false; s.scInPit = false;
            s.flag = "SAFETY CAR";
            Msg(s, "Safety Car deployed", "sc");
        }

        static void StartRed(RaceState s, string why)
        {
            s.scMode = Red;
            s.redTimer = 600f;
            s.redRainTimer = 0;
            s.flag = "RED FLAG";
            foreach (var c in s.cars) { if (c.status < 3) c.speed = 0; c.pitQueued = false; }
            Msg(s, why, "red");
        }

        /// <summary>Restart from the running order behind the Safety Car (one lap, then green). Teams may change tyres for free.</summary>
        static void Restart(RaceState s, Track t)
        {
            var order = RaceSim.Order(s);
            int p = 0;
            float cpLen = t.L / s.cpPerLap;
            int leadCp = -1;
            CarState lead = null;
            foreach (int i in order) if (s.cars[i].status < 3) { lead = s.cars[i]; break; }
            if (lead == null) return;
            int leadLap = lead.lap;
            double leadDist = lead.dist;
            foreach (int i in order)
            {
                var car = s.cars[i];
                if (car.status == 3 || car.status == 4) continue;
                // everyone on the leader's lap restarts on it; lapped cars stay the same number of laps down
                int down = Math.Max(0, (int)Math.Floor((leadDist - car.dist) / t.L));
                car.lap = Math.Max(0, leadLap - down);
                car.status = 0;
                car.pitS = 0;
                car.pitQueued = false;
                car.dist = car.lap * (double)t.L - (5 + p * 8);
                car.speed = 0;
                car.overtaking = false;
                car.slowTimer = 0;
                car.curSector = 2;
                car.sectorStart = s.time;
                car.lapStart = s.time;
                // free tyre change under the red flag
                int best = Coordinator.WeatherCompound(s, car);
                int comp = best >= 0 ? best : (car.set >= 0 && Tyre.Dry(car.sets[car.set].compound) ? car.sets[car.set].compound : Tyre.Medium);
                int set = RaceSim.BestSet(car, comp, -1);
                if (car.nextManual && car.nextSet >= 0) { set = car.nextSet; best = 99; }   // your choice for the restart
                car.nextManual = false; car.nextSet = -1;
                if (set >= 0 && (car.set < 0 || car.sets[set].wear < car.sets[car.set].wear - 0.1f || best >= 0 || !Tyre.Dry(car.sets[car.set].compound)))
                {
                    car.set = set; car.sets[set].used = true; car.tyreLapAge = 0;
                    if (car.sets[set].compound <= Tyre.Hard) car.usedCompounds |= 1 << car.sets[set].compound;
                    else car.usedWet = true;
                }
                car.lastCp = Mathf.Max(-1, (int)Math.Floor(car.dist / cpLen));
                car.lastCpTime = s.time;
                if (p == 0) leadCp = car.lastCp;
                p++;
            }
            // timing checkpoints after the leader's restart position are measured again
            if (leadCp + 1 >= 0 && leadCp + 1 < s.firstCross.Count) s.firstCross.RemoveRange(leadCp + 1, s.firstCross.Count - leadCp - 1);
            // rolling restart: the Safety Car leads the field round for one lap, then pits
            s.scMode = SC;
            s.scDist = lead.dist + 60;
            s.scTimer = 0;
            s.scIn = true; s.scInPit = false;
            s.flag = "SAFETY CAR";
            Msg(s, "Race resumes behind the Safety Car - Safety Car in this lap", "sc");
        }

        // ================================================================== stewards

        public static void Investigate(RaceState s, int car, string reason, int victim = -1)
        {
            if (s.penalties.Any(p => p.car == car && !p.decided && p.reason == reason)) return;
            s.penalties.Add(new Penalty { car = car, victim = victim, reason = reason, decideAt = s.time + s.rng.Range(40f, 120f) });
            Msg(s, "Incident involving " + s.cars[car].tla + (victim >= 0 ? " and " + s.cars[victim].tla : "") + " - " + reason + " - under investigation", "investigation");
        }

        public static void Give(RaceState s, int car, string reason, int seconds, bool driveThrough)
        {
            var c = s.cars[car];
            var p = new Penalty { car = car, reason = reason, seconds = seconds, driveThrough = driveThrough, decided = true, guilty = true, serveByLap = c.lap + 3 };
            s.penalties.Add(p);
            Msg(s, driveThrough ? "Drive-through penalty for " + c.tla + " - " + reason : seconds + " second time penalty for " + c.tla + " - " + reason, "penalty");
            if (c.mine >= 0) RaceSim.Say(s, car, "Coordinator", c.name, driveThrough ? "We have a drive-through penalty: " + reason.ToLowerInvariant() + "." : "We have a " + seconds + " second penalty: " + reason.ToLowerInvariant() + ".", true);
        }

        static void Stewards(RaceState s)
        {
            foreach (var p in s.penalties)
            {
                if (p.decided || s.time < p.decideAt) continue;
                p.decided = true;
                var c = s.cars[p.car];
                // the decision depends on the incident: the driver who made the move, with less skill, is more often at fault
                float fault = 0.35f + Mathf.Clamp01((85f - c.dCons) / 40f) * 0.3f + (c.style == Style.Push ? 0.15f : 0f);
                p.guilty = s.rng.Chance(fault);
                if (p.guilty)
                {
                    p.seconds = s.rng.Chance(0.6) ? 5 : 10;
                    p.serveByLap = c.lap + 3;
                    Msg(s, p.seconds + " second time penalty for " + c.tla + " - " + p.reason, "penalty");
                    if (c.mine >= 0) RaceSim.Say(s, p.car, "Coordinator", c.name, "We have a " + p.seconds + " second penalty for that.", true);
                }
                else Msg(s, "No further action - " + c.tla + " - " + p.reason, "investigation");
            }
        }

        public static float PendingSeconds(RaceState s, int car)
        {
            return s.penalties.Where(p => p.car == car && p.decided && p.guilty && !p.served && !p.driveThrough).Sum(p => p.seconds);
        }

        public static bool PendingDriveThrough(RaceState s, int car)
        {
            return s.penalties.Any(p => p.car == car && p.driveThrough && !p.served);
        }

        public static bool UnderInvestigation(RaceState s, int car)
        {
            return s.penalties.Any(p => p.car == car && !p.decided);
        }

        /// <summary>Time penalties are served stationary before the crew starts work.</summary>
        public static float ServeAtStop(RaceState s, int car)
        {
            float total = 0;
            foreach (var p in s.penalties)
                if (p.car == car && p.decided && p.guilty && !p.served && !p.driveThrough) { total += p.seconds; p.served = true; }
            if (total > 0) Msg(s, s.cars[car].tla + " serves a " + total + " second penalty", "penalty");
            return total;
        }

        public static void ServeDriveThrough(RaceState s, int car)
        {
            foreach (var p in s.penalties) if (p.car == car && p.driveThrough && !p.served) p.served = true;
            Msg(s, s.cars[car].tla + " has served the drive-through", "penalty");
        }

        /// <summary>Per-lap checks: track limits (warnings, then a penalty) and unserved drive-throughs.</summary>
        public static void OnLap(RaceState s, int i)
        {
            var c = s.cars[i];
            float chance = 0.007f * (c.style == Style.Push ? 2f : 1f) * (1f + Mathf.Max(0, 85f - c.dCons) / 30f);
            if (s.scMode == None && s.rng.Chance(chance))
            {
                c.trackLimits++;
                if (c.trackLimits == 3) { Msg(s, "Black and white flag for " + c.tla + " - track limits", "flag"); if (c.mine >= 0) RaceSim.Say(s, i, "Coordinator", c.name, "Black and white flag. One more and it's a penalty.", true); }
                else if (c.trackLimits == 4) Give(s, i, "Track limits", 5, false);
                else if (c.trackLimits < 3) Msg(s, "Lap time deleted for " + c.tla + " - track limits", "info");
            }
            if (s.scMode != None)
                foreach (var p in s.penalties) if (p.car == i && p.driveThrough && !p.served) p.serveByLap++;   // caution laps don't count
            foreach (var p in s.penalties)
                if (p.car == i && p.driveThrough && !p.served && c.lap > p.serveByLap)
                {
                    p.driveThrough = false;
                    p.seconds = 20;            // not served in time: converted to time added to the result
                    p.decided = true; p.guilty = true;
                    c.driveThroughPending = false;
                    Msg(s, c.tla + " did not serve the drive-through - 20 seconds added", "penalty");
                }
            // drive-throughs can't be served under the Safety Car; otherwise the car comes in within the laps allowed
            if (!c.driveThroughPending && s.scMode == None && PendingDriveThrough(s, i))
            {
                c.driveThroughPending = true;
                if (c.mine >= 0) RaceSim.Say(s, i, "Coordinator", c.name, "Serve the drive-through this lap.", true);
            }
        }

        /// <summary>Contact during a pass: the move may cost the other car time or a tyre, and goes to the stewards.</summary>
        public static void OnPass(RaceState s, Track t, int attacker, int defender)
        {
            var a = s.cars[attacker]; var d = s.cars[defender];
            // a close fight is where contact happens; breezing past a much slower car rarely ends in it
            float closeness = 1f - Mathf.Clamp01((a.mult - d.mult) * 100f / 2f);
            float risk = 0.008f * closeness * (a.style == Style.Push ? 1.5f : 1f) * (1f + Mathf.Max(0, 85f - a.dBrake) / 40f) * (1f + s.wetness);
            if (!s.rng.Chance(risk)) return;
            d.slowTimer = s.rng.Range(1.5f, 4f);
            if (s.rng.Chance(0.25) && d.set >= 0) { d.sets[d.set].wear = 1.05f; d.pitQueued = true; }   // puncture
            if (d.mine >= 0) RaceSim.Say(s, defender, d.name, "Coordinator", "He hit me! Check the car.", true);
            if (a.mine >= 0) RaceSim.Say(s, attacker, a.name, "Coordinator", "We touched. I think I'm OK.", false);
            if (s.session == Session.Race) Investigate(s, attacker, "Causing a collision", defender);
        }
    }
}
