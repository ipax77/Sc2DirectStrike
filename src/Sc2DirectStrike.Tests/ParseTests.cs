using s2protocol.NET;
using s2protocol.NET.Models;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.Tests;

[TestClass]
public sealed class ParseTests
{
    private readonly ReplayDecoder replayDecoder = new();
    private readonly ReplayDecoderOptions replayDecoderOptions = new()
    {
        Details = true,
        Initdata = true,
        Metadata = true,
        GameEvents = false,
        MessageEvents = false,
        TrackerEvents = true,
        AttributeEvents = false,
    };

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay")]
    [DataRow("testdata/Direct Strike (10096).SC2Replay")]
    [DataRow("testdata/Direct Strike (10124).SC2Replay")]
    [DataRow("testdata/Direct Strike (10143).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanSetGameTime(string replayName)
    {
        var replay = await GetReplay(replayName);

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);
        Assert.IsNotNull(dsReplay);

        Assert.IsGreaterThan(DateTime.MinValue, dsReplay.GameTime);
        Assert.IsNotEmpty(dsReplay.Players);
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay", 525)]
    [DataRow("testdata/Direct Strike (10096).SC2Replay", 483)]
    [DataRow("testdata/Direct Strike (10124).SC2Replay", 722)]
    [DataRow("testdata/Direct Strike (10143).SC2Replay", 341)]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay", 483)]
    public async Task CanSetReplayMetadata(string replayName, int durationSeconds)
    {
        var replay = await GetReplay(replayName);

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.IsNotNull(replay.Metadata);
        Assert.AreEqual(replay.Metadata.BaseBuild, dsReplay.BaseBuild);
        Assert.IsNotEmpty(dsReplay.BaseBuild);
        Assert.AreEqual(TimeSpan.FromSeconds(durationSeconds), dsReplay.Duration);
    }

    [TestMethod]
    [DataRow("testdata/Direct Strike (10060).SC2Replay", GameMode.Standard)]
    [DataRow("testdata/Direct Strike (10096).SC2Replay", GameMode.BrawlCommanders)]
    [DataRow("testdata/Direct Strike (10124).SC2Replay", GameMode.Standard)]
    [DataRow("testdata/Direct Strike (10143).SC2Replay", GameMode.Standard)]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay", GameMode.Standard)]
    public async Task CanSetGameMode(string replayName, GameMode gameMode)
    {
        var replay = await GetReplay(replayName);

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.AreEqual(gameMode, dsReplay.GameMode);
    }

    [TestMethod]
    public async Task CanSetPlayerMetadata()
    {
        var replay = await GetReplay("testdata/Direct Strike (10060).SC2Replay");

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);
        var firstPlayer = dsReplay.Players[0];
        var fifthPlayer = dsReplay.Players[4];

        Assert.AreEqual(20D, firstPlayer.APM);
        Assert.AreEqual(PlayerResult.Win, firstPlayer.Result);
        Assert.AreEqual(Race.Random, firstPlayer.SelectedRace);

        Assert.AreEqual(5D, fifthPlayer.APM);
        Assert.AreEqual(PlayerResult.Loss, fifthPlayer.Result);
        Assert.AreEqual(Race.Terran, fifthPlayer.SelectedRace);
    }

    [TestMethod]
    public async Task CanMapTeMetadataByPlayerIdAsListIndex()
    {
        var replay = await GetReplay("testdata/Direct Strike TE (1910).SC2Replay");

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);
        var playerWithSkippedSlotId = dsReplay.Players[3];

        Assert.AreEqual(5, playerWithSkippedSlotId.SlotId);
        Assert.AreEqual(62D, playerWithSkippedSlotId.APM);
        Assert.AreEqual(PlayerResult.Undecided, playerWithSkippedSlotId.Result);
        Assert.AreEqual(Race.Terran, playerWithSkippedSlotId.SelectedRace);
    }

    [TestMethod]
    public async Task CanSetTeObserversFromInitdata()
    {
        var replay = await GetReplay("testdata/Direct Strike TE (1904).SC2Replay");

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.HasCount(6, dsReplay.Players);
        Assert.HasCount(2, dsReplay.Observers);
        Assert.IsFalse(dsReplay.Players.Any(player => player.Name is "Apache" or "Nova"));

        DirectStrikeObserver apache = AssertObserver(dsReplay, "Apache");
        Assert.AreEqual("DIRSTK", apache.Clan);
        Assert.AreEqual(5, apache.SlotId);
        Assert.AreEqual(2, apache.Region);
        Assert.AreEqual(1, apache.Realm);
        Assert.AreEqual(8641442, apache.Id);

        DirectStrikeObserver nova = AssertObserver(dsReplay, "Nova");
        Assert.AreEqual("DIRSTK", nova.Clan);
        Assert.AreEqual(0, nova.SlotId);
        Assert.AreEqual(2, nova.Region);
        Assert.AreEqual(1, nova.Realm);
        Assert.AreEqual(9493659, nova.Id);
    }

