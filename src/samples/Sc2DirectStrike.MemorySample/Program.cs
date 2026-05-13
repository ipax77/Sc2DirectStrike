using s2protocol.NET;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.MemorySample;

class Program
{
    static async Task Main(string[] args)
    {
        var testReplay = @"C:\Users\pax77\source\repos\Sc2DirectStrike\src\Sc2DirectStrike.Tests\testdata\Direct Strike TE (1106).SC2Replay";
        var replayDecoder = new ReplayDecoder();
        var sc2Replay = await replayDecoder.DecodeAsync(testReplay);
        ArgumentNullException.ThrowIfNull(sc2Replay);

        Console.WriteLine("ready to parse.");
        Console.ReadLine();

        var dsReplay = Sc2DirectStrikeParser.Parse(sc2Replay);
        ArgumentNullException.ThrowIfNull(dsReplay);

        Console.WriteLine("job done.");
        Console.ReadLine();
    }
}
