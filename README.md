# Idle Formula Team Manager (F1 Racing 8-Bit)

A pixel / 8-bit Formula 1 **team manager** for iOS and PC, built in **Unity 6** (6000.6.5f1).
You run the team — drivers, engineers, coordinator, sponsors, car upgrades and pit crew — and watch
your two cars race on real circuit shapes while you make the strategy calls.

## What's in the game so far
- **Career mode** with 3 save slots, 11 teams based on the 2026 grid (or build a custom team),
  opponent difficulty from Super Easy to XTREME.
- **Team management:** sign drivers and staff from a transfer market (current F1 drivers plus F2/F3
  prospects), train drivers, coordinator study, car upgrades, pit crew training, 50+ sponsors.
- **Full race weekends:** Practice 1–3 (skippable, teaches tyre wear), Q1–Q3 knockout qualifying,
  then the race on 22 real circuits.
- **Race simulation:** tyre wear and compounds, pit stops (crew rating matters), overtaking, dirty air,
  follow-camera view, team radio, and you can override the coordinator's pit calls, tyres and driving style.
- **Race control:** weather with forecasts and intermediates/wets, yellow flags, Virtual Safety Car,
  Safety Car, red flags, track limits and stewards' penalties.
- **Endless careers:** at the end of every season, contracts expire, drivers age and develop or retire,
  rookies arrive, rivals develop their cars, and the Next Year Car Fund builds next season's car.

## Opening the project
1. Install **Unity Hub** and Unity **6000.6.5f1**.
2. In Unity Hub choose **Add → Add project from disk** and pick this folder.
3. Open `Assets/Scenes/SampleScene.unity` and press **Play**. (The game builds its screens from code.)

All code lives in `Assets/FTM/Scripts`; the game data (teams, people, sponsors, calendar) is in
`Assets/FTM/Resources/FTM/GameConfig.json`. See `Assets/FTM/README.md` for the technical notes.

Circuit outlines come from the MIT-licensed [bacinger/f1-circuits](https://github.com/bacinger/f1-circuits)
data; fonts are Inter and Press Start 2P (SIL Open Font License).
