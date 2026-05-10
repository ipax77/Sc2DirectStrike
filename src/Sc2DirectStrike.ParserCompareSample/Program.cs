using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using s2protocol.NET;
using s2protocol.NET.Models;
using NewBreakpoint = Sc2DirectStrike.Parser.Breakpoint;
using NewParser = Sc2DirectStrike.Parser.Sc2DirectStrikeParser;
using NewReplayDto = Sc2DirectStrike.Parser.ReplayDto;
using NewReplayPlayerDto = Sc2DirectStrike.Parser.ReplayPlayerDto;
using NewSpawnDto = Sc2DirectStrike.Parser.SpawnDto;
using NewUnitDto = Sc2DirectStrike.Parser.UnitDto;
using OldParser = dsstats.parser.DsstatsParser;
using OldReplayDto = dsstats.shared.ReplayDto;
using OldReplayPlayerDto = dsstats.shared.ReplayPlayerDto;
using OldSpawnDto = dsstats.shared.SpawnDto;
using OldUnitDto = dsstats.shared.UnitDto;

namespace Sc2DirectStrike.ParserCompareSample;

public static class Program
{
    private const int DefaultMaxReplaySizeKb = 300;
    private const int DecodeThreads = 8;
    private const int DefaultIterations = 1;
    private const int MaxDifferencesToPrint = 200;
    private const string ReplaySearchPattern = "Direct Strike TE*.SC2Replay";