    [TestMethod]
    public async Task CanSetStandardCommandersFromEarlyWorkerEvents()
    {
        var replay = await GetReplay("testdata/Direct Strike TE (1904).SC2Replay");

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        CollectionAssert.AreEqual(
            new[] { Commander.Protoss, Commander.Protoss, Commander.Protoss, Commander.Terran, Commander.Terran, Commander.Terran },
            dsReplay.Players.Select(player => player.Commander).ToArray());
    }

    [TestMethod]
    public async Task CanSetCommanderModeCommandersFromEarlyWorkerEvents()
    {
        var replay = await GetReplay("testdata/Direct Strike (10096).SC2Replay");

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        CollectionAssert.AreEqual(
            new[] { Commander.Terran, Commander.Tychus, Commander.Raynor, Commander.Stukov, Commander.Fenix, Commander.Terran },
            dsReplay.Players.Select(player => player.Commander).ToArray());
    }

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
        int expectedWinnerTeam = 0;
        if (nexusDeathTime != TimeSpan.Zero)
        {
            expectedWinnerTeam = 1;
        }
        else if (planetaryDeathTime != TimeSpan.Zero)
        {
            expectedWinnerTeam = 2;
        }

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
    [DataRow("testdata/Direct Strike TE (1904).SC2Replay")]
    [DataRow("testdata/Direct Strike TE (1910).SC2Replay")]
    public async Task CanMapTeInitdataMetadataAndTrackerCommandersByControlPlayerId(string replayName)
    {
        Sc2Replay replay = await GetReplay(replayName);

        DirectStrikeReplay dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.IsNotNull(replay.Initdata);
        Assert.IsNotNull(replay.Metadata);
        Assert.IsNotNull(replay.TrackerEvents);

        var playersByToon = dsReplay.Players.ToDictionary(player => (player.Region, player.Realm, player.Id));
        var metadataPlayersByPlayerId = replay.Metadata.Players.ToDictionary(player => player.PlayerID);
        var firstWorkerByControlPlayerId = replay.TrackerEvents.SUnitBornEvents
            .Where(unitBornEvent => unitBornEvent.Gameloop <= 1440 && TryParseWorkerCommander(unitBornEvent.UnitTypeName, out _))
            .GroupBy(unitBornEvent => unitBornEvent.ControlPlayerId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (SPlayerSetupEvent setupEvent in replay.TrackerEvents.SPlayerSetupEvents)
        {
            Slot? slot = replay.Initdata.LobbyState.Slots.SingleOrDefault(slot => slot.UserId == setupEvent.UserId);
            Assert.IsNotNull(slot);
            Assert.IsTrue(TryParseToonHandle(slot.ToonHandle, out int region, out int realm, out int id));
            Assert.IsTrue(playersByToon.TryGetValue((region, realm, id), out DirectStrikePlayer? player));

            Assert.IsTrue(metadataPlayersByPlayerId.TryGetValue(setupEvent.PlayerId, out MetadataPlayer? metadataPlayer));
            Assert.IsTrue(firstWorkerByControlPlayerId.TryGetValue(setupEvent.PlayerId, out SUnitBornEvent? workerEvent));
            Assert.IsTrue(TryParseAssignedRaceCommander(metadataPlayer.AssignedRace, out Commander metadataCommander));
            Assert.IsTrue(TryParseWorkerCommander(workerEvent.UnitTypeName, out Commander trackerCommander));

            Assert.AreEqual(metadataCommander, player.Commander);
            Assert.AreEqual(metadataCommander, trackerCommander);
        }
    }

    [TestMethod]
    public async Task CanParseReplayWithoutMetadata()
    {
        ReplayDecoderOptions options = new()
        {
            Details = true,
            Initdata = true,
            Metadata = false,
            GameEvents = false,
            MessageEvents = false,
            TrackerEvents = true,
            AttributeEvents = false,
        };
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10060).SC2Replay", options);

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.AreEqual(string.Empty, dsReplay.BaseBuild);
        Assert.AreEqual(TimeSpan.Zero, dsReplay.Duration);
        Assert.AreEqual(PlayerResult.Win, dsReplay.Players[0].Result);
        Assert.AreEqual(Race.None, dsReplay.Players[0].SelectedRace);
        Assert.IsEmpty(dsReplay.Observers);
    }

