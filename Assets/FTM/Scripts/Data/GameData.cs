using System;
using System.Collections.Generic;

namespace FTM
{
    // ------------------------------------------------------------------
    //  CONFIG (filled in by you in Resources/FTM/GameConfig.json)
    //  Any number left at -1 or any empty text means "Not configured":
    //  the game shows "Not configured" and disables whatever depends on it.
    // ------------------------------------------------------------------

    public static class Stat
    {
        public static readonly string[] Driver = { "Braking", "Cornering", "Smoothness", "Consistency", "Fitness" };
        public static readonly string[] DriverShort = { "BRA", "COR", "SMO", "CON", "FIT" };
        public static readonly string[] Part = { "Front Wing", "Rear Wing", "Suspension", "Brakes", "Gearbox", "Engine" };
        public static readonly string[] Coord = { "Aggressive", "Passive", "Game-plan" };
    }

    [Serializable]
    public class PersonDef
    {
        public string id = "";
        public string name = "";
        public string nationality = "";   // e.g. "GBR"
        public int age = -1;
        public long salaryPerRace = -1;
        public long signingBonus = -1;
        public long buyoutFee = -1;       // only needed for people currently on another team
        public int contractSeasons = -1;
        public string teamId = "";        // empty = free agent
    }

    [Serializable]
    public class DriverDef : PersonDef
    {
        public int raceNumber = -1;
        public int[] stats = new int[0];      // Braking, Cornering, Smoothness, Consistency, Fitness
        public int[] potential = new int[0];  // max for each stat
    }

    [Serializable]
    public class EngineerDef : PersonDef
    {
        public int[] stats = new int[0];      // Front Wing, Rear Wing, Suspension, Brakes, Gearbox, Engine
    }

    [Serializable]
    public class CoordinatorDef : PersonDef
    {
        public int[] stats = new int[0];      // Aggressive, Passive, Game-plan
    }

    [Serializable]
    public class TeamDef
    {
        public string id = "";
        public string name = "";
        public string color = "";             // "#RRGGBB"
        public int logo = -1;                 // index into the premade logo set
        public long startingBudget = -1;
        public int[] carParts = new int[0];   // 6 ratings, same order as Stat.Part
        public string[] drivers = new string[0];      // 2 driver ids
        public string[] engineers = new string[0];    // 3 engineer ids
        public string coordinator = "";
        public int pitCrew = -1;              // pit crew rating 0-100 (stop speed and reliability)
    }

    [Serializable]
    public class CustomTeamDef
    {
        public long startingBudget = -1;
        public int[] carParts = new int[0];
        public string joinMode = "";          // "extra" = joins as an extra team, "replace:<teamId>" = takes that team's place
        public int pitCrew = -1;
        public string color = "";
    }

    [Serializable]
    public class EventDef
    {
        public string grandPrix = "";
        public string circuit = "";
        public int laps = -1;
        public string overtaking = "";        // "Easy" | "Medium" | "Hard"
        public string tireWear = "";
        public string cornering = "";
        public string trackId = "";           // file in Resources/FTM/Tracks
        public int pitSpeedKmh = 80;          // published pit-lane limit for the event
        public float rainChance = -1f;        // chance of rain over the weekend (0-1), -1 = Not configured (dry)
    }

    [Serializable]
    public class SponsorOfferDef
    {
        public string id = "";
        public string name = "";
        public string color = "";
        public long guaranteedPerRace = -1;
        public long bonusAmount = 0;
        public string bonusCondition = "";
        public int durationRaces = -1;
        public long terminationFee = -1;      // cost to end this deal early (-1 = can't be replaced yet)
        public int[] slots = new int[0];      // which slots (1-6) it is offered for; empty = any slot
    }

    [Serializable]
    public class CostRule
    {
        public long cost = -1;
        public int durationEvents = -1;
        public int gain = -1;
    }

