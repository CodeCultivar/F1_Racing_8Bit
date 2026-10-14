using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    /// <summary>
    /// The coordinator's game plan and in-race decisions. Every team's coordinator uses this; a better
    /// Game-plan rating makes the plan more reliable, Aggressive/Passive shape pit and pace calls.
    /// It only knows what is visible now (tyre wear, gaps, laps left) - never future incidents.
    /// </summary>
    public static class Coordinator
    {
        /// <summary>Laps a set lasts at a medium-wear track before reaching ~85% wear (balanced pace).</summary>
        public static readonly float[] BaseLife = { 18f, 28f, 40f };
        static readonly float[] PaceOffset = { -0.7f, -0.35f, 0f };   // seconds per lap vs hard
        const float PitLaneLoss = 20f;   // driving through the pit lane vs the track

        public static float Life(RaceState s, int compound) { return BaseLife[compound] / s.wearFactor; }

        class Option { public int[] comps; public float cost; public int[] lengths; }

        /// <summary>Builds (or rebuilds) a car's race plan. forcedStart = compound the car must start on, or -1.</summary>
        public static void Plan(RaceState s, CarState car, int forcedStart)
        {
            int laps = s.totalLaps;
            var options = new List<Option>();
            int[] dry = { Tyre.Soft, Tyre.Medium, Tyre.Hard };
            foreach (int a in dry)
                foreach (int b in dry)
                {
                    if (a != b) options.Add(Make(s, car, new[] { a, b }, laps, PitLoss(car)));
                    foreach (int c in dry)
                        if (a != b || b != c || a != c) options.Add(Make(s, car, new[] { a, b, c }, laps, PitLoss(car)));
                }
            options = options.Where(o => o != null && (forcedStart < 0 || o.comps[0] == forcedStart)).ToList();
            if (options.Count == 0) options.Add(Make(s, car, forcedStart >= 0 ? new[] { forcedStart, forcedStart == Tyre.Hard ? Tyre.Medium : Tyre.Hard } : new[] { Tyre.Medium, Tyre.Hard }, laps, PitLoss(car), true));

            // a weaker coordinator misjudges the options more
            float noise = (100f - car.cPlan) / 100f * 9f;
            var judge = new System.Random((car.driverId + s.round + s.season).GetHashCode());
            foreach (var o in options.OrderBy(x => string.Join(",", x.comps)))
            {
                double u1 = 1.0 - judge.NextDouble(), u2 = judge.NextDouble();
                o.cost += (float)(System.Math.Sqrt(-2 * System.Math.Log(u1)) * System.Math.Cos(2 * System.Math.PI * u2)) * noise;
            }
            var best = options.OrderBy(o => o.cost).First();

            car.plan.Clear();
            int end = 0;
            for (int i = 0; i < best.comps.Length; i++)
            {
                end += best.lengths[i];
                car.plan.Add(new Stint { compound = best.comps[i], endLap = i == best.comps.Length - 1 ? laps : end });
            }
            int stops = best.comps.Length - 1;
            string seq = string.Join(" > ", best.comps.Select(c => Tyre.Name[c]));
            string why = s.wearFactor > 1.1f ? "High tyre wear here, so we split the race to protect the tyres."
                       : s.wearFactor < 0.9f ? "Low tyre wear, so we can run long stints and avoid traffic."
                       : stops == 1 ? "One stop keeps us out of traffic and saves pit time."
                       : "Two stops lets us push on fresher tyres.";
            car.planReason = (stops == 1 ? "One stop: " : "Two stops: ") + seq + ". " + why;
        }

        static float PitLoss(CarState car) { return PitLaneLoss + Rules.PitServiceTime(car.pitCrew) + Rules.SlowStopChance(car.pitCrew) * 5f; }

        static Option Make(RaceState s, CarState car, int[] comps, int laps, float pitLoss, bool force = false)
        {
            // the coordinator plans with what the team KNOWS about tyre life here (practice data), not the truth
            System.Func<int, float> life = c => Session.EstimatedLife(s, car, c);
            float totalLife = comps.Sum(c => life(c));
            var lengths = new int[comps.Length];
            int used = 0;
            for (int i = 0; i < comps.Length; i++)
            {
                lengths[i] = i == comps.Length - 1 ? laps - used : Mathf.Max(1, Mathf.RoundToInt(laps * life(comps[i]) / totalLife));
                used += lengths[i];
            }
            float cost = (comps.Length - 1) * pitLoss;
            for (int i = 0; i < comps.Length; i++)
            {
                float lifeI = life(comps[i]);
                if (!force && lengths[i] > lifeI * 1.02f) return null;      // would run the tyre past its life
                if (lengths[i] < 3 && !force) return null;
                float avgWear = 0.85f * lengths[i] / lifeI / 2f;
                cost += lengths[i] * (PaceOffset[comps[i]] + 80f * (0.014f * avgWear + Mathf.Max(0, 0.85f * lengths[i] / lifeI - 0.72f) * 0.03f));
            }
            return new Option { comps = comps, cost = cost, lengths = lengths };
        }

        /// <summary>When each car leaves the garage in practice / qualifying, and what it runs.</summary>
        public static List<Run> SessionRuns(RaceState s, Track t, CarState car)
        {
            var r = s.rng;
            var runs = new List<Run>();
            switch (s.session)
            {
                case Session.FP1:
                    runs.Add(new Run { startAt = r.Range(180, 600), laps = 4, compound = Tyre.Hard, newSet = true, fuel = 0.5f, program = 0 });
                    runs.Add(new Run { startAt = r.Range(1300, 1700), laps = 5, compound = Tyre.Medium, newSet = true, fuel = 0.4f, program = 1 });
                    runs.Add(new Run { startAt = r.Range(2300, 2700), laps = 8, compound = Tyre.Medium, newSet = false, fuel = 0.75f, program = 2 });
                    break;
                case Session.FP2:
                    runs.Add(new Run { startAt = r.Range(300, 700), laps = 3, compound = Tyre.Soft, newSet = true, fuel = 0.12f, program = 3 });
                    runs.Add(new Run { startAt = r.Range(1500, 1900), laps = 10, compound = Tyre.Medium, newSet = false, fuel = 0.8f, program = 2 });
                    runs.Add(new Run { startAt = r.Range(2600, 2900), laps = 6, compound = Tyre.Hard, newSet = false, fuel = 0.6f, program = 2 });
                    break;
                case Session.FP3:
                    runs.Add(new Run { startAt = r.Range(400, 900), laps = 3, compound = Tyre.Soft, newSet = true, fuel = 0.12f, program = 3 });
                    runs.Add(new Run { startAt = r.Range(1800, 2300), laps = 4, compound = Tyre.Soft, newSet = false, fuel = 0.3f, program = 1 });
                    break;
                default:
                    // two attempts on new softs; a better coordinator times them into clearer track
                    float first = r.Range(60, 240), late = s.sessionLength - r.Range(300, 420) - (car.cPlan - 70) * 1.5f;
                    runs.Add(new Run { startAt = first, laps = 1, compound = Tyre.Soft, newSet = true, fuel = 0.08f, program = 3 });
                    runs.Add(new Run { startAt = Mathf.Max(first + 200, late), laps = 1, compound = Tyre.Soft, newSet = true, fuel = 0.08f, program = 3 });
                    break;
            }
            if (s.session <= Session.FP3)
                foreach (var run in runs) run.laps = Mathf.Max(2, run.laps + (int)r.Range(-1, 2.99f));
            return runs;
        }

        /// <summary>
        /// How wet the coordinator expects the track to be over the next laps: what they can see on track, plus
        /// the forecast. Better planners trust a strong forecast earlier.
        /// </summary>
        public static float WetAhead(RaceState s, CarState car)
        {
            float w = s.wetness;
            float f5 = RaceControl.Forecast(s, 5);
            float trust = 0.6f + car.cPlan / 250f;
            if (f5 * trust > 0.62f && s.rain > 0.02f) w = Mathf.Max(w, 0.1f);
            if (s.rain > 0.6f) w = Mathf.Max(w, s.wetness + 0.15f);
            return w;
        }

        /// <summary>The compound this coordinator wants for the conditions (-1 = slicks).</summary>
        public static int WeatherCompound(RaceState s, CarState car)
        {
            float w = WetAhead(s, car);
            int cur = car.set >= 0 ? car.sets[car.set].compound : Tyre.Medium;
            // hysteresis: stay on the current type unless the conditions have clearly moved on
            float slow = (100f - car.cPlan) / 1000f;   // weaker planners react later
            if (Tyre.Dry(cur)) return w > 0.07f + slow ? (w > 0.65f ? Tyre.Wet : Tyre.Inter) : -1;
            if (cur == Tyre.Inter) return w < 0.045f - slow * 0.4f ? -1 : w > 0.72f + slow ? Tyre.Wet : Tyre.Inter;
            return w < 0.045f - slow * 0.4f ? -1 : w < 0.55f - slow ? Tyre.Inter : Tyre.Wet;
        }

        public static int ChooseNextSet(RaceState s, CarState car)
        {
            int wetComp = WeatherCompound(s, car);
            if (wetComp >= 0)
            {
                int ws = RaceSim.BestSet(car, wetComp, car.set);
                if (ws < 0) ws = RaceSim.BestSet(car, wetComp == Tyre.Wet ? Tyre.Inter : Tyre.Wet, car.set);
                if (ws >= 0) return ws;
            }
            int stint = car.stops + 1;
            int comp = stint < car.plan.Count ? car.plan[stint].compound : Tyre.Hard;
            var cur = car.set >= 0 ? car.sets[car.set].compound : -1;
            // the two-dry-compounds rule: if only one dry compound has been used, the next one must differ
            // (it doesn't apply once intermediates or wets have been raced)
            int usedCount = 0;
            for (int c = 0; c < 3; c++) if ((car.usedCompounds & (1 << c)) != 0) usedCount++;
            if (!car.usedWet && usedCount <= 1 && comp == cur) comp = cur == Tyre.Hard ? Tyre.Medium : Tyre.Hard;
            if (!Tyre.Dry(cur) && s.totalLaps - car.lap < 15) comp = Tyre.Soft;   // drying track late on: softs switch on fastest
            int set = RaceSim.BestSet(car, comp, car.set);
            if (set < 0) set = RaceSim.BestSet(car, comp == Tyre.Hard ? Tyre.Medium : Tyre.Hard, car.set);
            return set;
        }

        public static void OnLap(RaceState s, Track t, CarState car, int index)
        {
            if (car.status != 0) return;
            int lapsLeft = s.totalLaps - car.lap;
            var set = car.set >= 0 ? car.sets[car.set] : null;
            float wear = set != null ? set.wear : 0;
            int stint = car.stops;
            int endLap = stint < car.plan.Count ? car.plan[stint].endLap : s.totalLaps;
            bool stopsLeft = stint < car.plan.Count - 1;

            // ---- pit timing
            if (!car.pitManual && !car.pitQueued && lapsLeft >= 2)
            {
                float gapAhead = GapAhead(s, t, index);
                bool planned = stopsLeft && car.lap + 1 >= endLap;
                bool urgent = wear > 0.82f && lapsLeft > 3;
                if (urgent && wear < 1f)
                {
                    // only worth it if there is a clearly better set to fit
                    int cand = car.nextManual && car.nextSet >= 0 ? car.nextSet : ChooseNextSet(s, car);
                    if (cand >= 0 && car.sets[cand].wear > wear - 0.3f) urgent = false;
                }
                bool undercut = stopsLeft && Mathf.Abs(car.lap + 1 - endLap) <= 2 && gapAhead < 1.5f && s.rng.Chance(car.cAgg / 400f);
                int usedCount = 0;
                for (int c = 0; c < 3; c++) if ((car.usedCompounds & (1 << c)) != 0) usedCount++;
                bool mustStop = !car.usedWet && usedCount < 2 && lapsLeft <= 3;
                bool mateQueued = s.cars.Any(o => o != car && o.teamId == car.teamId && (o.pitQueued || o.status == 1 || o.status == 2));
                // conditions: wrong tyre type for the track
                int curComp = set != null ? set.compound : Tyre.Medium;
                int wantWet = WeatherCompound(s, car);
                bool weather = (wantWet >= 0 && wantWet != curComp) || (wantWet < 0 && !Tyre.Dry(curComp));
                // Safety Car / VSC: a stop costs far less while the field is slowed
                bool caution = s.scMode == RaceControl.SC || s.scMode == RaceControl.VSC;
                bool cheap = caution && lapsLeft > 4 && ((stopsLeft && car.lap + 1 >= endLap - 10 && wear > 0.3f) || (!stopsLeft && wear > 0.55f && lapsLeft > 8) || (!car.usedWet && usedCount < 2));
                if (cheap && mateQueued && s.scMode == RaceControl.VSC && s.rng.Chance(0.5)) cheap = false;   // double-stacking under VSC costs too much
                if ((planned || undercut) && mateQueued && !urgent && !mustStop && s.rng.Chance(0.75)) planned = undercut = false;  // avoid stacking
                if (planned || urgent || undercut || mustStop || weather || cheap)
                {
                    car.pitQueued = true;
                    if (!car.nextManual) car.nextSet = ChooseNextSet(s, car);
                    if (car.mine >= 0)
                    {
                        string comp = car.nextSet >= 0 ? Tyre.Name[car.sets[car.nextSet].compound].ToLowerInvariant() : "tyres";
                        string why = weather ? (Tyre.Dry(curComp) ? " The track is getting wet." : wantWet < 0 ? " Track is dry enough for slicks." : " Conditions have changed.")
                                   : cheap ? (s.scMode == RaceControl.SC ? " Cheap stop under the Safety Car." : " Cheap stop under the VSC.")
                                   : urgent ? " Tyres are finished." : undercut ? " Going for the undercut." : mustStop ? " We still need the second compound." : "";
                        RaceSim.Say(s, index, "Coordinator", car.name, "Box this lap. " + Cap(comp) + "s ready." + why, false);
                        RaceSim.Say(s, index, car.name, "Coordinator", "Copy, boxing for " + comp + "s.", false);
                    }
                }
            }
            else if (car.pitManual && car.mine >= 0 && wear > 0.82f && !car.pitQueued && lapsLeft > 3 && car.lap % 3 == 0)
            {
                RaceSim.Say(s, index, "Coordinator", "You", "Suggestion: box " + car.name + " - tyres are at " + Mathf.RoundToInt(wear * 100) + "% wear.", false, true);
            }
            else if (car.pitManual && car.mine >= 0 && !car.pitQueued && lapsLeft > 2 && car.lap % 2 == 0)
            {
                int curC = set != null ? set.compound : Tyre.Medium;
                int want = WeatherCompound(s, car);
                if ((want >= 0 && want != curC) || (want < 0 && !Tyre.Dry(curC)))
                    RaceSim.Say(s, index, "Coordinator", "You", "Suggestion: box " + car.name + " for " + (want < 0 ? "slicks" : Tyre.Name[want].ToLowerInvariant() + "s") + " - conditions have changed.", false, true);
                else if ((s.scMode == RaceControl.SC || s.scMode == RaceControl.VSC) && wear > 0.35f && lapsLeft > 6)
                    RaceSim.Say(s, index, "Coordinator", "You", "Suggestion: " + (s.scMode == RaceControl.SC ? "Safety Car" : "VSC") + " - cheap stop for " + car.name + " if we box now.", false, true);
            }

            // ---- drive style
            if (!car.styleManual)
            {
                float gapAhead = GapAhead(s, t, index);
                int stintEnd = car.pitQueued ? car.lap + 1 : endLap;
                float projected = wear + (stintEnd - car.lap) * (set != null ? RaceSim.WearPerLap(s, car, set.compound) : 0);
                int want = Style.Balanced;
                string msg = null;
                if (projected > 0.97f && !car.pitQueued) { want = Style.Conserve; msg = "Conserve for a few laps. We need to extend this stint."; }
                else if (gapAhead < 1.0f && wear < 0.6f && car.cAgg >= car.cPas - 8) { want = Style.Push; msg = "Push now, the car ahead is within a second."; }
                else if (lapsLeft <= 5 && projected < 0.85f) { want = Style.Push; msg = "Final laps. Everything you've got."; }
                else if (car.cPas > car.cAgg + 10 && wear > 0.5f) { want = Style.Conserve; msg = "Look after the tyres, no need to push."; }
                if (want != car.style)
                {
                    car.style = want;
                    if (car.mine >= 0)
                    {
                        RaceSim.Say(s, index, "Coordinator", car.name, msg ?? "Back to a balanced pace.", false);
                        RaceSim.Say(s, index, car.name, "Coordinator", want == Style.Push ? "Copy, pushing." : want == Style.Conserve ? "Understood, saving the tyres." : "Copy.", false);
                    }
                }
            }
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : s.Substring(0, 1).ToUpperInvariant() + s.Substring(1); }

        /// <summary>Seconds to the car ahead in the race order (99 if none / unknown).</summary>
        public static float GapAhead(RaceState s, Track t, int index)
        {
            var order = RaceSim.Order(s);
            int p = order.IndexOf(index);
            if (p <= 0) return 99f;
            var me = s.cars[index]; var ah = s.cars[order[p - 1]];
            if (ah.status == 3) return 99f;
            double d = ah.dist - me.dist;
            if (d > t.L) return 99f;
            return (float)(d / Mathf.Max(20f, me.speed));
        }
    }
}
