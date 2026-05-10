# SC2 Direct Strike Replay Parser

Extracts Direct Strike-specific data from decoded StarCraft II replay files.

## Agent Notes

Use this file as the lightweight project briefing before making changes.

## Project Layout

- `src/src.slnx` - solution file containing all projects.
- `src/Sc2DirectStrike.Parser` - parser library.
- `src/Sc2DirectStrike.Tests` - MSTest test project with replay fixtures.
- `src/Sc2DirectStrike.Tests/testdata` - sample `.SC2Replay` files used by tests and benchmarks.
- `src/Sc2DirectStrike.Benchmarks` - BenchmarkDotNet project for parser replay benchmarks.

## Setup

Install the .NET 10 SDK. This workspace currently uses SDK `10.0.203`, and all
projects target `net10.0`.

From the repository root:

```powershell
dotnet restore src\src.slnx
dotnet build src\src.slnx
dotnet test src\Sc2DirectStrike.Tests\Sc2DirectStrike.Tests.csproj
```

The verified baseline is 85 tests total: 81 passing and 4 skipped.

Stress replay tests are skipped by default. Run them explicitly with:

```powershell
$env:SC2DIRECTSTRIKE_RUN_STRESS_TESTS = "1"
dotnet test src\Sc2DirectStrike.Tests\Sc2DirectStrike.Tests.csproj --filter TestCategory=Stress
```

Run parser benchmarks with:

```powershell
dotnet run -c Release --project src\Sc2DirectStrike.Benchmarks\Sc2DirectStrike.Benchmarks.csproj
```

In the Codex sandbox, `dotnet restore`, `dotnet build`, or `dotnet test` may
fail while reading the user-level NuGet config at
`C:\Users\pax77\AppData\Roaming\NuGet\NuGet.Config`. If that happens, rerun
the same command with escalated permissions so NuGet can read the local
configuration.

## Tech Stack

- Target framework: `net10.0`.
- Language version: `latest`.
- Nullable reference types and implicit usings are enabled.
- Parser dependency: `s2protocol.NET` `0.9.1.1`.
- Test framework: MSTest `4.0.2`.
- Benchmark framework: BenchmarkDotNet `0.15.8`.

The parser project has analyzers enabled, treats build warnings as errors, and
treats nullable warnings as errors. Keep edits warning-clean.

## Parser Behavior

The main entry point is `Sc2DirectStrikeParser.Parse(Sc2Replay replay)`.

Current behavior:

- Requires a non-null replay and replay details.
- Accepts replay titles that start with `Direct Strike`, case-insensitively.
- Throws `InvalidOperationException` for non-Direct Strike replay titles.
- Sets `GameTime` from replay details, `TE` from the replay title, and
  `BaseBuild` from replay metadata.
- Parses game mode from gameloop-zero mode and mutation upgrade events.
- Parses players from replay details and metadata.
- Parses observers from lobby/init data.
- Maps player identity, clan, toon, slot, APM, result, selected race, commander,
  team, and game position when the replay contains the needed data.
- Uses tracker events to populate player stats, refinery times, tier upgrades,
  filtered upgrade timings, middle control changes, objective timings, winner
  team, and grouped spawn units.
- Sets replay `Duration` from the Nexus/Planetary death time, or from the
  longest player duration when no objective death is available.
- Matches metadata and tracker players by player id, toon, slot, or details-list
  fallback depending on the available replay data.

## Testing Notes

The test project copies everything under `testdata` to the output directory.
Existing tests decode replay fixtures with `s2protocol.NET`, call
`Sc2DirectStrikeParser.Parse`, and verify replay, player, tracker, upgrade,
layout, and spawn behavior.

When extending parsing behavior, prefer adding or updating fixture-backed tests
in `ParseTests.cs`, `ParseTests.Tracker.cs`, or `ParseTests.Spawns.cs`.