    [TestMethod]
    public async Task CanParseReplayWithoutTrackerEvents()
    {
        ReplayDecoderOptions options = new()
        {
            Details = true,
            Initdata = true,
            Metadata = true,
            GameEvents = false,
            MessageEvents = false,
            TrackerEvents = false,
            AttributeEvents = false,
        };
        Sc2Replay replay = await GetReplay("testdata/Direct Strike (10060).SC2Replay", options);

        var dsReplay = Sc2DirectStrikeParser.Parse(replay);

        Assert.AreEqual(GameMode.None, dsReplay.GameMode);
        Assert.AreEqual(0, dsReplay.WinnerTeam);
        Assert.AreEqual(TimeSpan.Zero, dsReplay.GameEndTime);
        Assert.AreEqual(TimeSpan.Zero, dsReplay.CannonTime);
        Assert.AreEqual(TimeSpan.Zero, dsReplay.BunkerTime);
        Assert.AreEqual(0, dsReplay.FirstMiddleControlTeam);
        Assert.IsEmpty(dsReplay.MiddleChanges);
        Assert.IsTrue(dsReplay.Players.All(player => player.GamePos == 0));
        Assert.IsTrue(dsReplay.Players.All(player => player.TeamId == 0));
        Assert.IsTrue(dsReplay.Players.All(player => player.RefineryTimes.Length == 0));
        CollectionAssert.AreEqual(
            new[] { Commander.Zerg, Commander.Protoss, Commander.Protoss, Commander.Zerg, Commander.Terran, Commander.Zerg },
            dsReplay.Players.Select(player => player.Commander).ToArray());
    }

    [TestMethod]
    public void CanClassifyGameModeFallbacks()
    {
        Assert.AreEqual(GameMode.Tutorial, InvokeGetGameMode([], 1));
        Assert.AreEqual(GameMode.None, InvokeGetGameMode([], 6));
        Assert.AreEqual(GameMode.None, InvokeGetGameMode(["GameModeMystery"], 6));
        Assert.AreEqual(GameMode.BrawlStandard, InvokeGetGameMode(["GameModeBrawl", "GameModeStandard"], 6));
        Assert.AreEqual(GameMode.BrawlCommanders, InvokeGetGameMode(["GameModeBrawl", "MutationCommanders"], 6));
    }

    private static GameMode InvokeGetGameMode(string[] modes, int playerCount)
    {
        var method = typeof(Sc2DirectStrikeParser).GetMethod("GetGameMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);

        object? result = method.Invoke(null, [new HashSet<string>(modes, StringComparer.Ordinal), playerCount]);
        Assert.IsNotNull(result);

        return (GameMode)result;
    }

    private static DirectStrikeObserver AssertObserver(DirectStrikeReplay replay, string name)
    {
        DirectStrikeObserver? observer = replay.Observers.SingleOrDefault(observer => observer.Name == name);
        Assert.IsNotNull(observer);

        return observer;
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

    private static bool TryParseAssignedRaceCommander(string assignedRace, out Commander commander)
    {
        commander = assignedRace switch
        {
            "Prot" => Commander.Protoss,
            "Terr" => Commander.Terran,
            "Zerg" => Commander.Zerg,
            _ => Commander.None,
        };

        return commander != Commander.None;
    }

    private static bool TryParseWorkerCommander(string unitTypeName, out Commander commander)
    {
        const string workerPrefix = "Worker";

        commander = Commander.None;
        if (!unitTypeName.StartsWith(workerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string commanderName = unitTypeName[workerPrefix.Length..];
        commander = commanderName switch
        {
            nameof(Commander.Protoss) => Commander.Protoss,
            nameof(Commander.Terran) => Commander.Terran,
            nameof(Commander.Zerg) => Commander.Zerg,
            _ => Enum.TryParse(commanderName, out Commander parsedCommander) ? parsedCommander : Commander.None,
        };

        return commander != Commander.None;
    }

    private static bool TryParseToonHandle(string toonHandle, out int region, out int realm, out int id)
    {
        region = 0;
        realm = 0;
        id = 0;

        string[] parts = toonHandle.Split('-');
        return parts.Length == 4
            && string.Equals(parts[1], "S2", StringComparison.Ordinal)
            && int.TryParse(parts[0], out region)
            && int.TryParse(parts[2], out realm)
            && int.TryParse(parts[3], out id);
    }

    private readonly record struct ExpectedMiddleControlChange(TimeSpan Time, int Team);

    private async Task<Sc2Replay> GetReplay(string replayPath)
    {
        return await GetReplay(replayPath, replayDecoderOptions);
    }

    private async Task<Sc2Replay> GetReplay(string replayPath, ReplayDecoderOptions options)
    {
        return await replayDecoder.DecodeAsync(replayPath, options, CancellationToken.None) ?? throw new ArgumentNullException(nameof(replayPath));
    }
}
