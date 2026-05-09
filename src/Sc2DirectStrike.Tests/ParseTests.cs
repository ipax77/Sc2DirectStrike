using s2protocol.NET;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.Tests;

[TestClass]
public sealed class ParseTests
{
    private readonly ReplayDecoder replayDecoder = new();
    private readonly ReplayDecoderOptions replayDecoderOptions = new()
    {
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

    private async Task<Sc2Replay> GetReplay(string replayPath)
    {
        return await replayDecoder.DecodeAsync(replayPath) ?? throw new ArgumentNullException(nameof(replayPath));
    }
}
