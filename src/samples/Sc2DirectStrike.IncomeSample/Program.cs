using System.Globalization;
using s2protocol.NET;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.IncomeSample;

public static class Program
{
    private const double GameLoopsPerSecond = 22.4D;
    private const double BaseIncomePerSecond = 7.5D;
    private const double RefineryIncomePerSecond = 0.5D;
    private const double MiddleIncomePerSecond = 1D;

    private static readonly (Breakpoint Breakpoint, int Gameloop)[] BreakpointGameloops =
    [
        (Breakpoint.Min5, 6_720),
        (Breakpoint.Min10, 13_440),
        (Breakpoint.Min15, 20_160),
    ];

    private static readonly int[] RefineryCosts = [150, 225, 300, 375, 500];

    private static readonly string[] DefaultReplayNames =
    [
        "Direct Strike (7586).SC2Replay",
        "Direct Strike TE (1046).SC2Replay",
        "Direct Strike TE (1106).SC2Replay",
    ];

    public static async Task<int> Main(string[] args)
    {
        string[] replayPaths = args.Length == 0
            ? [.. DefaultReplayNames.Select(name => Path.Combine(AppContext.BaseDirectory, "testdata", name))]
            : args;

        ReplayDecoderOptions replayDecoderOptions = new()
        {
            Details = true,
            Initdata = true,
            Metadata = true,
            GameEvents = false,
            MessageEvents = false,
            TrackerEvents = true,
            AttributeEvents = false,
        };

        using ReplayDecoder replayDecoder = new();
        List<ReplayError> errors = [];

        Console.WriteLine("Replay | Player | Team | Breakpoint | Gameloop | Time | OffsetFromBreakpoint | StatsIncome | FormulaIncome | Delta | DtoGas | FormulaGas | StatsRefCost | FormulaRefCost | RefCostDelta | GasCause");

        foreach (string replayPath in replayPaths)
        {
            try
            {
                await PrintReplayIncomeDifferences(replayDecoder, replayDecoderOptions, replayPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add(new(replayPath, ex.GetType().Name, ex.Message));
            }
        }

        if (errors.Count == 0)
        {
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Replay errors:");
        foreach (ReplayError error in errors)
        {
            Console.Error.WriteLine(
                "  {0}: {1}: {2}",
                error.Path,
                error.ErrorType,
                error.Message);
        }

        return 3;
    }

    private static async Task PrintReplayIncomeDifferences(
        ReplayDecoder replayDecoder,
        ReplayDecoderOptions replayDecoderOptions,
        string replayPath)
    {
        Sc2Replay replay = await replayDecoder.DecodeAsync(replayPath, replayDecoderOptions, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Could not decode replay '{0}'.", replayPath));

        DirectStrikeReplay directStrikeReplay = Sc2DirectStrikeParser.Parse(replay);
        ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);
        MiddleControlHelper middleControl = new(dto);
        string replayName = Path.GetFileName(replayPath);

        DirectStrikePlayer[] players = [.. directStrikeReplay.Players];
        ReplayPlayerDto[] dtoPlayers = [.. dto.Players];
        int playerCount = Math.Min(players.Length, dtoPlayers.Length);

        for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            DirectStrikePlayer player = players[playerIndex];
            ReplayPlayerDto dtoPlayer = dtoPlayers[playerIndex];
            Dictionary<Breakpoint, SpawnDto> dtoSpawnsByBreakpoint = dtoPlayer.Spawns.ToDictionary(
                spawn => spawn.Breakpoint,
                spawn => spawn);

            foreach (ExpectedBreakpointSpawn expected in GetExpectedBreakpointSpawns(player))
            {
                if (!dtoSpawnsByBreakpoint.TryGetValue(expected.Breakpoint, out SpawnDto? dtoSpawn)
                    || expected.Spawn.SummaryStats is not { } stats)
                {
                    continue;
                }

                IncomeFormulaBreakdown formulaBreakdown = GetExpectedDtoFormulaIncome(player, stats.Gameloop, middleControl);
                int formulaIncome = formulaBreakdown.Income;
                int delta = dtoSpawn.Income - formulaIncome;
                TimeSpan expectedBreakpointTime = ToExpectedDtoTime(GetNominalGameloop(expected.Breakpoint, stats.Gameloop));
                TimeSpan offsetFromBreakpoint = stats.Time - expectedBreakpointTime;
                int statsRefineryCost = GetExpectedDtoRefineryCost(player, stats.Gameloop);
                int refineryCostDelta = statsRefineryCost - formulaBreakdown.RefineryCost;
                string gasCause = dtoSpawn.GasCount != formulaBreakdown.RefineryCount || refineryCostDelta != 0
                    ? "yes"
                    : "no";

                Console.WriteLine(
                    "{0} | {1} | {2} | {3} | {4} | {5:c} | {6:+0.##;-0.##;0}s | {7} | {8} | {9:+0;-0;0} | {10} | {11} | {12} | {13} | {14:+0;-0;0} | {15}",
                    replayName,
                    player.Name,
                    player.TeamId,
                    expected.Breakpoint,
                    stats.Gameloop,
                    stats.Time,
                    offsetFromBreakpoint.TotalSeconds,
                    dtoSpawn.Income,
                    formulaIncome,
                    delta,
                    dtoSpawn.GasCount,
                    formulaBreakdown.RefineryCount,
                    statsRefineryCost,
                    formulaBreakdown.RefineryCost,
                    refineryCostDelta,
                    gasCause);
            }
        }
    }

    private static int GetNominalGameloop(Breakpoint breakpoint, int fallbackGameloop)
    {
        foreach ((Breakpoint candidate, int gameloop) in BreakpointGameloops)
        {
            if (candidate == breakpoint)
            {
                return gameloop;
            }
        }

        return fallbackGameloop;
    }

    private static IEnumerable<ExpectedBreakpointSpawn> GetExpectedBreakpointSpawns(DirectStrikePlayer player)
    {
        DirectStrikePlayerSpawn[] statsBackedSpawns =
        [
            .. player.Spawns.Where(spawn => spawn.SummaryStats is { MineralsCollectionRate: > 0 }),
        ];

        if (statsBackedSpawns.Length == 0)
        {
            yield break;
        }

        foreach ((Breakpoint breakpoint, int gameloop) in BreakpointGameloops)
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

    private static IncomeFormulaBreakdown GetExpectedDtoFormulaIncome(
        DirectStrikePlayer player,
        int targetGameloop,
        MiddleControlHelper middleControl)
    {
        (TimeSpan team1MiddleControl, TimeSpan team2MiddleControl) = middleControl.GetControl(ToExpectedDtoTime(targetGameloop));
        double middleControlSeconds = player.TeamId == 1
            ? team1MiddleControl.TotalSeconds
            : player.TeamId == 2
                ? team2MiddleControl.TotalSeconds
                : 0D;
        double middleIncome = middleControlSeconds * MiddleIncomePerSecond;
        double targetSeconds = targetGameloop / GameLoopsPerSecond;
        double income = (targetSeconds * BaseIncomePerSecond) + middleIncome;

        int refineryCount = 0;
        foreach (TimeSpan refinery in player.RefineryTimes)
        {
            int refineryGameloop = (int)(refinery.TotalSeconds * GameLoopsPerSecond);
            if (refineryGameloop < targetGameloop)
            {
                refineryCount++;
                income += ((targetGameloop - refineryGameloop) / GameLoopsPerSecond) * RefineryIncomePerSecond;
            }
        }

        int refineryCost = GetExpectedDtoRefineryCost(player, targetGameloop);
        return new((int)income - refineryCost, refineryCount, refineryCost);
    }

    private static int GetExpectedDtoRefineryCost(DirectStrikePlayer player, int targetGameloop)
    {
        int refineryCount = player.RefineryTimes.Count(refinery => (int)(refinery.TotalSeconds * GameLoopsPerSecond) < targetGameloop);
        int cost = 0;
        for (int i = 0; i < refineryCount && i < RefineryCosts.Length; i++)
        {
            cost += RefineryCosts[i];
        }

        return cost;
    }

    private static TimeSpan ToExpectedDtoTime(int gameloop)
    {
        return TimeSpan.FromSeconds(gameloop / GameLoopsPerSecond);
    }

    private readonly record struct ExpectedBreakpointSpawn(Breakpoint Breakpoint, DirectStrikePlayerSpawn Spawn);

    private readonly record struct IncomeFormulaBreakdown(int Income, int RefineryCount, int RefineryCost);

    private sealed record ReplayError(string Path, string ErrorType, string Message);
}
