# SC2 Direct Strike Replay Parser

A .NET parser for extracting Direct Strike-specific data from decoded StarCraft II replay files.

The parser builds on [`s2protocol.NET`](https://www.nuget.org/packages/s2protocol.NET/) and turns decoded replay data into Direct Strike-focused models for analysis, stats, tooling, and replay processing workflows.

## What It Extracts

- Replay identity, version/build, game time, duration, game mode, and TE status.
- Players, observers, teams, game positions, commanders, selected races, APM, and results.
- Objective timings, winner team, middle-control changes, refinery timings, tier upgrades, and filtered upgrade timings.
- Player stats, build-unit names, grouped spawn waves, spawn-unit positions, and unit death positions when replay data contains them.
- DTO output with compatibility hashes and breakpoint-oriented spawn summaries.

## Main APIs

```csharp
using Sc2DirectStrike.Parser;
using s2protocol.NET.Models;

Sc2Replay replay = /* decoded with s2protocol.NET */;

DirectStrikeReplay parsed = Sc2DirectStrikeParser.Parse(replay);
ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);
```

`Parse` returns the Direct Strike domain model. `ParseDto` returns a compact DTO shape intended for downstream storage, comparison, or API output.

## Changelog

### 0.2.2 - 2026-07-15

- Improved spawn-unit matching for commander-decorated and Starlight/Lightweight unit names while continuing to exclude produced units.
- Added AFK duration handling based on the first zero-income stats event after the AFK signal.
- Changed `Breakpoint.All` cumulative killed, lost, and upgrade values to use each player's final positive-income stats without changing compatibility hashes.
- Added replay-backed regression coverage and end-screen validation fixtures for spawn counts and final player stats.

## Requirements

- .NET SDK `10.0.203`
- Target framework `net10.0`

## Setup

From the repository root:

```powershell
dotnet restore src\src.slnx
dotnet build src\src.slnx
dotnet test src\Sc2DirectStrike.Tests\Sc2DirectStrike.Tests.csproj
```

Current verified test baseline: `131` total, `127` passing, `4` skipped.

Stress replay tests are skipped by default. Run them explicitly with:

```powershell
$env:SC2DIRECTSTRIKE_RUN_STRESS_TESTS = "1"
dotnet test src\Sc2DirectStrike.Tests\Sc2DirectStrike.Tests.csproj --filter TestCategory=Stress
```

Run parser benchmarks with:

```powershell
dotnet run -c Release --project src\Sc2DirectStrike.Benchmarks\Sc2DirectStrike.Benchmarks.csproj
```

## Project Layout

- `src/src.slnx` - solution file containing all projects.
- `src/Sc2DirectStrike.Parser` - parser library and public models.
- `src/Sc2DirectStrike.Tests` - MSTest test project with replay fixtures.
- `src/Sc2DirectStrike.Tests/testdata` - sample `.SC2Replay` files used by tests and benchmarks.
- `src/Sc2DirectStrike.Benchmarks` - BenchmarkDotNet parser benchmarks.
- `src/samples/Sc2DirectStrike.DuplicateSample` - sample tool for duplicate replay analysis.
- `src/samples/Sc2DirectStrike.IncomeSample` - sample tool for replay income calculations.
- `src/samples/Sc2DirectStrike.ParserCompareSample` - sample tool for parser comparison workflows.
- `src/Sc2DirectStrike.WasmSmoke` - WebAssembly smoke project for parser compatibility checks.

## Tech Stack

- Target framework: `net10.0`
- Language version: `latest`
- Parser dependency: `s2protocol.NET` `0.9.4`
- Test framework: MSTest `4.0.2`
- Benchmark framework: BenchmarkDotNet `0.15.8`