    private static readonly string DefaultReplayDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "StarCraft II",
        "Accounts",
        "107095918",
        "2-S2-1-226401",
        "Replays",
        "Multiplayer");

    public static async Task<int> Main(string[] args)
    {
        if (!TryGetOptions(args, out string inputPath, out long maxReplaySizeBytes, out int iterations))
        {
            Console.Error.WriteLine("Usage: Sc2DirectStrike.ParserCompareSample [replay-directory-or-file] [max-size-kb] [iterations]");
            return 2;
        }

        if (!TryGetCandidateReplayFiles(inputPath, maxReplaySizeBytes, out FileInfo[] candidates, out string? candidateError))
        {
            Console.Error.WriteLine(candidateError);
            return 2;
        }

        if (candidates.Length == 0)
        {
            Console.Error.WriteLine(
                "No replay candidates found in '{0}' matching '{1}' with size <= {2:N0} bytes.",
                inputPath,
                ReplaySearchPattern,
                maxReplaySizeBytes);
            return 2;
        }

        using ReplayDecoder replayDecoder = new();
        ReplayDecoderOptions replayDecoderOptions = CreateDecoderOptions();
        List<ReplayComparison> comparisons = [];
        List<ReplayError> errors = [];
        string[] candidatePaths = [.. candidates.Select(static candidate => candidate.FullName)];

        IAsyncEnumerable<DecodeParallelResult> decodeResults = replayDecoder.DecodeParallelWithErrorReport(
            candidatePaths,
            Math.Min(DecodeThreads, candidatePaths.Length),
            replayDecoderOptions,
            CancellationToken.None);

        await foreach (DecodeParallelResult decodeResult in decodeResults)
        {
            string replayPath = decodeResult.ReplayPath;
            if (decodeResult.Exception is { Length: > 0 } decodeException)
            {
                errors.Add(new(replayPath, "DecodeError", decodeException));
                continue;
            }

            if (decodeResult.Sc2Replay is not { } replay)
            {
                errors.Add(new(replayPath, nameof(InvalidOperationException), "Decoder returned no replay and no exception."));
                continue;
            }

            try
            {
                comparisons.Add(CompareReplay(replayPath, replay, iterations));
            }
            catch (Exception ex)
            {
                errors.Add(new(replayPath, ex.GetType().Name, ex.Message));
            }
        }

        PrintSummary(inputPath, maxReplaySizeBytes, iterations, candidates.Length, comparisons, errors);
        PrintDifferences("Roster differences", comparisons.SelectMany(static comparison => comparison.RosterDifferences));
        PrintDifferences("Spawn differences", comparisons.SelectMany(static comparison => comparison.SpawnDifferences));
        PrintErrors(errors);

        if (errors.Count > 0)
        {
            return 3;
        }

        if (comparisons.Any(static comparison => comparison.HasDifferences))
        {
            return 1;
        }

        Console.WriteLine("PASS: no roster or spawn differences found.");
        return 0;
    }

    private static bool TryGetOptions(string[] args, out string inputPath, out long maxReplaySizeBytes, out int iterations)
    {
        inputPath = args.Length > 0 ? args[0] : DefaultReplayDirectory;
        maxReplaySizeBytes = DefaultMaxReplaySizeKb * 1024L;
        iterations = DefaultIterations;

        if (args.Length > 3)
        {
            return false;
        }

        if (args.Length > 1)
        {
            if (!long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out long maxReplaySizeKb)
                || maxReplaySizeKb < 0)
            {
                return false;
            }

            maxReplaySizeBytes = maxReplaySizeKb * 1024L;
        }

        if (args.Length > 2)
        {
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                || iterations <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetCandidateReplayFiles(
        string inputPath,
        long maxReplaySizeBytes,
        out FileInfo[] candidates,
        out string? error)
    {
        candidates = [];
        error = null;

        if (File.Exists(inputPath))
        {
            FileInfo file = new(inputPath);
            candidates = file.Length <= maxReplaySizeBytes ? [file] : [];
            return true;
        }

        if (Directory.Exists(inputPath))
        {
            candidates =
            [
                .. Directory
                    .EnumerateFiles(inputPath, ReplaySearchPattern, SearchOption.TopDirectoryOnly)
                    .Select(static path => new FileInfo(path))
                    .Where(file => file.Length <= maxReplaySizeBytes)
                    .OrderByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Skip(200)
                    .Take(100),
            ];
            return true;
        }

        error = $"Replay directory or file does not exist: {inputPath}";
        return false;
    }

    private static ReplayDecoderOptions CreateDecoderOptions()
    {
        return new()
        {
            Details = true,
            Initdata = true,
            Metadata = true,
            GameEvents = false,
            MessageEvents = true,
            TrackerEvents = true,
            AttributeEvents = false,
        };
    }

    private static ReplayComparison CompareReplay(string replayPath, Sc2Replay replay, int iterations)
    {
        ParseTimingResult timing = MeasureParsers(replay, iterations);
        List<Difference> rosterDifferences = [];
        List<Difference> spawnDifferences = [];

        List<PlayerMatch> playerMatches = MatchPlayers(timing.NewDto.Players, timing.OldDto.Players);
        CompareRoster(replayPath, playerMatches, rosterDifferences);
        CompareSpawns(replayPath, playerMatches, spawnDifferences);

        return new(
            replayPath,
            timing.NewElapsed,
            timing.OldElapsed,
            timing.Iterations,
            rosterDifferences,
            spawnDifferences);
    }

    private static ParseTimingResult MeasureParsers(Sc2Replay replay, int iterations)
    {
        NewReplayDto? newDto = null;
        OldReplayDto? oldDto = null;
        long newTicks = 0;
        long oldTicks = 0;
        Stopwatch stopwatch = new();

        for (int i = 0; i < iterations; i++)
        {
            if (i % 2 == 0)
            {
                newTicks += Measure(stopwatch, () => newDto = NewParser.ParseDto(replay));
                oldTicks += Measure(stopwatch, () => oldDto = OldParser.ParseReplay(replay));
            }
            else
            {
                oldTicks += Measure(stopwatch, () => oldDto = OldParser.ParseReplay(replay));
                newTicks += Measure(stopwatch, () => newDto = NewParser.ParseDto(replay));
            }
        }

        return new(
            newDto ?? throw new InvalidOperationException("New parser returned no DTO."),
            oldDto ?? throw new InvalidOperationException("Old parser returned no DTO."),
            TimeSpan.FromTicks(newTicks),
            TimeSpan.FromTicks(oldTicks),
            iterations);
    }

    private static long Measure(Stopwatch stopwatch, Action action)
    {
        stopwatch.Restart();
        action();
        stopwatch.Stop();
        return stopwatch.ElapsedTicks;
    }

    private static List<PlayerMatch> MatchPlayers(
        IReadOnlyCollection<NewReplayPlayerDto> newPlayers,
        IReadOnlyList<OldReplayPlayerDto> oldPlayers)
    {
        NewReplayPlayerDto[] newPlayerArray = [.. newPlayers];
        Dictionary<ToonKey, List<IndexedPlayer<NewReplayPlayerDto>>> newPlayersByToon = GroupNewPlayersByToon(newPlayerArray);
        Dictionary<ToonKey, List<IndexedPlayer<OldReplayPlayerDto>>> oldPlayersByToon = GroupOldPlayersByToon(oldPlayers);
        List<PlayerMatch> matches = [];

        foreach (ToonKey key in newPlayersByToon.Keys.Concat(oldPlayersByToon.Keys).Distinct().Order())
        {
            newPlayersByToon.TryGetValue(key, out List<IndexedPlayer<NewReplayPlayerDto>>? newGroup);
            oldPlayersByToon.TryGetValue(key, out List<IndexedPlayer<OldReplayPlayerDto>>? oldGroup);
            newGroup ??= [];
            oldGroup ??= [];

            int pairCount = Math.Max(newGroup.Count, oldGroup.Count);
            for (int i = 0; i < pairCount; i++)
            {
                IndexedPlayer<NewReplayPlayerDto>? newPlayer = i < newGroup.Count ? newGroup[i] : null;
                IndexedPlayer<OldReplayPlayerDto>? oldPlayer = i < oldGroup.Count ? oldGroup[i] : null;
                string matchKind = GetRosterMatchKind(newGroup.Count, oldGroup.Count);

                matches.Add(new(
                    newPlayer?.Index,
                    oldPlayer?.Index,
                    matchKind,
                    key,
                    newPlayer?.Player,
                    oldPlayer?.Player));
            }
        }

        return matches;
    }

    private static string GetRosterMatchKind(int newCount, int oldCount)
    {
        if (newCount == 0)
        {
            return "MissingNew";
        }

        if (oldCount == 0)
        {
            return "MissingOld";
        }

        if (newCount != 1 || oldCount != 1)
        {
            return $"DuplicateToonId(new={newCount},old={oldCount})";
        }

        return "ToonId";
    }

    private static Dictionary<ToonKey, List<IndexedPlayer<NewReplayPlayerDto>>> GroupNewPlayersByToon(IReadOnlyList<NewReplayPlayerDto> players)
    {
        Dictionary<ToonKey, List<IndexedPlayer<NewReplayPlayerDto>>> playersByToon = [];
        for (int i = 0; i < players.Count; i++)
        {
            ToonKey key = ToonKey.FromNew(players[i]);
            if (!playersByToon.TryGetValue(key, out List<IndexedPlayer<NewReplayPlayerDto>>? group))
            {
                group = [];
                playersByToon.Add(key, group);
            }

            group.Add(new(i, players[i]));
        }

        return playersByToon;
    }

    private static Dictionary<ToonKey, List<IndexedPlayer<OldReplayPlayerDto>>> GroupOldPlayersByToon(IReadOnlyList<OldReplayPlayerDto> players)
    {
        Dictionary<ToonKey, List<IndexedPlayer<OldReplayPlayerDto>>> playersByToon = [];
        for (int i = 0; i < players.Count; i++)
        {
            ToonKey key = ToonKey.FromOld(players[i]);
            if (!playersByToon.TryGetValue(key, out List<IndexedPlayer<OldReplayPlayerDto>>? group))
            {
                group = [];
                playersByToon.Add(key, group);
            }

            group.Add(new(i, players[i]));
        }

        return playersByToon;
    }

    private static void CompareRoster(string replayPath, List<PlayerMatch> playerMatches, List<Difference> differences)
    {
        foreach (PlayerMatch match in playerMatches)
        {
            string playerLabel = match.PlayerLabel;
            if (match.NewPlayer is null || match.OldPlayer is null)
            {
                differences.Add(new(
                    replayPath,
                    playerLabel,
                    "Roster",
                    "PlayerIdentity",
                    match.NewPlayer is null ? "<missing>" : DescribeNewPlayer(match.NewPlayer),
                    match.OldPlayer is null ? "<missing>" : DescribeOldPlayer(match.OldPlayer)));
                continue;
            }

            if (!string.Equals(match.MatchKind, "ToonId", StringComparison.Ordinal))
            {
                differences.Add(new(replayPath, playerLabel, "Roster", "PlayerIdentity", "Unique ToonId", match.MatchKind));
            }

            AddDifferenceIfChanged(differences, replayPath, playerLabel, "Roster", "GamePos", match.NewPlayer.GamePos, match.OldPlayer.GamePos);
        }
    }

    private static void CompareSpawns(string replayPath, List<PlayerMatch> playerMatches, List<Difference> differences)
    {
        foreach (PlayerMatch match in playerMatches)
        {
            if (match.NewPlayer is null || match.OldPlayer is null)
            {
                continue;
            }

            Dictionary<int, List<NewSpawnDto>> newSpawnsByBreakpoint = GroupNewSpawnsByBreakpoint(match.NewPlayer.Spawns);
            Dictionary<int, List<OldSpawnDto>> oldSpawnsByBreakpoint = GroupOldSpawnsByBreakpoint(match.OldPlayer.Spawns);
            int[] breakpoints = [.. newSpawnsByBreakpoint.Keys.Concat(oldSpawnsByBreakpoint.Keys).Distinct().Order()];

            foreach (int breakpoint in breakpoints)
            {
                string category = $"Spawn:{FormatBreakpoint(breakpoint)}";
                bool hasNew = newSpawnsByBreakpoint.TryGetValue(breakpoint, out List<NewSpawnDto>? newSpawns);
                bool hasOld = oldSpawnsByBreakpoint.TryGetValue(breakpoint, out List<OldSpawnDto>? oldSpawns);
                if (!hasNew || !hasOld)
                {
                    differences.Add(new(
                        replayPath,
                        match.PlayerLabel,
                        category,
                        "Spawn",
                        hasNew ? DescribeNewSpawn(newSpawns![^1]) : "<missing>",
                        hasOld ? DescribeOldSpawn(oldSpawns![^1]) : "<missing>"));
                    continue;
                }

                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "SpawnCount", newSpawns!.Count, oldSpawns!.Count);

                NewSpawnDto newSpawn = newSpawns[^1];
                OldSpawnDto oldSpawn = oldSpawns[^1];
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "Income", newSpawn.Income, oldSpawn.Income);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "GasCount", newSpawn.GasCount, oldSpawn.GasCount);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "ArmyValue", newSpawn.ArmyValue, oldSpawn.ArmyValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "KilledValue", newSpawn.KilledValue, oldSpawn.KilledValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "LostValue", newSpawn.LostValue, oldSpawn.LostValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UpgradeSpent", newSpawn.UpgradeSpent, oldSpawn.UpgradeSpent);
                int newUnitTotal = GetNewUnitTotal(newSpawn.Units);
                int oldUnitTotal = GetOldUnitTotal(oldSpawn.Units);
                string newUnitHash = CreateNewUnitHash(newSpawn.Units);
                string oldUnitHash = CreateOldUnitHash(oldSpawn.Units);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UnitTotal", newUnitTotal, oldUnitTotal);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UnitPositionHash", newUnitHash, oldUnitHash);

                if (newUnitTotal != oldUnitTotal || !string.Equals(newUnitHash, oldUnitHash, StringComparison.Ordinal))
                {
                    AddUnitDetailDifferences(differences, replayPath, match.PlayerLabel, category, newSpawn.Units, oldSpawn.Units);
                }
            }
        }
    }

    private static Dictionary<int, List<NewSpawnDto>> GroupNewSpawnsByBreakpoint(IReadOnlyCollection<NewSpawnDto> spawns)
    {
        Dictionary<int, List<NewSpawnDto>> spawnsByBreakpoint = [];
        foreach (NewSpawnDto spawn in spawns)
        {
            int breakpoint = (int)spawn.Breakpoint;
            if (!spawnsByBreakpoint.TryGetValue(breakpoint, out List<NewSpawnDto>? breakpointSpawns))
            {
                breakpointSpawns = [];
                spawnsByBreakpoint.Add(breakpoint, breakpointSpawns);
            }

            breakpointSpawns.Add(spawn);
        }

        return spawnsByBreakpoint;
    }

    private static Dictionary<int, List<OldSpawnDto>> GroupOldSpawnsByBreakpoint(IReadOnlyCollection<OldSpawnDto> spawns)
    {
        Dictionary<int, List<OldSpawnDto>> spawnsByBreakpoint = [];
        foreach (OldSpawnDto spawn in spawns)
        {
            int breakpoint = (int)spawn.Breakpoint;
            if (!spawnsByBreakpoint.TryGetValue(breakpoint, out List<OldSpawnDto>? breakpointSpawns))
            {
                breakpointSpawns = [];
                spawnsByBreakpoint.Add(breakpoint, breakpointSpawns);
            }

            breakpointSpawns.Add(spawn);
        }

        return spawnsByBreakpoint;
    }

    private static void AddUnitDetailDifferences(
        List<Difference> differences,
        string replayPath,
        string player,
        string category,
        IReadOnlyCollection<NewUnitDto> newUnits,
        IReadOnlyCollection<OldUnitDto> oldUnits)
    {
        Dictionary<UnitToken, int> newUnitCounts = CreateNewUnitTokenCounts(newUnits);
        Dictionary<UnitToken, int> oldUnitCounts = CreateOldUnitTokenCounts(oldUnits);

        foreach (UnitToken token in newUnitCounts.Keys.Concat(oldUnitCounts.Keys).Distinct().Order())
        {
            int newCount = newUnitCounts.GetValueOrDefault(token);
            int oldCount = oldUnitCounts.GetValueOrDefault(token);
            if (newCount == oldCount)
            {
                continue;
            }

            differences.Add(new(
                replayPath,
                player,
                category,
                "UnitDetail",
                newCount == 0 ? "<missing>" : FormatUnitToken(token, newCount),
                oldCount == 0 ? "<missing>" : FormatUnitToken(token, oldCount),
                FormatIntDelta(newCount, oldCount)));
        }
    }

    private static Dictionary<UnitToken, int> CreateNewUnitTokenCounts(IReadOnlyCollection<NewUnitDto> units)
    {
        Dictionary<UnitToken, int> counts = [];
        foreach (NewUnitDto unit in units)
        {
            AddUnitTokens(counts, unit.Name, unit.Positions);
        }

        return counts;
    }

    private static Dictionary<UnitToken, int> CreateOldUnitTokenCounts(IReadOnlyCollection<OldUnitDto> units)
    {
        Dictionary<UnitToken, int> counts = [];
        foreach (OldUnitDto unit in units)
        {
            AddUnitTokens(counts, unit.Name, unit.Positions);
        }

        return counts;
    }

    private static void AddUnitTokens(Dictionary<UnitToken, int> counts, string name, IReadOnlyList<int> positions)
    {
        for (int i = 0; i + 1 < positions.Count; i += 2)
        {
            UnitToken token = new(name, positions[i], positions[i + 1]);
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }
    }

    private static string FormatUnitToken(UnitToken token, int count)
    {
        string text = $"{token.Name}@{token.X.ToString(CultureInfo.InvariantCulture)},{token.Y.ToString(CultureInfo.InvariantCulture)}";
        return count == 1 ? text : $"{text} x{count.ToString(CultureInfo.InvariantCulture)}";
    }

    private static void AddDifferenceIfChanged<T>(
        List<Difference> differences,
        string replayPath,
        string player,
        string category,
        string field,
        T newValue,
        T oldValue)
    {
        if (!EqualityComparer<T>.Default.Equals(newValue, oldValue))
        {
            differences.Add(new(
                replayPath,
                player,
                category,
                field,
                Convert.ToString(newValue, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(oldValue, CultureInfo.InvariantCulture) ?? string.Empty,
                FormatDelta(newValue, oldValue)));
        }
    }

    private static void AddEnumDifferenceIfChanged(
        List<Difference> differences,
        string replayPath,
        string player,
        string category,
        string field,
        int newValue,
        string newName,
        int oldValue,
        string oldName)
    {
        if (newValue != oldValue)
        {
            differences.Add(new(
                replayPath,
                player,
                category,
                field,
                $"{newValue}:{newName}",
                $"{oldValue}:{oldName}",
                FormatIntDelta(newValue, oldValue)));
        }
    }

    private static string FormatDelta<T>(T newValue, T oldValue)
    {
        if (newValue is int newInt && oldValue is int oldInt)
        {
            return FormatIntDelta(newInt, oldInt);
        }

        return string.Empty;
    }

    private static string FormatIntDelta(int newValue, int oldValue)
        => (newValue - oldValue).ToString("+0;-0;0", CultureInfo.InvariantCulture);

    private static string DescribeNewPlayer(NewReplayPlayerDto player)
        => $"{player.Name} team={player.TeamId} pos={player.GamePos} toon={FormatNewToon(player)}";

    private static string DescribeOldPlayer(OldReplayPlayerDto player)
        => $"{player.Name} team={player.TeamId} pos={player.GamePos} toon={FormatOldToon(player)}";

    private static string DescribeNewSpawn(NewSpawnDto spawn)
        => $"income={spawn.Income} army={spawn.ArmyValue} kills={spawn.KilledValue} units={GetNewUnitTotal(spawn.Units)}";

    private static string DescribeOldSpawn(OldSpawnDto spawn)
        => $"income={spawn.Income} army={spawn.ArmyValue} kills={spawn.KilledValue} units={GetOldUnitTotal(spawn.Units)}";

    private static string FormatNewToon(NewReplayPlayerDto player)
        => $"{player.Player.ToonId.Region}:{player.Player.ToonId.Realm}:{player.Player.ToonId.Id}";

    private static string FormatOldToon(OldReplayPlayerDto player)
        => $"{player.Player.ToonId.Region}:{player.Player.ToonId.Realm}:{player.Player.ToonId.Id}";

    private static string FormatBreakpoint(int value)
        => Enum.IsDefined(typeof(NewBreakpoint), value) ? ((NewBreakpoint)value).ToString() : value.ToString(CultureInfo.InvariantCulture);

    private static int GetNewUnitTotal(IReadOnlyCollection<NewUnitDto> units)
        => units.Sum(static unit => unit.Count);

    private static int GetOldUnitTotal(IReadOnlyCollection<OldUnitDto> units)
        => units.Sum(static unit => unit.Count);

    private static string CreateNewUnitHash(IReadOnlyCollection<NewUnitDto> units)
    {
        StringBuilder builder = new();
        foreach (NewUnitDto unit in units.OrderBy(static unit => unit.Name, StringComparer.Ordinal))
        {
            AppendUnit(builder, unit.Name, unit.Count, unit.Positions);
        }

        return CreateShortHash(builder.ToString());
    }

    private static string CreateOldUnitHash(IReadOnlyCollection<OldUnitDto> units)
    {
        StringBuilder builder = new();
        foreach (OldUnitDto unit in units.OrderBy(static unit => unit.Name, StringComparer.Ordinal))
        {
            AppendUnit(builder, unit.Name, unit.Count, unit.Positions);
        }

        return CreateShortHash(builder.ToString());
    }

    private static void AppendUnit(StringBuilder builder, string name, int count, IReadOnlyList<int> positions)
    {
        builder.Append(name);
        builder.Append('#');
        builder.Append(count.ToString(CultureInfo.InvariantCulture));
        builder.Append('@');
        builder.Append(positions.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        foreach (int position in positions)
        {
            builder.Append(position.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
        }

        builder.Append('|');
    }

    private static string CreateShortHash(string value)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant();
    }

    private static void PrintSummary(
        string inputPath,
        long maxReplaySizeBytes,
        int iterations,
        int candidateCount,
        IReadOnlyCollection<ReplayComparison> comparisons,
        IReadOnlyCollection<ReplayError> errors)
    {
        TimeSpan newTotal = TimeSpan.FromTicks(comparisons.Sum(static comparison => comparison.NewElapsed.Ticks));
        TimeSpan oldTotal = TimeSpan.FromTicks(comparisons.Sum(static comparison => comparison.OldElapsed.Ticks));
        int measuredParses = comparisons.Sum(static comparison => comparison.Iterations);
        double newAverageMs = measuredParses == 0 ? 0D : newTotal.TotalMilliseconds / measuredParses;
        double oldAverageMs = measuredParses == 0 ? 0D : oldTotal.TotalMilliseconds / measuredParses;
        double deltaMs = newAverageMs - oldAverageMs;
        double deltaPercent = oldAverageMs == 0D ? 0D : (deltaMs / oldAverageMs) * 100D;

        Console.WriteLine("Old parser comparison sample");
        Console.WriteLine("Input: {0}", inputPath);
        Console.WriteLine("Pattern: {0}", ReplaySearchPattern);
        Console.WriteLine("Max size: {0:N0} bytes", maxReplaySizeBytes);
        Console.WriteLine("Decode threads: {0:N0}", Math.Min(DecodeThreads, Math.Max(1, candidateCount)));
        Console.WriteLine("Parse iterations: {0:N0}", iterations);
        Console.WriteLine();
        Console.WriteLine("Candidates: {0:N0}", candidateCount);
        Console.WriteLine("Decoded: {0:N0}", comparisons.Count + errors.Count(static error => !string.Equals(error.ErrorType, "DecodeError", StringComparison.Ordinal)));
        Console.WriteLine("Compared: {0:N0}", comparisons.Count);
        Console.WriteLine("Parse errors: {0:N0}", errors.Count(static error => !string.Equals(error.ErrorType, "DecodeError", StringComparison.Ordinal)));
        Console.WriteLine("Decode errors: {0:N0}", errors.Count(static error => string.Equals(error.ErrorType, "DecodeError", StringComparison.Ordinal)));
        Console.WriteLine("Roster-diff replays: {0:N0}", comparisons.Count(static comparison => comparison.RosterDifferences.Count > 0));
        Console.WriteLine("Spawn-diff replays: {0:N0}", comparisons.Count(static comparison => comparison.SpawnDifferences.Count > 0));
        Console.WriteLine("Roster differences: {0:N0}", comparisons.Sum(static comparison => comparison.RosterDifferences.Count));
        Console.WriteLine("Spawn differences: {0:N0}", comparisons.Sum(static comparison => comparison.SpawnDifferences.Count));
        Console.WriteLine();
        Console.WriteLine("Parse time (decode excluded):");
        Console.WriteLine("  New total: {0:N2} ms", newTotal.TotalMilliseconds);
        Console.WriteLine("  Old total: {0:N2} ms", oldTotal.TotalMilliseconds);
        Console.WriteLine("  New avg: {0:N2} ms", newAverageMs);
        Console.WriteLine("  Old avg: {0:N2} ms", oldAverageMs);
        Console.WriteLine("  Avg delta: {0:+0.00;-0.00;0.00} ms ({1:+0.00;-0.00;0.00}%)", deltaMs, deltaPercent);
        Console.WriteLine();
    }

    private static void PrintDifferences(string title, IEnumerable<Difference> differences)
    {
        Difference[] allDifferences = [.. differences];
        if (allDifferences.Length == 0)
        {
            return;
        }

        Console.WriteLine(title + ":");
        Console.WriteLine("Replay | Player | Category | Field | New | Old | Delta");
        foreach (Difference difference in allDifferences.Take(MaxDifferencesToPrint))
        {
            Console.WriteLine(
                "{0} | {1} | {2} | {3} | {4} | {5} | {6}",
                Path.GetFileName(difference.ReplayPath),
                difference.Player,
                difference.Category,
                difference.Field,
                difference.NewValue,
                difference.OldValue,
                difference.Delta);
        }

        int omittedCount = allDifferences.Length - MaxDifferencesToPrint;
        if (omittedCount > 0)
        {
            Console.WriteLine("Omitted {0:N0} more {1}.", omittedCount, title.ToLowerInvariant());
        }

        Console.WriteLine();
    }

    private static void PrintErrors(IReadOnlyCollection<ReplayError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine("Replay errors:");
        foreach (ReplayError error in errors)
        {
            Console.Error.WriteLine("  {0}: {1}: {2}", error.Path, error.ErrorType, error.Message);
        }
    }

    private sealed record ReplayComparison(
        string ReplayPath,
        TimeSpan NewElapsed,
        TimeSpan OldElapsed,
        int Iterations,
        List<Difference> RosterDifferences,
        List<Difference> SpawnDifferences)
    {
        public bool HasDifferences => RosterDifferences.Count > 0 || SpawnDifferences.Count > 0;
    }

    private sealed record ParseTimingResult(
        NewReplayDto NewDto,
        OldReplayDto OldDto,
        TimeSpan NewElapsed,
        TimeSpan OldElapsed,
        int Iterations);

    private sealed record PlayerMatch(
        int? NewIndex,
        int? OldIndex,
        string MatchKind,
        ToonKey ToonKey,
        NewReplayPlayerDto? NewPlayer,
        OldReplayPlayerDto? OldPlayer)
    {
        public string PlayerLabel
        {
            get
            {
                string name = NewPlayer?.Name ?? OldPlayer?.Name ?? "<unknown>";
                string newIndex = NewIndex?.ToString(CultureInfo.InvariantCulture) ?? "-";
                string oldIndex = OldIndex?.ToString(CultureInfo.InvariantCulture) ?? "-";
                return $"{name} [{ToonKey}] (new#{newIndex}/old#{oldIndex})";
            }
        }
    }

    private readonly record struct IndexedPlayer<T>(int Index, T Player);

    private readonly record struct UnitToken(string Name, int X, int Y) : IComparable<UnitToken>
    {
        public int CompareTo(UnitToken other)
        {
            int nameComparison = string.Compare(Name, other.Name, StringComparison.Ordinal);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            int xComparison = X.CompareTo(other.X);
            return xComparison != 0 ? xComparison : Y.CompareTo(other.Y);
        }
    }

    private readonly record struct ToonKey(int Region, int Realm, int Id) : IComparable<ToonKey>
    {
        public static ToonKey FromNew(NewReplayPlayerDto player)
            => new(player.Player.ToonId.Region, player.Player.ToonId.Realm, player.Player.ToonId.Id);

        public static ToonKey FromOld(OldReplayPlayerDto player)
            => new(player.Player.ToonId.Region, player.Player.ToonId.Realm, player.Player.ToonId.Id);

        public int CompareTo(ToonKey other)
        {
            int regionComparison = Region.CompareTo(other.Region);
            if (regionComparison != 0)
            {
                return regionComparison;
            }

            int realmComparison = Realm.CompareTo(other.Realm);
            return realmComparison != 0 ? realmComparison : Id.CompareTo(other.Id);
        }

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"{Region}:{Realm}:{Id}");
    }

    private sealed record Difference(
        string ReplayPath,
        string Player,
        string Category,
        string Field,
        string NewValue,
        string OldValue,
        string Delta = "");

    private sealed record ReplayError(string Path, string ErrorType, string Message);
}
