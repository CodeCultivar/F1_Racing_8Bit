using System;
using System.Collections.Generic;

namespace FTM
{
    public static class Tyre
    {
        public const int Soft = 0, Medium = 1, Hard = 2, Inter = 3, Wet = 4;
        public static readonly string[] Letter = { "S", "M", "H", "I", "W" };
        public static readonly string[] Name = { "Soft", "Medium", "Hard", "Intermediate", "Wet" };
        public static readonly string[] Hex = { "#E8002D", "#F5D84A", "#F4F6F8", "#43B047", "#0067FF" };
        public static bool Dry(int c) { return c <= Hard; }
    }

    public static class Style
    {
        public const int Balanced = 0, Push = 1, Conserve = 2;
        public static readonly string[] Label = { "Balanced", "Push", "Conserve" };
    }

    /// <summary>Small deterministic RNG whose state is saved with the race, so a resumed race never re-rolls.</summary>
    [Serializable]
    public class RaceRng
    {
        public long s = 0x2545F4914F6CDD1DL;

        public double Next()
        {
            unchecked
            {
                ulong x = (ulong)s;
                x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
                s = (long)x;
                return ((x * 2685821657736338717UL) >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        public float Range(float a, float b) { return a + (float)Next() * (b - a); }
        public bool Chance(double p) { return Next() < p; }

        public float Normal()
        {
            double u1 = Math.Max(1e-9, Next()), u2 = Next();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }

    [Serializable]
    public class TyreSet
    {
        public int compound;
        public float wear;     // 0 = new, 1 = worn out
        public bool used;
    }

    [Serializable]
    public class Stint
    {
        public int compound;
        public int endLap;     // planned lap to stop at the end of this stint (last stint = race distance)
    }

    [Serializable]
    public class RadioMsg
    {
        public int car = -1;       // index into cars (only your two drivers get radio)
        public string from = "", to = "", text = "";
        public int lap;
        public double time;
        public bool safety;
        public bool suggestion;
    }

    [Serializable]
    public class Command
    {
        public int id;
        public int car;
        public string type = "";   // pit, cancelpit, tyre, style, auto
        public int value;
        public double issued;
        public string status = "pending";
    }

    /// <summary>One planned practice/qualifying run: leave the garage at startAt (seconds into the session).</summary>
    [Serializable]
    public class Run
    {
        public float startAt;
        public int laps;           // timed laps (out lap and in lap are extra)
        public int compound;
        public bool newSet;
        public float fuel;         // 0 = empty, 1 = race start load
        public int program;        // Session.Programs
        public bool done;
    }

    [Serializable]
    public class CarState
    {
        // identity
        public string driverId = "", name = "", tla = "", teamId = "", teamName = "", color = "";
        public int raceNumber;
        public int mine = -1;        // 0 / 1 = your Driver 1 / Driver 2, -1 = rival
        public int teamIndex;
        // ratings (difficulty already applied for rivals)
        public float carCorner, carStraight, carBrake, carGearbox, carReliability;
        public float dBrake, dCorner, dSmooth, dCons, dFit;
        public float cAgg, cPas, cPlan;
        public float pitCrew = 50;
        // motion
        public double dist;          // total metres since the start line (negative on the grid)
        public float speed;
        public int lap;              // completed laps
        public int status;           // 0 racing, 1 pit lane, 2 pit service, 3 retired, 4 finished
        public float pitS;           // metres travelled in the pit lane
        public float serviceLeft;
        public float slowTimer;      // after a mistake
        public float mult = 1f;      // current pace multiplier (for battles)
        public bool overtaking;
        public float overtakeTimer;
        public int overtakeTarget = -1;
        // tyres
        public List<TyreSet> sets = new List<TyreSet>();
        public int set = -1;         // fitted set
        public int nextSet = -1;     // chosen set for the next stop
        public bool nextManual;
        public int usedCompounds;    // bit mask of dry compounds raced
        public float tyreLapAge;     // laps on the fitted set
        // strategy
        public List<Stint> plan = new List<Stint>();
        public string planReason = "";
        public bool pitQueued;
        public bool pitManual;
        public int style = Style.Balanced;
        public bool styleManual;
        public int stops;
        public float lastService, bestService;
        public bool lastSlow;
        // timing
        public int grid;
        public double lapStart;
        public float lastLap, bestLap;
        public float[] sectors = new float[3];
        public float[] lastSectors = new float[3];
        public double sectorStart;
        public int curSector;
        public int lastCp = -1;
        public double lastCpTime;
        public float lapNoise;
        public double finishTime;
        public double lapCrossTime;
        // practice / qualifying
        public float fuel = 1f;
        public bool outLap, lapValid;
        public int runLaps, program;
        public List<Run> runs = new List<Run>();
        public float sBest;          // best valid lap this session
        public double sBestAt;
        public int sLaps;
        public float q1, q2, q3;
        public double q1At, q2At, q3At;
        public int qOutSeg;          // 0 = still in, 1 = out in Q1, 2 = out in Q2
        public float[] knowLaps = new float[3];   // laps of tyre data per compound this weekend
        public float[] knowWear = new float[3];   // wear measured over those laps
        public float[] lifeErr = new float[3];    // how wrong the pre-weekend tyre life estimate is
        public float lapStartWear;
        public string issue = "";
        public int setsUsedNew;
        public string retireReason = "";
        public bool ledLap;
        public int bestPos = 99;
        // race control
        public bool usedWet;         // raced inters or wets (two-dry-compound rule no longer applies)
        public int trackLimits;
        public bool driveThroughPending;  // car is heading through the pit lane to serve a drive-through
        public float penaltyAdded;   // seconds added to the race time at the finish
        public float penaltyServing; // seconds being served stationary in the box
    }

    [Serializable]
    public class RaceState
    {
        public int version = 1;
        public int season, round;
        public string grandPrix = "", circuit = "", trackId = "";
        public int totalLaps;
        public int pitSpeedKmh = 80;
        public float overtakeEase = 0.5f;
        public float wearFactor = 1f;
        public string rulesVersion = "";
        public int phase;            // 0 grid & plan, 1 lights, 2 racing, 3 finished
        public double time;
        public float lightsTimer;
        public int lightsOn;
        public bool chequered;
        public int speed = 1;        // 0 pause, 1, 3, 5
        public List<CarState> cars = new List<CarState>();
        public List<double> firstCross = new List<double>();
        public int cpPerLap = 20;
        public RaceRng rng = new RaceRng();
        public List<RadioMsg> radio = new List<RadioMsg>();
        public List<Command> commands = new List<Command>();
        public int nextCommandId = 1;
        public float fastestLap;
        public int fastestCar = -1;
        public long costsCharged;
        public bool committed;
        public List<int> gridOrder = new List<int>();
        public string flag = "GREEN";
        // weekend: 0 FP1, 1 FP2, 2 FP3, 3 Q1, 4 Q2, 5 Q3, 6 Race (older saves default to the race)
        public int session = 6;
        public float sessionLeft;
        public float sessionLength;
        public bool sessionEnded;
        public float estLap = 90f;
        public bool simulating;
        public double raceStartTime;     // sim time when the lights went out for the race start
        // weather (Stage 3)
        public List<float> rainLine = new List<float>();   // hidden truth: rain intensity per minute
        public float rainChance;
        public float rain, wetness;
        public bool wasWet;              // the race itself had a wet track at some point
        // race control
        public int scMode;               // RaceControl.None / VSC / SC / Red
        public float scTimer, redTimer, redRainTimer;
        public double scDist;
        public bool scIn, scInPit;
        public float yellowAt, yellowTimer;
        public List<ControlMsg> control = new List<ControlMsg>();
        public List<Penalty> penalties = new List<Penalty>();
    }
}
