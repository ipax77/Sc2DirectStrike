using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Sc2DirectStrike.ParserCompareSample.Shared;
using s2protocol.NET;
using s2protocol.NET.Models;
using ParserReplayDto = Sc2DirectStrike.Parser.ReplayDto;
using ParserReplayPlayerDto = Sc2DirectStrike.Parser.ReplayPlayerDto;
using ParserSpawnDto = Sc2DirectStrike.Parser.SpawnDto;
using ParserUnitDto = Sc2DirectStrike.Parser.UnitDto;

namespace Sc2DirectStrike.ParserCompareSample.NewStack;

internal static class Program
{
    private const string StackName = "New";
    private const string ParserName = "Sc2DirectStrikeParser";
    private const string S2ProtocolVersion = "0.9.3";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Sc2DirectStrike.ParserCompareSample.NewStack <request-json> <result-json>");
            return 2;
        }

        CompareRequest? request = await ReadJsonAsync<CompareRequest>(args[0]);
        if (request is null)
        {
            Console.Error.WriteLine("Could not read compare request.");
            return 2;
        }

        CompareWorkerResult result = await RunAsync(request);
        await WriteJsonAsync(args[1], result);
        return 0;
    }

    private static async Task<CompareWorkerResult> RunAsync(CompareRequest request)
    {
        DecodedReplay[] decodedReplays = new DecodedReplay[request.ReplayPaths.Length];
        ConcurrentBag<ReplayError> errors = [];
        ReplayDecoderOptions decoderOptions = CreateDecoderOptions(request.DecoderOptions);
        int decodeThreads = Math.Max(1, Math.Min(request.DecodeThreads, Math.Max(1, request.ReplayPaths.Length)));
        using SemaphoreSlim semaphore = new(decodeThreads);
        Stopwatch decodeStopwatch = Stopwatch.StartNew();

        Task[] decodeTasks = request.ReplayPaths.Select(async (replayPath, index) =>
        {
            await semaphore.WaitAsync();
            try
            {
                using ReplayDecoder decoder = new();
                Sc2Replay? replay = await decoder.DecodeAsync(replayPath, decoderOptions, CancellationToken.None);
                if (replay is null)
                {
                    errors.Add(new(replayPath, "DecodeError", "Decoder returned no replay."));
                    return;
                }

                decodedReplays[index] = new(replayPath, replay);
            }
            catch (Exception ex)
            {
                errors.Add(new(replayPath, "DecodeError", ex.Message));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(decodeTasks);
        decodeStopwatch.Stop();

        int iterations = Math.Max(1, request.ParseIterations);
        Stopwatch parseStopwatch = new();
        long parseTicks = 0;
        List<NormalizedReplayResult> replays = [];

        foreach (DecodedReplay decodedReplay in decodedReplays)
        {
            if (decodedReplay.Replay is null)
            {
                continue;
            }

            try
            {
                ParserReplayDto? dto = null;
                for (int i = 0; i < iterations; i++)
                {
                    parseStopwatch.Restart();
                    dto = Sc2DirectStrike.Parser.Sc2DirectStrikeParser.ParseDto(decodedReplay.Replay);
                    parseStopwatch.Stop();
                    parseTicks += parseStopwatch.Elapsed.Ticks;
                }

                replays.Add(new()
                {
                    ReplayPath = decodedReplay.ReplayPath,
                    Replay = Normalize(dto ?? throw new InvalidOperationException("Parser returned no DTO.")),
                });
            }
            catch (Exception ex)
            {
                errors.Add(new(decodedReplay.ReplayPath, ex.GetType().Name, ex.Message));
            }
        }

        long onePassParseTicks = iterations == 0 ? parseTicks : parseTicks / iterations;
        return new()
        {
            StackName = StackName,
            ParserName = ParserName,
            S2ProtocolVersion = S2ProtocolVersion,
            DecodeElapsedTicks = decodeStopwatch.Elapsed.Ticks,
            ParseElapsedTicks = parseTicks,
            OnePassElapsedTicks = decodeStopwatch.Elapsed.Ticks + onePassParseTicks,
            ParseIterations = iterations,
            Replays = replays,
            Errors = [.. errors.OrderBy(static error => error.Path, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private static ReplayDecoderOptions CreateDecoderOptions(CompareDecoderOptions options)
        => new()
        {
            Details = options.Details,
            Initdata = options.Initdata,
            Metadata = options.Metadata,
            GameEvents = options.GameEvents,
            MessageEvents = options.MessageEvents,
            TrackerEvents = options.TrackerEvents,
            AttributeEvents = options.AttributeEvents,
        };

    private static NormalizedReplayDto Normalize(ParserReplayDto dto)
        => new()
        {
            Players = [.. dto.Players.Select(NormalizePlayer)],
        };

    private static NormalizedReplayPlayerDto NormalizePlayer(ParserReplayPlayerDto player)
        => new()
        {
            Name = player.Name,
            TeamId = player.TeamId,
            GamePos = player.GamePos,
            ToonId = new()
            {
                Region = player.Player.ToonId.Region,
                Realm = player.Player.ToonId.Realm,
                Id = player.Player.ToonId.Id,
            },
            Spawns = [.. player.Spawns.Select(NormalizeSpawn)],
        };

    private static NormalizedSpawnDto NormalizeSpawn(ParserSpawnDto spawn)
        => new()
        {
            Breakpoint = (int)spawn.Breakpoint,
            Income = spawn.Income,
            GasCount = spawn.GasCount,
            ArmyValue = spawn.ArmyValue,
            KilledValue = spawn.KilledValue,
            LostValue = spawn.LostValue,
            UpgradeSpent = spawn.UpgradeSpent,
            Units = [.. spawn.Units.Select(NormalizeUnit)],
        };

    private static NormalizedUnitDto NormalizeUnit(ParserUnitDto unit)
        => new()
        {
            Name = unit.Name,
            Count = unit.Count,
            Positions = [.. unit.Positions],
        };

    private static async Task<T?> ReadJsonAsync<T>(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream);
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value);
    }

    private readonly record struct DecodedReplay(string ReplayPath, Sc2Replay? Replay);
}
