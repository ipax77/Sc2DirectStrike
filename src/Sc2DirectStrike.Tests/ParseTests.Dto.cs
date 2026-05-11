using s2protocol.NET;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.Tests;

public sealed partial class ParseTests
{
    private const double ExpectedDtoGameLoopsPerSecond = 22.4D;
    private const double ExpectedDtoBaseIncomePerSecond = 7.5D;
    private const double ExpectedDtoRefineryIncomePerSecond = 0.5D;
    private const double ExpectedDtoMiddleIncomePerSecond = 1D;
    private const int ExpectedDtoIncomeFormulaTolerance = 300;

    private static readonly (Breakpoint Breakpoint, int Gameloop)[] ExpectedDtoBreakpointGameloops =
    [
        (Breakpoint.Min5, 6_720),
        (Breakpoint.Min10, 13_440),
        (Breakpoint.Min15, 20_160),
    ];

    private static readonly int[] ExpectedDtoRefineryCosts = [150, 225, 300, 375, 500];

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanParseReplayDtoMetadataAndPlayers(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);
        DirectStrikeReplay parsedReplay = Sc2DirectStrikeParser.Parse(replay);

        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);

        Assert.AreEqual(replay.FileName, dto.FileName);
        if (dto.Duration >= ToExpectedDtoTime(6_720) && dto.Players.Count > 0)
        {
            Assert.IsNotEmpty(dto.CompatHash);
        }
        else
        {
            Assert.AreEqual(string.Empty, dto.CompatHash);
        }

        Assert.AreEqual(replay.Details?.Title ?? replay.Metadata?.Title ?? string.Empty, dto.Title);
        Assert.IsNotEmpty(dto.Version);
        Assert.AreEqual(parsedReplay.GameMode, dto.GameMode);
        Assert.AreEqual(parsedReplay.Players.Select(player => player.Region).FirstOrDefault(region => region != 0), dto.RegionId);
        Assert.AreEqual(parsedReplay.GameTime, dto.Gametime);
        Assert.AreEqual(parsedReplay.Duration, dto.Duration);
        Assert.AreEqual(parsedReplay.CannonTime, dto.Cannon);
        Assert.AreEqual(parsedReplay.BunkerTime, dto.Bunker);
        Assert.AreEqual(parsedReplay.WinnerTeam, dto.WinnerTeam);
        Assert.AreEqual(parsedReplay.FirstMiddleControlTeam, dto.FirstTeamCrossedMiddle);
        CollectionAssert.AreEqual(parsedReplay.MiddleChanges, dto.MiddleChanges.ToArray());
        Assert.HasCount(parsedReplay.Players.Count, dto.Players);

        for (int i = 0; i < parsedReplay.Players.Count; i++)
        {
            DirectStrikePlayer expectedPlayer = parsedReplay.Players[i];
            ReplayPlayerDto actualPlayer = dto.Players.ElementAt(i);
            Assert.IsNotEmpty(actualPlayer.CompatHash);
            Assert.AreEqual(expectedPlayer.Name, actualPlayer.Name);
            Assert.AreEqual(expectedPlayer.Clan, actualPlayer.Clan);
            Assert.AreEqual(expectedPlayer.Commander, actualPlayer.Race);
            Assert.AreEqual(ExpectedSelectedRaceCommander(expectedPlayer.SelectedRace), actualPlayer.SelectedRace);
            Assert.AreEqual(expectedPlayer.TeamId, actualPlayer.TeamId);
            Assert.AreEqual(expectedPlayer.GamePos, actualPlayer.GamePos);
            Assert.AreEqual(expectedPlayer.Result, actualPlayer.Result);
            Assert.AreEqual(expectedPlayer.Duration, actualPlayer.Duration);
            Assert.AreEqual((int)Math.Round(expectedPlayer.APM), actualPlayer.Apm);
            Assert.AreEqual(0, actualPlayer.Messages);
            Assert.AreEqual(0, actualPlayer.Pings);
            Assert.IsFalse(actualPlayer.IsMvp);
            Assert.AreEqual(expectedPlayer.Id, actualPlayer.Player.PlayerId);
            Assert.AreEqual(expectedPlayer.Name, actualPlayer.Player.Name);
            Assert.AreEqual(expectedPlayer.Region, actualPlayer.Player.ToonId.Region);
            Assert.AreEqual(expectedPlayer.Realm, actualPlayer.Player.ToonId.Realm);
            Assert.AreEqual(expectedPlayer.Id, actualPlayer.Player.ToonId.Id);
            CollectionAssert.AreEqual(expectedPlayer.TierUpgrades, actualPlayer.TierUpgrades.ToArray());
            CollectionAssert.AreEqual(expectedPlayer.RefineryTimes, actualPlayer.Refineries.ToArray());
            CollectionAssert.AreEqual(
                expectedPlayer.Upgrades
                    .OrderBy(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key)
                    .ToArray(),
                actualPlayer.Upgrades.Select(upgrade => upgrade.Name).ToArray());
            CollectionAssert.AreEqual(
                expectedPlayer.Upgrades
                    .OrderBy(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value)
                    .ToArray(),
                actualPlayer.Upgrades.Select(upgrade => upgrade.Time).ToArray());
        }
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike (10161).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanParseReplayDtoBreakpointSpawns(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);
        DirectStrikeReplay parsedReplay = Sc2DirectStrikeParser.Parse(replay);

        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);
        MiddleControlHelper middleControl = new(dto);

        for (int i = 0; i < parsedReplay.Players.Count; i++)
        {
            DirectStrikePlayer expectedPlayer = parsedReplay.Players[i];
            ReplayPlayerDto actualPlayer = dto.Players.ElementAt(i);
            Dictionary<DirectStrikePlayerSpawn, int> expectedArmyValues = GetExpectedDtoArmyValues(expectedPlayer);
            ExpectedDtoBreakpointSpawn[] expectedSpawns = [.. GetExpectedDtoBreakpointSpawns(expectedPlayer)];

            CollectionAssert.AreEqual(
                expectedSpawns.Select(spawn => spawn.Breakpoint).ToArray(),
                actualPlayer.Spawns.Select(spawn => spawn.Breakpoint).ToArray(),
                $"Unexpected DTO breakpoints for player index {i}.");

            for (int j = 0; j < expectedSpawns.Length; j++)
            {
                DirectStrikePlayerSpawn expectedSpawn = expectedSpawns[j].Spawn;
                DirectStrikePlayerStats expectedStats = expectedSpawn.SummaryStats
                    ?? throw new InvalidOperationException("Expected DTO spawn must have summary stats.");
                SpawnDto actualSpawn = actualPlayer.Spawns.ElementAt(j);

                Assert.AreEqual(GetExpectedDtoIncome(expectedPlayer, expectedStats.Gameloop), actualSpawn.Income);
                Assert.IsLessThanOrEqualTo(
                    ExpectedDtoIncomeFormulaTolerance,
                    Math.Abs(actualSpawn.Income - GetExpectedDtoFormulaIncome(expectedPlayer, expectedStats.Gameloop, middleControl)));
                Assert.AreEqual(expectedPlayer.RefineryTimes.Count(refinery => refinery <= expectedStats.Time), actualSpawn.GasCount);
                Assert.AreEqual(expectedArmyValues[expectedSpawn], actualSpawn.ArmyValue);
                Assert.AreEqual(expectedStats.MineralsKilledArmy, actualSpawn.KilledValue);
                Assert.AreEqual(expectedStats.MineralsLostArmy, actualSpawn.LostValue);
                Assert.AreEqual(expectedStats.MineralsUsedCurrentTechnology, actualSpawn.UpgradeSpent);
                AssertUnitDtosAreEquivalent(expectedSpawn, actualSpawn);
            }
        }
    }

    [TestMethod]
    public async Task ReplayDtoOmitsBreakpointsAfterLeaverDuration()
    {
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10161).SC2Replay");

        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);

        ReplayPlayerDto gamePos4 = dto.Players.Single(player => player.GamePos == 4);
        ReplayPlayerDto gamePos5 = dto.Players.Single(player => player.GamePos == 5);

        CollectionAssert.AreEqual(
            new[] { Breakpoint.All },
            gamePos4.Spawns.Select(spawn => spawn.Breakpoint).ToArray());
        CollectionAssert.AreEqual(
            new[] { Breakpoint.All },
            gamePos5.Spawns.Select(spawn => spawn.Breakpoint).ToArray());
    }

    [TestMethod]
    public async Task ReplayDtoIncomeIsAccumulatedInsteadOfCurrentRate()
    {
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10124).SC2Replay");
        DirectStrikeReplay parsedReplay = Sc2DirectStrikeParser.Parse(replay);

        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);

        Assert.IsTrue(parsedReplay.Players.Zip(dto.Players).Any(pair =>
        {
            Dictionary<Breakpoint, DirectStrikePlayerSpawn> expectedSpawnsByBreakpoint = GetExpectedDtoBreakpointSpawns(pair.First)
                .ToDictionary(spawn => spawn.Breakpoint, spawn => spawn.Spawn);
            return pair.Second.Spawns.Any(spawn =>
                expectedSpawnsByBreakpoint.TryGetValue(spawn.Breakpoint, out DirectStrikePlayerSpawn? expectedSpawn)
                && expectedSpawn.SummaryStats is not null
                && spawn.Income != expectedSpawn.SummaryStats.MineralsCollectionRate);
        }));
    }

    [TestMethod]
    public async Task ReplayDtoArmyValueDoesNotUseRawActiveForces()
    {
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10124).SC2Replay");
        DirectStrikeReplay parsedReplay = Sc2DirectStrikeParser.Parse(replay);

        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);

        Assert.IsTrue(parsedReplay.Players.Zip(dto.Players).Any(pair =>
        {
            Dictionary<Breakpoint, DirectStrikePlayerSpawn> expectedSpawnsByBreakpoint = GetExpectedDtoBreakpointSpawns(pair.First)
                .ToDictionary(spawn => spawn.Breakpoint, spawn => spawn.Spawn);
            return pair.Second.Spawns.Any(spawn =>
                expectedSpawnsByBreakpoint.TryGetValue(spawn.Breakpoint, out DirectStrikePlayerSpawn? expectedSpawn)
                && expectedSpawn.SummaryStats is not null
                && spawn.ArmyValue != expectedSpawn.SummaryStats.MineralsUsedActiveForces);
        }));
    }

    [TestMethod]
    public async Task ReplayDtoCompatHashIsDeterministic()
    {
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10060).SC2Replay");

        ReplayDto first = Sc2DirectStrikeParser.ParseDto(replay);
        ReplayDto second = Sc2DirectStrikeParser.ParseDto(replay);

        Assert.IsNotEmpty(first.CompatHash);
        Assert.AreEqual(first.CompatHash, second.CompatHash);
    }

    [TestMethod]
    public async Task ReplayDtoCompatHashDoesNotUseFileName()
    {
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10060).SC2Replay");

        ReplayDto first = Sc2DirectStrikeParser.ParseDto(replay);
        typeof(Sc2Replay).GetProperty(nameof(Sc2Replay.FileName))?.SetValue(replay, "renamed-copy.SC2Replay");
        ReplayDto second = Sc2DirectStrikeParser.ParseDto(replay);

        Assert.AreEqual(first.CompatHash, second.CompatHash);
        Assert.AreNotEqual(first.FileName, second.FileName);
    }

    [TestMethod]
    public void ReplayDtoCompatHashDoesNotUseGametime()
    {
        ReplayPlayerDto player = CreateCompatHashPlayer(includeMin5: true);

        string compatHash = InvokeCreateCompatHash("Direct Strike", "1.2.3", GameMode.Standard, 2, 100, TimeSpan.FromMinutes(5), [player]);

        Assert.IsNotEmpty(compatHash);
        Assert.IsFalse(compatHash.Contains(DateTime.UnixEpoch.ToString("O"), StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplayDtoPlayerCompatHashAllowsEmptySnapshot()
    {
        DirectStrikePlayer player = new()
        {
            Name = "Player",
            Clan = "Clan",
            Commander = Commander.Protoss,
            SelectedRace = Race.Random,
            TeamId = 1,
            GamePos = 1,
            Region = 2,
            Realm = 1,
            Id = 123,
        };
        PlayerDto playerDto = new()
        {
            PlayerId = 123,
            Name = "Player",
            ToonId = new()
            {
                Region = 2,
                Realm = 1,
                Id = 123,
            },
        };

        Assert.IsNotEmpty(InvokeCreatePlayerCompatHash(player, playerDto, Commander.Random, snapshot: null));
    }

    [TestMethod]
    public void ReplayDtoCompatHashIsEmptyBeforeFiveMinutes()
    {
        ReplayPlayerDto player = CreateCompatHashPlayer(includeMin5: false);

        string compatHash = InvokeCreateCompatHash("Direct Strike", "1.2.3", GameMode.Standard, 2, 100, TimeSpan.FromMinutes(4), [player]);

        Assert.AreEqual(string.Empty, compatHash);
    }

    private static IEnumerable<ExpectedDtoBreakpointSpawn> GetExpectedDtoBreakpointSpawns(DirectStrikePlayer player)
    {
        DirectStrikePlayerSpawn[] statsBackedSpawns = [.. player.Spawns.Where(spawn => spawn.SummaryStats is { MineralsCollectionRate: > 0 })];
        if (statsBackedSpawns.Length == 0)
        {
            yield break;
        }

        foreach ((Breakpoint breakpoint, int gameloop) in ExpectedDtoBreakpointGameloops)
        {
            if (player.DurationGameloop > 0 && gameloop > player.DurationGameloop)
            {
                continue;
            }

            yield return new(
                breakpoint,
                statsBackedSpawns
                    .OrderBy(spawn => Math.Abs(spawn.EndGameloop - gameloop))
                    .ThenBy(spawn => spawn.EndGameloop)
                    .First());
        }

        yield return new(Breakpoint.All, statsBackedSpawns[^1]);
    }

    private static Dictionary<DirectStrikePlayerSpawn, int> GetExpectedDtoArmyValues(DirectStrikePlayer player)
    {
        Dictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn = new(player.Spawns.Count);
        int cumulativePreviousArmyValue = 0;
        foreach (DirectStrikePlayerSpawn spawn in player.Spawns)
        {
            if (spawn.SummaryStats is not { } stats)
            {
                continue;
            }

            int armyValue = (stats.MineralsUsedActiveForces - cumulativePreviousArmyValue + stats.MineralsLostArmy) / 2;
            armyValuesBySpawn.Add(spawn, armyValue);
            cumulativePreviousArmyValue += armyValue;
        }

        return armyValuesBySpawn;
    }

    private static int GetExpectedDtoIncome(DirectStrikePlayer player, int targetGameloop)
    {
        if (targetGameloop <= 0 || player.Stats.Count == 0)
        {
            return 0;
        }

        double income = 0;
        int previousGameloop = 0;
        int previousRate = player.Stats[0].MineralsCollectionRate;

        foreach (DirectStrikePlayerStats stat in player.Stats)
        {
            int currentGameloop = Math.Min(stat.Gameloop, targetGameloop);
            if (currentGameloop > previousGameloop)
            {
                income += previousRate * ((currentGameloop - previousGameloop) / ExpectedDtoGameLoopsPerSecond) / 60D;
                previousGameloop = currentGameloop;
            }

            previousRate = stat.MineralsCollectionRate;
            if (stat.Gameloop >= targetGameloop)
            {
                return (int)income - GetExpectedDtoRefineryCost(player, targetGameloop);
            }
        }

        if (previousGameloop < targetGameloop)
        {
            income += previousRate * ((targetGameloop - previousGameloop) / ExpectedDtoGameLoopsPerSecond) / 60D;
        }

        return (int)income - GetExpectedDtoRefineryCost(player, targetGameloop);
    }

    private static int GetExpectedDtoRefineryCost(DirectStrikePlayer player, int targetGameloop)
    {
        int refineryCount = player.RefineryTimes.Count(refinery => (int)(refinery.TotalSeconds * ExpectedDtoGameLoopsPerSecond) < targetGameloop);
        int cost = 0;
        for (int i = 0; i < refineryCount && i < ExpectedDtoRefineryCosts.Length; i++)
        {
            cost += ExpectedDtoRefineryCosts[i];
        }

        return cost;
    }

    private static int GetExpectedDtoFormulaIncome(DirectStrikePlayer player, int targetGameloop, MiddleControlHelper middleControl)
    {
        (TimeSpan team1MiddleControl, TimeSpan team2MiddleControl) = middleControl.GetControl(ToExpectedDtoTime(targetGameloop));
        double middleControlSeconds = player.TeamId == 1
            ? team1MiddleControl.TotalSeconds
            : player.TeamId == 2
                ? team2MiddleControl.TotalSeconds
                : 0D;
        double middleIncome = middleControlSeconds * ExpectedDtoMiddleIncomePerSecond;
        double targetSeconds = targetGameloop / ExpectedDtoGameLoopsPerSecond;
        double income = (targetSeconds * ExpectedDtoBaseIncomePerSecond) + middleIncome;

        foreach (TimeSpan refinery in player.RefineryTimes)
        {
            int refineryGameloop = (int)(refinery.TotalSeconds * ExpectedDtoGameLoopsPerSecond);
            if (refineryGameloop < targetGameloop)
            {
                income += ((targetGameloop - refineryGameloop) / ExpectedDtoGameLoopsPerSecond) * ExpectedDtoRefineryIncomePerSecond;
            }
        }

        return (int)income - GetExpectedDtoRefineryCost(player, targetGameloop);
    }

    private static TimeSpan ToExpectedDtoTime(int gameloop)
    {
        return TimeSpan.FromSeconds(gameloop / ExpectedDtoGameLoopsPerSecond);
    }

    private static void AssertUnitDtosAreEquivalent(DirectStrikePlayerSpawn expectedSpawn, SpawnDto actualSpawn)
    {
        UnitDto[] expectedUnits = [.. expectedSpawn.Units
            .GroupBy(unit => unit.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new UnitDto
            {
                Name = group.Key,
                Count = group.Count(),
                Positions = [.. group.SelectMany(unit => new[] { unit.X, unit.Y })],
            })];

        Assert.HasCount(expectedUnits.Length, actualSpawn.Units);
        for (int i = 0; i < expectedUnits.Length; i++)
        {
            UnitDto expected = expectedUnits[i];
            UnitDto actual = actualSpawn.Units.ElementAt(i);
            Assert.AreEqual(expected.Name, actual.Name);
            Assert.AreEqual(expected.Count, actual.Count);
            CollectionAssert.AreEqual(expected.Positions.ToArray(), actual.Positions.ToArray());
        }
    }

    private static string InvokeCreateCompatHash(
        string title,
        string version,
        GameMode gameMode,
        int regionId,
        int baseBuild,
        TimeSpan duration,
        List<ReplayPlayerDto> players)
    {
        var method = typeof(Sc2DirectStrikeParser).GetMethod("CreateCompatHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);

        object? result = method.Invoke(null, [title, version, gameMode, regionId, baseBuild, duration, players]);
        Assert.IsNotNull(result);

        return (string)result;
    }

    private static string InvokeCreatePlayerCompatHash(DirectStrikePlayer player, PlayerDto playerDto, Commander selectedRace, SpawnDto? snapshot)
    {
        var method = typeof(Sc2DirectStrikeParser).GetMethod("CreatePlayerCompatHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);

        object? result = method.Invoke(null, [player, playerDto, selectedRace, snapshot]);
        Assert.IsNotNull(result);

        return (string)result;
    }

    private static ReplayPlayerDto CreateCompatHashPlayer(bool includeMin5)
    {
        return CreateCompatHashPlayer(includeMin5
            ? [CreateCompatHashSpawn(Breakpoint.Min5)]
            : [CreateCompatHashSpawn(Breakpoint.All)]);
    }

    private static ReplayPlayerDto CreateCompatHashPlayer(IReadOnlyCollection<SpawnDto> spawns)
    {
        return new()
        {
            CompatHash = "player-compat",
            Name = "Player",
            Clan = "Clan",
            Race = Commander.Protoss,
            SelectedRace = Commander.Random,
            TeamId = 1,
            GamePos = 1,
            Result = PlayerResult.Win,
            Duration = TimeSpan.FromMinutes(5),
            Apm = 42,
            Messages = 1,
            Pings = 2,
            IsMvp = false,
            Spawns = spawns,
            Player = new()
            {
                PlayerId = 123,
                Name = "Player",
                ToonId = new()
                {
                    Region = 2,
                    Realm = 1,
                    Id = 123,
                },
            },
        };
    }

    private static SpawnDto CreateCompatHashSpawn(Breakpoint breakpoint)
    {
        return new()
        {
            Breakpoint = breakpoint,
            Income = 1000,
            GasCount = 2,
            ArmyValue = 500,
            KilledValue = 300,
            LostValue = 200,
            UpgradeSpent = 100,
            Units =
            [
                new()
                {
                    Name = "Zealot",
                    Count = 2,
                    Positions = [1, 2, 3, 4],
                },
            ],
        };
    }

    private static Commander ExpectedSelectedRaceCommander(Race race)
    {
        return race switch
        {
            Race.Terran => Commander.Terran,
            Race.Protoss => Commander.Protoss,
            Race.Zerg => Commander.Zerg,
            Race.Random => Commander.Random,
            _ => Commander.None,
        };
    }

    private readonly record struct ExpectedDtoBreakpointSpawn(Breakpoint Breakpoint, DirectStrikePlayerSpawn Spawn);
}