    /// <summary>Season rollover rules (-1 = Not configured: that part of the rollover is skipped).</summary>
    [Serializable]
    public class SeasonRules
    {
        public int winterLoss = -1;            // points every car part loses over the winter (new regulations)
        public long fundPerPoint = -1;         // Next Year Car Fund money for +1 point on a part with 80-rated engineers
        public int rookieDrivers = 0;          // new F2/F3 graduates entering free agency each season
        public int rookieEngineers = 0;
        public int rookieCoordinators = 0;
        public int rivalRenewChance = 65;      // % chance a rival team keeps someone whose contract ran out
    }

    [Serializable]
    public class DifficultyDef
    {
        public string name = "";
        public int rivalOffset;       // added to every rival rating (car, drivers, staff), clamped 1-100
    }

    [Serializable]
    public class GameConfig
    {
        public string _readme = "";
        public string championshipName = "";
        public string rulesVersion = "FIA 2026 F1 Sporting Regulations, Section B, Issue 09 (2026-10-01)";
        public string overallFormula = "";          // "" = not configured, "average" = mean of the stats
        public long raceOperationsPerRace = -1;
        public long releaseFee = -1;                // cost to release one of your own people when replacing them
        public CostRule driverTraining = new CostRule();
        public CostRule coordinatorStudy = new CostRule();
        public CostRule carUpgrade = new CostRule();
        public CostRule pitCrewUpgrade = new CostRule();
        public SeasonRules seasonRules = new SeasonRules();
        public List<long> prizeMoney = new List<long>();   // per car, P1 first
        public List<int> racePoints = new List<int>();     // full-points race, P1 first
        public int sponsorOffersMin = 1;          // each sponsor slot shows a random number of offers between min and max,
        public int sponsorOffersMax = 3;          // re-rolled after every race (not when you reopen the slot)
        public int defaultDifficulty = 2;
        public List<DifficultyDef> difficulties = new List<DifficultyDef>();
        public List<TeamDef> teams = new List<TeamDef>();
        public CustomTeamDef customTeam = new CustomTeamDef();
        public List<string> customTeamNames = new List<string>();
        public List<DriverDef> drivers = new List<DriverDef>();
        public List<EngineerDef> engineers = new List<EngineerDef>();
        public List<CoordinatorDef> coordinators = new List<CoordinatorDef>();
        public List<EventDef> calendar = new List<EventDef>();
        public List<SponsorOfferDef> sponsorOffers = new List<SponsorOfferDef>();
    }

    // ------------------------------------------------------------------
    //  SAVE DATA (one per career slot)
    // ------------------------------------------------------------------

    [Serializable]
    public class Job
    {
        public bool active;
        public int stat = -1;
        public int remainingEvents;
        public int totalEvents;
        public long cost;
    }

    [Serializable]
    public class DriverState
    {
        public bool filled;
        public DriverDef def = new DriverDef();
        public Job training = new Job();
        public List<ChangeEntry> recent = new List<ChangeEntry>();
    }

    [Serializable]
    public class EngineerState
    {
        public bool filled;
        public EngineerDef def = new EngineerDef();
    }

    [Serializable]
    public class CoordinatorState
    {
        public bool filled;
        public CoordinatorDef def = new CoordinatorDef();
        public Job study = new Job();
    }

    [Serializable]
    public class ChangeEntry
    {
        public string when = "";   // event or season
        public string stat = "";
        public int change;
        public string note = "";
    }

    [Serializable]
    public class SponsorState
    {
        public bool active;
        public string offerId = "";
        public string name = "";
        public string color = "";
        public long guaranteedPerRace;
        public long bonusAmount;
        public string bonusCondition = "";
        public int racesRemaining;
        public long terminationFee = -1;
    }

    [Serializable]
    public class Classified
    {
        public string driverId = "";
        public string driverName = "";
        public string teamId = "";
        public string teamName = "";
        public int position;       // 0 = not classified
        public int points;
        public bool pole;
    }

