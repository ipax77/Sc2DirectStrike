
# SC2 Direct Strike Replay Parser

Extracts relevant information from decoded StarCraft II replays that match the
Direct Strike custom map setup.

## Agent Notes

Use this file as the lightweight project briefing before making changes.

## Project Layout

- `src/src.slnx` - solution file.
- `src/Sc2DirectStrike.Parser` - parser library.
- `src/Sc2DirectStrike.Tests` - MSTest test project with replay fixtures.
- `src/Sc2DirectStrike.Tests/testdata` - sample `.SC2Replay` files used by tests.

## Tech Stack

- .NET SDK/runtime target: `net10.0`.
- Language version: `latest`.
- Nullable reference types and implicit usings are enabled.
- Parser dependency: `s2protocol.NET` `0.9.1.1`.
- Test framework: MSTest `4.0.2`.

The parser project has analyzers enabled, treats warnings as errors, and treats
nullable warnings as errors. Keep edits warning-clean.

## Common Commands

From the repository root:

```powershell
dotnet build src\src.slnx
dotnet test src\Sc2DirectStrike.Tests\Sc2DirectStrike.Tests.csproj
```

The current baseline is 13 passing tests.

In the Codex sandbox, `dotnet test` may fail while reading the user-level
NuGet config at `C:\Users\pax77\AppData\Roaming\NuGet\NuGet.Config`. If that
happens, rerun the same command with escalated permissions so restore/test can
read the local NuGet configuration.

## Parser Behavior

The main entry point is `Sc2DirectStrikeParser.Parse(Sc2Replay replay)`.

Current behavior:

- Requires a non-null replay and replay details.
- Accepts replay titles that start with `Direct Strike`, case-insensitively.
- Throws `InvalidOperationException` for non-Direct Strike replay titles.
- Sets `GameTime` from `replay.Details.DateTimeUTC`.
- Sets `TE` when the replay title ends with `TE`, case-insensitively.
- Sets `BaseBuild` and `Duration` from replay metadata when available.
- Parses players from replay details into `DirectStrikePlayer` records.
- Maps player race text to the `Commander` enum when possible.
- Maps metadata APM, result, and selected race onto players when available.
- Matches metadata players by 1-based `PlayerID` as details-list index, then
  falls back to same-order matching.

Some model fields exist but are not populated yet, including `GameMode`,
`WinnerTeam`, and player `TeamId`.

## Testing Notes

The test project copies everything under `testdata` to the output directory.
Existing tests decode each replay fixture with `s2protocol.NET`, call
`Sc2DirectStrikeParser.Parse`, and verify that game time and player data are
present.

When extending parsing behavior, prefer adding or updating fixture-backed tests
in `ParseTests.cs`.
