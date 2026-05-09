using s2protocol.NET;
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

    private async Task<Sc2Replay> GetReplay(string replayPath)
    {
        return await GetReplay(replayPath, replayDecoderOptions);
    }

    private async Task<Sc2Replay> GetReplay(string replayPath, ReplayDecoderOptions options)
    {
        return await replayDecoder.DecodeAsync(replayPath, options, CancellationToken.None) ?? throw new ArgumentNullException(nameof(replayPath));
    }
}
