# Idle Formula Team Manager — project notes

## How to run
Open any scene (e.g. `Assets/Scenes/SampleScene`) and press **Play**. The game builds itself from code
(`Assets/FTM/Scripts`), so no scene setup is needed.

In the Editor only, the opening screen has a **DEV: PREVIEW LOBBY** button that opens the lobby with an
empty team so you can check the layout before any data is filled in. It is not a save slot.

## Where things live
| Folder | What |
|---|---|
| `Scripts/Core` | Colors and sizes (`Theme.cs`), UI kit, buttons, procedural pixel art, app/saving (`GameApp.cs`) |
| `Scripts/Data` | Data model, config loader, save system, all calculations (`Rules.cs`) |
| `Scripts/Screens` | Opening, Career Slots, Choose Team, Custom Team, Lobby and the 9 tabs |
| `Resources/FTM/GameConfig.json` | **All game values you still need to decide** |
| `Resources/FTM/Fonts` | Press Start 2P and Inter (SIL Open Font License, see the OFL files) |

Saves are JSON files in `Application.persistentDataPath/Careers/career_1..3.json`
(on Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\FTM\Careers`).

## GameConfig.json
Anything left at `-1` or `""` is **Not configured**. The game shows "Not configured" and disables the action
that depends on it. Nothing is guessed.

| Field | Meaning |
|---|---|
| `championshipName` | Shown in the top bar, League Position, Standings, History |
| `overallFormula` | `""` = overalls not shown. `"average"` = overall is the average of the stats (drivers, engineers, coordinators, car) |
| `raceOperationsPerRace` | Race operations cost per race (Finances) |
| `releaseFee` | Cost to release one of your own people when replacing them in Transfers |
| `driverTraining` / `coordinatorStudy` | `cost`, `durationEvents` (0 = immediate), `gain` (points added, capped at potential / 100) |
| `difficulties` / `defaultDifficulty` | Opponent levels (name + `rivalOffset` added to every rival rating, clamped 1-100). Picked on Choose Your Team, changeable per career in Settings |
| `carUpgrade` | `cost`, `gain`, `durationEvents` (races until it is fitted; 0 = immediate). One car upgrade at a time |
| `teams` | The 11 team cards (see below) |
| `customTeam` | `startingBudget`, `carParts` (6 numbers), `color`, `joinMode` = `"extra"` or `"replace:team5"` |
| `customTeamNames` | The premade names a custom team can choose from |
| `drivers` / `engineers` / `coordinators` | Every person in the game (team rosters + free agents) |
| `calendar` | Events in order. `pitSpeedKmh` = pit-lane limit, `rainChance` = 0-1 chance of rain over the weekend (-1 = always dry) |
| `sponsorOffers` | Sponsor deals that can be offered |
| `sponsorOffersMin` / `sponsorOffersMax` | Each slot shows a random number of offers in this range, re-rolled after every race |

Stat order — drivers: Braking, Cornering, Smoothness, Consistency, Fitness. Engineers and car parts:
Front Wing, Rear Wing, Suspension, Brakes, Gearbox, Engine. Coordinators: Aggressive, Passive, Game-plan.

### Examples of each entry (format only, the numbers are placeholders)
```json
"teams": [
  { "id": "team1", "name": "TEAM NAME", "color": "#E10600", "logo": 0, "startingBudget": 20000000,
    "carParts": [80, 80, 80, 80, 80, 80],
    "drivers": ["d1", "d2"], "engineers": ["e1", "e2", "e3"], "coordinator": "c1" }
],
"drivers": [
  { "id": "d1", "name": "First Last", "nationality": "GBR", "age": 24, "raceNumber": 4,
    "salaryPerRace": 100000, "signingBonus": 500000, "buyoutFee": -1, "contractSeasons": 2, "teamId": "team1",
    "stats": [80, 82, 78, 85, 90], "potential": [92, 90, 88, 91, 95] }
],
"engineers":    [ { "id": "e1", "name": "...", "nationality": "ITA", "age": 45, "salaryPerRace": 50000, "signingBonus": 100000, "buyoutFee": -1, "contractSeasons": 2, "teamId": "team1", "stats": [70, 72, 68, 75, 71, 69] } ],
"coordinators": [ { "id": "c1", "name": "...", "nationality": "FRA", "age": 50, "salaryPerRace": 40000, "signingBonus": 80000, "buyoutFee": -1, "contractSeasons": 2, "teamId": "team1", "stats": [60, 70, 75] } ],
"calendar": [ { "grandPrix": "Grand Prix name", "circuit": "Circuit name", "laps": 57, "overtaking": "Medium", "tireWear": "Hard", "cornering": "Easy" } ],
"sponsorOffers": [ { "id": "s1", "name": "Sponsor", "color": "#FFD700", "guaranteedPerRace": 250000, "bonusAmount": 100000,
                     "bonusCondition": "Finish in the top 10", "durationRaces": 10, "terminationFee": 500000, "slots": [] } ]
```
A person with an empty `teamId` is a free agent. `buyoutFee` is only needed to sign someone from another team.

## Race mode
`Scripts/Race`: `RaceSim` (physics, battles, tyres, pits, timing), `Coordinator` (game plans and in-race calls),
`RaceFlow` (start / resume / Save and Exit / commit results once), `RaceScreen` + `DriverPanel` (race control desk),
`GridScreen`, `ResultsScreen`. Circuits: `Resources/FTM/Tracks` (from the MIT-licensed bacinger/f1-circuits data).
1x = about 6x real time (a full Grand Prix is ~15 minutes at 1x, ~3 minutes at 5x).
Stage 2: practice + Q1-Q3 + tyre allocations, follow camera.
Stage 3 (`RaceControl`): weather (hidden rain timeline per weekend, forecasts at 5/10/15 min, track wetness,
intermediates and wets), yellow flags, Virtual Safety Car, Safety Car, red flags with a standing restart and free
tyre change, track limits, stewards' investigations, time penalties served at the next stop (or added at the
finish), drive-throughs, unsafe releases. Race control messages show on the track and in Radio History.

## Seasons
Careers run for as many seasons as you like. After the last round the dashboard shows SEASON REVIEW:
final position and champions, season-long sponsor bonuses, contracts that ended (renew 1-3 years at a new
salary + renewal fee, or release), aging (young people grow toward potential, drivers decline from 34 and
retire 36-42, staff decline from 55 and retire 60-67), F2/F3 rookies, rival transfers and rival car
development, and your car after the new regulations plus the Next Year Car Fund.
Each career keeps its own world (`CareerSave.world`), a snapshot of GameConfig that then evolves.
`seasonRules` in GameConfig: `winterLoss`, `fundPerPoint`, `rookieDrivers/Engineers/Coordinators`, `rivalRenewChance`.

## Not in this build (on purpose)
Passive income, music/sound.
The lobby never creates results, ages drivers or advances the season by itself; the race system will write
results into the save (`CareerSave.seasons[].results`) and the lobby will display them.
