using s2protocol.NET;
using s2protocol.NET.Models;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.Tests;

public sealed partial class ParseTests
{
    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetPlayerTeamsAndGamePositionsFromStagingAreas(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.IsNotNull(replay.Details);
        Assert.IsTrue(dsReplay.Players.All(player => player.TeamId is 1 or 2));
        Assert.IsTrue(dsReplay.Players.All(player => player.GamePos is >= 1 and <= 6));

        DetailsPlayer[] detailsPlayers = [.. replay.Details.Players];
        for (int i = 0; i < dsReplay.Players.Count; i++)
        {
            Assert.AreEqual(detailsPlayers[i].WorkingSetSlotId, dsReplay.Players[i].SlotId);
        }

        if (dsReplay.Players.Count == 6)
        {
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3 },
                dsReplay.Players.Where(player => player.TeamId == 1).Select(player => player.GamePos).Order().ToArray());
            CollectionAssert.AreEqual(
                new[] { 4, 5, 6 },
                dsReplay.Players.Where(player => player.TeamId == 2).Select(player => player.GamePos).Order().ToArray());
        }
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetObjectiveTimingsFromTrackerDeaths(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        TimeSpan nexusDeathTime = GetObjectiveDeathTime(replay, "ObjectiveNexus");
        TimeSpan planetaryDeathTime = GetObjectiveDeathTime(replay, "ObjectivePlanetaryFortress");
        TimeSpan expectedGameEndTime = nexusDeathTime == TimeSpan.Zero ? planetaryDeathTime : nexusDeathTime;
        int expectedWinnerTeam = GetExpectedWinnerTeam(replay, dsReplay, nexusDeathTime, planetaryDeathTime);

        Assert.AreEqual(expectedGameEndTime, dsReplay.GameEndTime);
        Assert.AreEqual(expectedWinnerTeam, dsReplay.WinnerTeam);
        Assert.AreEqual(GetObjectiveDeathTime(replay, "ObjectivePhotonCannon"), dsReplay.CannonTime);
        Assert.AreEqual(GetObjectiveDeathTime(replay, "ObjectiveBunker"), dsReplay.BunkerTime);
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetPlayerRefineryTimesFromTrackerEvents(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        TimeSpan[][] expectedRefineryTimes = GetExpectedPlayerRefineryTimes(replay, dsReplay);
        for (int i = 0; i < dsReplay.Players.Count; i++)
        {
            CollectionAssert.AreEqual(
                expectedRefineryTimes[i],
                dsReplay.Players[i].RefineryTimes,
                $"Unexpected refinery times for player index {i}.");
        }
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetPlayerUpgradesFromTrackerEvents(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        TimeSpan[][] expectedTierUpgrades = GetExpectedPlayerTierUpgrades(replay, dsReplay);
        Dictionary<string, TimeSpan>[] expectedUpgrades = GetExpectedPlayerUpgrades(replay, dsReplay);
        for (int i = 0; i < dsReplay.Players.Count; i++)
        {
            DirectStrikePlayer player = dsReplay.Players[i];
            CollectionAssert.AreEqual(
                expectedTierUpgrades[i],
                player.TierUpgrades,
                $"Unexpected tier upgrades for player index {i}.");
            AssertDictionariesAreEqual(
                expectedUpgrades[i],
                player.Upgrades,
                $"Unexpected upgrades for player index {i}.");
        }
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetMiddleControlFromTrackerOwnerChangeEvents(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        ExpectedMiddleControlChange[] expectedMiddleChanges = GetExpectedMiddleControlChanges(replay);
        CollectionAssert.AreEqual(
            expectedMiddleChanges.Select(change => change.Time).ToArray(),
            dsReplay.MiddleChanges);
        Assert.AreEqual(
            expectedMiddleChanges.Length == 0 ? 0 : expectedMiddleChanges[0].Team,
            dsReplay.FirstMiddleControlTeam);
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetPlayerStatsFromTrackerEvents(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        ExpectedPlayerStats[][] expectedStats = GetExpectedPlayerStats(replay, dsReplay);
        int[] expectedStateDurationGameloops = GetExpectedPlayerStateDurationGameloops(replay, dsReplay);
        Assert.IsTrue(dsReplay.Players.Any(player => player.Stats.Count > 0));
        for (int i = 0; i < dsReplay.Players.Count; i++)
        {
            DirectStrikePlayer player = dsReplay.Players[i];
            Assert.IsTrue(player.Stats.Select(stats => stats.Gameloop).SequenceEqual(player.Stats.Select(stats => stats.Gameloop).Order()));
            Assert.HasCount(expectedStats[i].Length, player.Stats, $"Unexpected stats count for player index {i}.");

            int expectedDurationGameloop = expectedStats[i]
                .Where(stats => stats.MineralsCollectionRate > 0)
                .Select(stats => stats.Gameloop)
                .DefaultIfEmpty()
                .Max();
            expectedDurationGameloop = Math.Max(expectedDurationGameloop, expectedStateDurationGameloops[i]);
            Assert.AreEqual(expectedDurationGameloop, player.DurationGameloop);
            Assert.AreEqual(TimeSpan.FromSeconds(expectedDurationGameloop / 22.4D), player.Duration);

            for (int j = 0; j < expectedStats[i].Length; j++)
            {
                ExpectedPlayerStats expected = expectedStats[i][j];
                DirectStrikePlayerStats actual = player.Stats[j];
                Assert.AreEqual(expected.Gameloop, actual.Gameloop);
                Assert.AreEqual(TimeSpan.FromSeconds(expected.Gameloop / 22.4D), actual.Time);
                Assert.AreEqual(expected.MineralsCollectionRate, actual.MineralsCollectionRate);
                Assert.AreEqual(expected.MineralsUsedActiveForces, actual.MineralsUsedActiveForces);
                Assert.AreEqual(expected.MineralsUsedCurrentTechnology, actual.MineralsUsedCurrentTechnology);
                Assert.AreEqual(expected.MineralsKilledArmy, actual.MineralsKilledArmy);
                Assert.AreEqual(expected.MineralsLostArmy, actual.MineralsLostArmy);
            }
        }
    }

    private static TimeSpan GetObjectiveDeathTime(Sc2Replay replay, string unitTypeName)
    {
        SUnitBornEvent? objective = replay.TrackerEvents?.SUnitBornEvents.SingleOrDefault(unitBornEvent =>
            unitBornEvent.Gameloop == 0
            && string.Equals(unitBornEvent.UnitTypeName, unitTypeName, StringComparison.Ordinal));

        return objective?.SUnitDiedEvent is { } diedEvent
            ? TimeSpan.FromSeconds(diedEvent.Gameloop / 22.4D)
            : TimeSpan.Zero;
    }

    private static TimeSpan[][] GetExpectedPlayerRefineryTimes(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        Dictionary<(int UnitTagIndex, int UnitTagRecycle), int> refineryOwnerIndexesByTag = [];

        foreach (SUnitBornEvent bornEvent in replay.TrackerEvents?.SUnitBornEvents ?? [])
        {
            if (bornEvent.Gameloop != 0
                || !bornEvent.UnitTypeName.StartsWith("MineralField", StringComparison.Ordinal)
                || !playerIndexesByControlPlayerId.TryGetValue(bornEvent.ControlPlayerId, out int playerIndex))
            {
                continue;
            }

            refineryOwnerIndexesByTag.TryAdd((bornEvent.UnitTagIndex, bornEvent.UnitTagRecycle), playerIndex);
        }

        List<int>[] refineryGameloopsByPlayer = [.. Enumerable.Range(0, dsReplay.Players.Count).Select(_ => new List<int>())];
        HashSet<(int UnitTagIndex, int UnitTagRecycle)> takenRefineries = [];

        foreach (SUnitTypeChangeEvent typeChangeEvent in replay.TrackerEvents?.SUnitTypeChangeEvents ?? [])
        {
            (int UnitTagIndex, int UnitTagRecycle) tag = (typeChangeEvent.UnitTagIndex, typeChangeEvent.UnitTagRecycle);
            if (!IsExpectedRefineryMinerals(typeChangeEvent.UnitTypeName)
                || !refineryOwnerIndexesByTag.TryGetValue(tag, out int playerIndex)
                || !takenRefineries.Add(tag))
            {
                continue;
            }

            refineryGameloopsByPlayer[playerIndex].Add(typeChangeEvent.Gameloop);
        }

        return [.. refineryGameloopsByPlayer
            .Select(gameloops => gameloops
                .Order()
                .Select(gameloop => TimeSpan.FromSeconds(gameloop / 22.4D))
                .ToArray())];
    }

    private static TimeSpan[][] GetExpectedPlayerTierUpgrades(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        List<int>[] tierUpgradesByPlayer = [.. Enumerable.Range(0, dsReplay.Players.Count).Select(_ => new List<int>())];

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop == 0
                || upgradeEvent.UpgradeTypeName is not ("Tier2" or "Tier3")
                || !playerIndexesByControlPlayerId.TryGetValue(upgradeEvent.PlayerId, out int playerIndex))
            {
                continue;
            }

            tierUpgradesByPlayer[playerIndex].Add(upgradeEvent.Gameloop);
        }

        return [.. tierUpgradesByPlayer
            .Select(gameloops => gameloops
                .Order()
                .Select(gameloop => TimeSpan.FromSeconds(gameloop / 22.4D))
                .ToArray())];
    }

    private static Dictionary<string, TimeSpan>[] GetExpectedPlayerUpgrades(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        Dictionary<string, int>[] upgradesByPlayer = [.. Enumerable.Range(0, dsReplay.Players.Count).Select(_ => new Dictionary<string, int>(StringComparer.Ordinal))];

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop == 0
                || !playerIndexesByControlPlayerId.TryGetValue(upgradeEvent.PlayerId, out int playerIndex))
            {
                continue;
            }

            string upgradeName = upgradeEvent.UpgradeTypeName;
            if (upgradeName is "Tier2" or "Tier3"
                || IsExpectedPlayerStateUpgrade(upgradeName)
                || IsExpectedFilteredUpgrade(upgradeName)
                || (upgradeName.Contains("Level", StringComparison.Ordinal) && !IsExpectedNormalizedLevelUpgrade(upgradeName, dsReplay.Players[playerIndex].Commander)))
            {
                continue;
            }

            upgradesByPlayer[playerIndex].TryAdd(upgradeName, upgradeEvent.Gameloop);
        }

        return [.. upgradesByPlayer.Select(upgrades => upgrades.ToDictionary(
            pair => pair.Key,
            pair => TimeSpan.FromSeconds(pair.Value / 22.4D),
            StringComparer.Ordinal))];
    }

    private static void AssertDictionariesAreEqual(IReadOnlyDictionary<string, TimeSpan> expected, IReadOnlyDictionary<string, TimeSpan> actual, string message)
    {
        Assert.HasCount(expected.Count, actual, message);
        foreach (KeyValuePair<string, TimeSpan> pair in expected)
        {
            Assert.IsTrue(actual.TryGetValue(pair.Key, out TimeSpan actualValue), $"{message} Missing upgrade '{pair.Key}'.");
            Assert.AreEqual(pair.Value, actualValue, $"{message} Unexpected timing for upgrade '{pair.Key}'.");
        }
    }

    private static ExpectedMiddleControlChange[] GetExpectedMiddleControlChanges(Sc2Replay replay)
    {
        List<ExpectedMiddleControlChange> changes = [];
        foreach (SUnitOwnerChangeEvent ownerChangeEvent in replay.TrackerEvents?.SUnitOwnerChangeEvents ?? [])
        {
            if (ownerChangeEvent.UnitTagIndex != 20
                || !TryGetExpectedMiddleControlTeam(ownerChangeEvent.UpkeepPlayerId, out int team))
            {
                continue;
            }

            changes.Add(new(TimeSpan.FromSeconds(ownerChangeEvent.Gameloop / 22.4D), team));
        }

        return [.. changes];
    }

    private static ExpectedPlayerStats[][] GetExpectedPlayerStats(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        List<ExpectedPlayerStats>[] statsByPlayer = [.. Enumerable.Range(0, dsReplay.Players.Count).Select(_ => new List<ExpectedPlayerStats>())];

        foreach (SPlayerStatsEvent statsEvent in replay.TrackerEvents?.SPlayerStatsEvents ?? [])
        {
            if (!playerIndexesByControlPlayerId.TryGetValue(statsEvent.PlayerId, out int playerIndex))
            {
                continue;
            }

            statsByPlayer[playerIndex].Add(new(
                statsEvent.Gameloop,
                statsEvent.MineralsCollectionRate,
                statsEvent.MineralsUsedActiveForces,
                statsEvent.MineralsUsedCurrentTechnology,
                statsEvent.MineralsKilledArmy,
                statsEvent.MineralsLostArmy));
        }

        return [.. statsByPlayer.Select(stats => stats.OrderBy(stat => stat.Gameloop).ToArray())];
    }

    private static int GetExpectedWinnerTeam(
        Sc2Replay replay,
        DirectStrikeReplay dsReplay,
        TimeSpan nexusDeathTime,
        TimeSpan planetaryDeathTime)
    {
        if (TryGetExpectedVictoryTeam(replay, dsReplay, out int victoryTeam))
        {
            return victoryTeam;
        }

        if (nexusDeathTime != TimeSpan.Zero)
        {
            return 1;
        }

        return planetaryDeathTime != TimeSpan.Zero ? 2 : 0;
    }

    private static bool TryGetExpectedVictoryTeam(Sc2Replay replay, DirectStrikeReplay dsReplay, out int team)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        int? victoryTeam = null;
        bool hasInvalidVictoryTeam = false;

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop == 0
                || upgradeEvent.UpgradeTypeName != "PlayerStateVictory"
                || !playerIndexesByControlPlayerId.TryGetValue(upgradeEvent.PlayerId, out int playerIndex))
            {
                continue;
            }

            int playerTeam = dsReplay.Players[playerIndex].TeamId;
            if (playerTeam is not (1 or 2))
            {
                hasInvalidVictoryTeam = true;
            }
            else if (victoryTeam is null)
            {
                victoryTeam = playerTeam;
            }
            else if (victoryTeam.Value != playerTeam)
            {
                hasInvalidVictoryTeam = true;
            }
        }

        team = victoryTeam ?? 0;
        return victoryTeam is not null && !hasInvalidVictoryTeam;
    }

    private static int[] GetExpectedPlayerStateDurationGameloops(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = GetPlayerIndexesByControlPlayerId(replay, dsReplay);
        int[] durations = new int[dsReplay.Players.Count];

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop == 0
                || !IsExpectedPlayerStateUpgrade(upgradeEvent.UpgradeTypeName)
                || !playerIndexesByControlPlayerId.TryGetValue(upgradeEvent.PlayerId, out int playerIndex))
            {
                continue;
            }

            durations[playerIndex] = Math.Max(durations[playerIndex], upgradeEvent.Gameloop);
        }

        return durations;
    }

    private static bool IsExpectedPlayerStateUpgrade(string upgradeName)
    {
        return upgradeName is "PlayerStateVictory" or "PlayerStateGameOver";
    }

    private static bool TryGetExpectedMiddleControlTeam(int upkeepPlayerId, out int team)
    {
        team = upkeepPlayerId switch
        {
            13 => 1,
            14 => 2,
            _ => 0,
        };

        return team != 0;
    }

    private static Dictionary<int, int> GetPlayerIndexesByControlPlayerId(Sc2Replay replay, DirectStrikeReplay dsReplay)
    {
        Dictionary<int, int> playerIndexesByControlPlayerId = [];
        Dictionary<(int Region, int Realm, int Id), int> playerIndexesByToon = [];
        Dictionary<int, int> playerIndexesBySlotId = [];

        for (int i = 0; i < dsReplay.Players.Count; i++)
        {
            DirectStrikePlayer player = dsReplay.Players[i];
            playerIndexesByToon.TryAdd((player.Region, player.Realm, player.Id), i);
            playerIndexesBySlotId.TryAdd(player.SlotId, i);
        }

        Slot[] slots = [.. replay.Initdata?.LobbyState?.Slots ?? []];
        foreach (SPlayerSetupEvent setupEvent in replay.TrackerEvents?.SPlayerSetupEvents ?? [])
        {
            if ((TryGetPlayerIndexByUserId(setupEvent.UserId, slots, playerIndexesByToon, out int playerIndex)
                    || playerIndexesBySlotId.TryGetValue(setupEvent.SlotId, out playerIndex)
                    || TryGetPlayerIndexByPlayerId(setupEvent.PlayerId, dsReplay, out playerIndex)))
            {
                playerIndexesByControlPlayerId.TryAdd(setupEvent.PlayerId, playerIndex);
            }
        }

        return playerIndexesByControlPlayerId;
    }

    private static bool TryGetPlayerIndexByUserId(
        int? userId,
        Slot[] slots,
        Dictionary<(int Region, int Realm, int Id), int> playerIndexesByToon,
        out int playerIndex)
    {
        playerIndex = -1;
        if (userId is null)
        {
            return false;
        }

        foreach (Slot slot in slots)
        {
            if (slot.UserId == userId
                && TryParseToonHandle(slot.ToonHandle, out int region, out int realm, out int id)
                && playerIndexesByToon.TryGetValue((region, realm, id), out playerIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPlayerIndexByPlayerId(int playerId, DirectStrikeReplay dsReplay, out int playerIndex)
    {
        playerIndex = playerId - 1;
        return playerIndex >= 0 && playerIndex < dsReplay.Players.Count;
    }

    private static bool IsExpectedRefineryMinerals(string unitTypeName)
    {
        return unitTypeName.StartsWith("RefineryMinerals", StringComparison.Ordinal)
            || unitTypeName.StartsWith("AssimilatorMinerals", StringComparison.Ordinal)
            || unitTypeName.StartsWith("ExtractorMinerals", StringComparison.Ordinal);
    }

    private static bool IsExpectedFilteredUpgrade(string upgradeName)
    {
        if (ExpectedUpgradeExactMatches.Contains(upgradeName))
        {
            return true;
        }

        foreach (string pattern in ExpectedUpgradeStartsWithPatterns)
        {
            if (upgradeName.StartsWith(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (string pattern in ExpectedUpgradeEndsWithPatterns)
        {
            if (upgradeName.EndsWith(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (string pattern in ExpectedUpgradeContainsPatterns)
        {
            if (upgradeName.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExpectedNormalizedLevelUpgrade(string upgradeName, Commander commander)
    {
        Commander normalizedCommander = ExpectedHeroToDefaultRace.GetValueOrDefault(commander, commander);
        return upgradeName.StartsWith(normalizedCommander.ToString(), StringComparison.Ordinal);
    }

    private static readonly HashSet<string> ExpectedUpgradeExactMatches = new(StringComparer.Ordinal)
    {
        "MineralIncomeBonus",
        "HighCapacityMode",
        "HornerMySignificantOtherBuffHan",
        "HornerMySignificantOtherBuffHorner",
        "StagingAreaNextSpawn",
        "MineralIncome",
        "SpookySkeletonNerf",
        "NeosteelFrame",
        "PlayerIsAFK",
        "DehakaHeroLevel",
        "DehakaSkillPoint",
        "DehakaHeroPlaceUsed",
        "KerriganMutatingCarapaceBonus",
        "TychusTychusPlaced",
        "TychusFirstOnesontheHouse",
        "ClolarionInterdictorsBonus",
        "PartyFrameHide",
        "FenixUnlock",
        "FenixExperienceAwarded",
        "HideWorkerCommandCard",
        "UsingVespeneIncapableWorker",
        "DehakaPrimalWurm",
    };

    private static readonly string[] ExpectedUpgradeStartsWithPatterns =
    [
        "AFKTimer",
        "Decoration",
        "Mastery",
        "Emote",
        "Tier",
        "DehakaCreeperHost",
        "Blacklist",
        "RaynorCostReduced",
        "Theme",
        "Worker",
        "AreaFlair",
        "AreaWeather",
        "Aura",
        "PowerField",
    ];

    private static readonly string[] ExpectedUpgradeEndsWithPatterns =
    [
        "Disable",
        "Enable",
        "Starlight",
        "Modification",
        "Bonus",
        "Bonus10",
    ];

    private static readonly string[] ExpectedUpgradeContainsPatterns =
    [
        "Worker",
        "PlaceEvolved",
    ];

    private static readonly Dictionary<Commander, Commander> ExpectedHeroToDefaultRace = new()
    {
        { Commander.Zagara, Commander.Zerg },
        { Commander.Abathur, Commander.Zerg },
        { Commander.Kerrigan, Commander.Zerg },
        { Commander.Alarak, Commander.Protoss },
        { Commander.Artanis, Commander.Protoss },
        { Commander.Vorazun, Commander.Protoss },
        { Commander.Fenix, Commander.Protoss },
        { Commander.Karax, Commander.Protoss },
        { Commander.Zeratul, Commander.Protoss },
        { Commander.Raynor, Commander.Terran },
        { Commander.Swann, Commander.Terran },
        { Commander.Nova, Commander.Terran },
        { Commander.Stukov, Commander.Terran },
    };

    private readonly record struct ExpectedMiddleControlChange(TimeSpan Time, int Team);

    private readonly record struct ExpectedPlayerStats(
        int Gameloop,
        int MineralsCollectionRate,
        int MineralsUsedActiveForces,
        int MineralsUsedCurrentTechnology,
        int MineralsKilledArmy,
        int MineralsLostArmy);
}