    /// <summary>Written by the race system later. The lobby only reads these.</summary>
    [Serializable]
    public class RaceResult
    {
        public int round;
        public string grandPrix = "";
        public int driver1Pos;
        public int driver2Pos;
        public int teamPoints;
        public string winner = "";
        public List<Classified> classification = new List<Classified>();
    }

    [Serializable]
    public class SeasonRecord
    {
        public int season;
        public List<RaceResult> results = new List<RaceResult>();
        public bool teamChampion;
        public bool driverChampion;
        public int teamPosition;                 // final constructors' position (0 = season not finished)
        public string championTeam = "", championDriver = "";
        public List<string> notes = new List<string>();   // season review lines
    }

    /// <summary>A rival team's car and roster in this career (changes every season).</summary>
    [Serializable]
    public class WorldTeam
    {
        public string id = "";
        public int[] carParts = new int[6];
        public int pitCrew = 60;
        public List<string> drivers = new List<string>();
        public List<string> engineers = new List<string>();
        public string coordinator = "";
    }

    /// <summary>
    /// This career's copy of everyone in the sport. Starts as a snapshot of GameConfig and then lives its own
    /// life: people age, develop, change teams, retire; rookies arrive; rival cars develop each winter.
    /// </summary>
    [Serializable]
    public class WorldState
    {
        public bool active;
        public List<DriverDef> drivers = new List<DriverDef>();
        public List<EngineerDef> engineers = new List<EngineerDef>();
        public List<CoordinatorDef> coordinators = new List<CoordinatorDef>();
        public List<WorldTeam> teams = new List<WorldTeam>();
        public List<string> retired = new List<string>();     // names of people who retired (for the season review)
        public int nextId = 1;
    }

    [Serializable]
    public class CareerSave
    {
        public int version = 1;
        public int slot;
        public string rulesVersion = "";
        public string baseTeamId = "";        // the real-team card you picked, or "custom"
        public int difficulty = -1;           // index into GameConfig.difficulties (-1 = default)
        public string teamName = "";
        public string teamColor = "";
        public int logo = -1;
        public long balance;
        public int[] carParts = new int[6];
        public long nextYearFund;
        public int[] fundWeights = { 2, 2, 2, 2, 2, 2 };   // Next Year Car Fund priority per part (0-3)
        public WorldState world = new WorldState();
        public Job carUpgrade = new Job();     // one car upgrade at a time; stat = part index
        public int pitCrew = -1;              // your pit crew rating (-1 = take it from the team's config)
        public Job pitCrewJob = new Job();    // pit crew training in progress
        public string activeRace = "";        // JSON of an in-progress race weekend (empty = none)
        public EventSummary lastEvent = new EventSummary();
        public DriverState[] drivers = { new DriverState(), new DriverState() };
        public EngineerState[] engineers = { new EngineerState(), new EngineerState(), new EngineerState() };
        public CoordinatorState coordinator = new CoordinatorState();
        public SponsorState[] sponsors = { new SponsorState(), new SponsorState(), new SponsorState(), new SponsorState(), new SponsorState(), new SponsorState() };
        public List<string> takenPeople = new List<string>();  // people you signed away from the pool
        public int season = 1;
        public int nextRound = 1;
        public List<SeasonRecord> seasons = new List<SeasonRecord>();
        public double playtimeSeconds;
        public long lastPlayedTicks;
        public long createdTicks;
    }

    /// <summary>Money breakdown of the most recently completed event (shown on the results screen).</summary>
    [Serializable]
    public class EventSummary
    {
        public int round;
        public string grandPrix = "";
        public long costs, prize, sponsorPay, bonuses;
        public List<string> lines = new List<string>();   // sponsor bonus lines
        public long Net { get { return prize + sponsorPay + bonuses - costs; } }
    }

    [Serializable]
    public class AudioSettings
    {
        public int music = 80;
        public int sfx = 80;
    }
}
