using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using s2protocol.NET;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        ReplayParseBenchmarks.PrintReplayInventory();
        if (args.Length == 0)
        {
            BenchmarkRunner.Run<ReplayParseBenchmarks>();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

[MemoryDiagnoser]
public class ReplayParseBenchmarks
{
    private static readonly string[] StressReplayPaths =
    [
        "testdata/Direct Strike (7586).SC2Replay",
        "testdata/Direct Strike TE (1046).SC2Replay",
        "testdata/Direct Strike TE (1106).SC2Replay",
    ];

    private readonly ReplayDecoder replayDecoder = new();
    private Sc2Replay[] replays = [];

    [GlobalSetup]
    public void Setup()
    {
        replays = DecodeStressReplays().GetAwaiter().GetResult();
    }

    [Benchmark]
    public DirectStrikeReplay[] ParseStressReplays()
    {
        DirectStrikeReplay[] parsedReplays = new DirectStrikeReplay[replays.Length];
        for (int i = 0; i < replays.Length; i++)
        {
            parsedReplays[i] = Sc2DirectStrikeParser.Parse(replays[i]);
        }

        return parsedReplays;
    }

    [Benchmark]
    public ReplayDto[] ParseDtoStressReplays()
    {
        ReplayDto[] replayDtos = new ReplayDto[replays.Length];
        for (int i = 0; i < replays.Length; i++)
        {
            replayDtos[i] = Sc2DirectStrikeParser.ParseDto(replays[i]);
        }

        return replayDtos;
    }

    public static void PrintReplayInventory()
    {
        Console.WriteLine("Stress replay inventory:");
        foreach (string replayPath in StressReplayPaths)
        {
            FileInfo replayFile = new(GetReplayPath(replayPath));
            Console.WriteLine("  {0} ({1:N0} bytes)", replayFile.Name, replayFile.Length);
        }

        Console.WriteLine();
    }

    private async Task<Sc2Replay[]> DecodeStressReplays()
    {
        List<Sc2Replay> decodedReplays = new(StressReplayPaths.Length);
        foreach (string replayPath in StressReplayPaths)
        {
            Sc2Replay replay = await replayDecoder.DecodeAsync(GetReplayPath(replayPath), CreateDecoderOptions(), CancellationToken.None)
                ?? throw new InvalidOperationException($"Could not decode replay '{replayPath}'.");

            PrintDecodedReplayInventory(replayPath, replay);
            decodedReplays.Add(replay);
        }

        return [.. decodedReplays];
    }

    private static void PrintDecodedReplayInventory(string replayPath, Sc2Replay replay)
    {
        Console.WriteLine(
            "Decoded {0}: setup={1:N0}, stats={2:N0}, born={3:N0}, owner={4:N0}, type={5:N0}, upgrades={6:N0}",
            Path.GetFileName(replayPath),
            replay.TrackerEvents?.SPlayerSetupEvents.Count ?? 0,
            replay.TrackerEvents?.SPlayerStatsEvents.Count ?? 0,
            replay.TrackerEvents?.SUnitBornEvents.Count ?? 0,
            replay.TrackerEvents?.SUnitOwnerChangeEvents.Count ?? 0,
            replay.TrackerEvents?.SUnitTypeChangeEvents.Count ?? 0,
            replay.TrackerEvents?.SUpgradeEvents.Count ?? 0);
    }

    private static ReplayDecoderOptions CreateDecoderOptions()
    {
        return new()
        {
            Details = true,
            Initdata = true,
            Metadata = true,
            GameEvents = false,
            MessageEvents = false,
            TrackerEvents = true,
            AttributeEvents = false,
        };
    }

    private static string GetReplayPath(string replayPath)
    {
        return Path.Combine(AppContext.BaseDirectory, replayPath);
    }
}
