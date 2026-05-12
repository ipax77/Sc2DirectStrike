using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sc2DirectStrike.ParserCompareSample.Shared;

namespace Sc2DirectStrike.ParserCompareSample;

public static class Program
{
    private const int DefaultMaxReplaySizeKb = 300;
    private const int DecodeThreads = 8;
    private const int DefaultIterations = 1;
    private const int MaxDifferencesToPrint = 200;
    private const string ReplaySearchPattern = "Direct Strike TE*.SC2Replay";
    private const string NewWorkerProjectName = "Sc2DirectStrike.ParserCompareSample.NewStack";
    private const string OldWorkerProjectName = "Sc2DirectStrike.ParserCompareSample.OldStack";

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

        CompareRequest request = new()
        {
            ReplayPaths = [.. candidates.Select(static candidate => candidate.FullName)],
            DecodeThreads = Math.Min(DecodeThreads, candidates.Length),
            ParseIterations = iterations,
            DecoderOptions = CreateDecoderOptions(),
        };

        CompareWorkerResult oldResult;
        CompareWorkerResult newResult;
        try
        {
            oldResult = await RunWorkerAsync(OldWorkerProjectName, request);
            newResult = await RunWorkerAsync(NewWorkerProjectName, request);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }

        List<ReplayError> errors =
        [
            .. oldResult.Errors.Select(error => PrefixError(oldResult, error)),
            .. newResult.Errors.Select(error => PrefixError(newResult, error)),
        ];
        List<ReplayComparison> comparisons = CompareResults(newResult, oldResult, errors);

        PrintSummary(inputPath, maxReplaySizeBytes, iterations, candidates.Length, newResult, oldResult, comparisons, errors);
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

    private static CompareDecoderOptions CreateDecoderOptions()
        => new()
        {
            Details = true,
            Initdata = true,
            Metadata = true,
            GameEvents = false,
            MessageEvents = true,
            TrackerEvents = true,
            AttributeEvents = false,
        };

    private static async Task<CompareWorkerResult> RunWorkerAsync(string projectName, CompareRequest request)
    {
        string workerPath = ResolveWorkerPath(projectName);
        string tempDirectory = Path.Combine(Path.GetTempPath(), "Sc2DirectStrike.ParserCompareSample", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string requestPath = Path.Combine(tempDirectory, "request.json");
        string resultPath = Path.Combine(tempDirectory, "result.json");

        try
        {
            await WriteJsonAsync(requestPath, request);
            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(workerPath);
            startInfo.ArgumentList.Add(requestPath);
            startInfo.ArgumentList.Add(resultPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start worker '{projectName}'.");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Worker '{projectName}' failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}{stdout}");
            }

            CompareWorkerResult? result = await ReadJsonAsync<CompareWorkerResult>(resultPath);
            return result ?? throw new InvalidOperationException($"Worker '{projectName}' wrote no result.");
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static string ResolveWorkerPath(string projectName)
    {
        string envVar = projectName.EndsWith(".NewStack", StringComparison.Ordinal)
            ? "SC2DIRECTSTRIKE_COMPARE_NEW_WORKER"
            : "SC2DIRECTSTRIKE_COMPARE_OLD_WORKER";
        string? configuredPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        DirectoryInfo baseDirectory = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string targetFramework = baseDirectory.Name;
        DirectoryInfo? configurationDirectory = baseDirectory.Parent;
        DirectoryInfo? binDirectory = configurationDirectory?.Parent;
        DirectoryInfo? projectDirectory = binDirectory?.Parent;
        DirectoryInfo? srcDirectory = projectDirectory?.Parent;
        if (configurationDirectory is null || srcDirectory is null)
        {
            throw new InvalidOperationException($"Could not resolve worker path for '{projectName}'.");
        }

        string workerPath = Path.Combine(
            srcDirectory.FullName,
            projectName,
            "bin",
            configurationDirectory.Name,
            targetFramework,
            projectName + ".dll");

        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException(
                $"Worker '{projectName}' was not found. Build the compare sample project so worker project references are built.",
                workerPath);
        }

        return workerPath;
    }

    private static List<ReplayComparison> CompareResults(
        CompareWorkerResult newResult,
        CompareWorkerResult oldResult,
        List<ReplayError> errors)
    {
        Dictionary<string, NormalizedReplayResult> newReplaysByPath = newResult.Replays.ToDictionary(
            static replay => replay.ReplayPath,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NormalizedReplayResult> oldReplaysByPath = oldResult.Replays.ToDictionary(
            static replay => replay.ReplayPath,
            StringComparer.OrdinalIgnoreCase);
        List<ReplayComparison> comparisons = [];

        foreach (string replayPath in newReplaysByPath.Keys.Concat(oldReplaysByPath.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            bool hasNew = newReplaysByPath.TryGetValue(replayPath, out NormalizedReplayResult? newReplay);
            bool hasOld = oldReplaysByPath.TryGetValue(replayPath, out NormalizedReplayResult? oldReplay);
            if (!hasNew || !hasOld)
            {
                errors.Add(new(
                    replayPath,
                    "ComparisonError",
                    hasNew ? "Old stack did not produce a parsed replay." : "New stack did not produce a parsed replay."));
                continue;
            }

            List<Difference> rosterDifferences = [];
            List<Difference> spawnDifferences = [];
            List<PlayerMatch> playerMatches = MatchPlayers(newReplay!.Replay.Players, oldReplay!.Replay.Players);
            CompareRoster(replayPath, playerMatches, rosterDifferences);
            CompareSpawns(replayPath, playerMatches, spawnDifferences);

            comparisons.Add(new(replayPath, rosterDifferences, spawnDifferences));
        }

        return comparisons;
    }

    private static List<PlayerMatch> MatchPlayers(
        IReadOnlyCollection<NormalizedReplayPlayerDto> newPlayers,
        IReadOnlyList<NormalizedReplayPlayerDto> oldPlayers)
    {
        NormalizedReplayPlayerDto[] newPlayerArray = [.. newPlayers];
        Dictionary<ToonKey, List<IndexedPlayer<NormalizedReplayPlayerDto>>> newPlayersByToon = GroupPlayersByToon(newPlayerArray);
        Dictionary<ToonKey, List<IndexedPlayer<NormalizedReplayPlayerDto>>> oldPlayersByToon = GroupPlayersByToon(oldPlayers);
        List<PlayerMatch> matches = [];

        foreach (ToonKey key in newPlayersByToon.Keys.Concat(oldPlayersByToon.Keys).Distinct().Order())
        {
            newPlayersByToon.TryGetValue(key, out List<IndexedPlayer<NormalizedReplayPlayerDto>>? newGroup);
            oldPlayersByToon.TryGetValue(key, out List<IndexedPlayer<NormalizedReplayPlayerDto>>? oldGroup);
            newGroup ??= [];
            oldGroup ??= [];

            int pairCount = Math.Max(newGroup.Count, oldGroup.Count);
            for (int i = 0; i < pairCount; i++)
            {
                IndexedPlayer<NormalizedReplayPlayerDto>? newPlayer = i < newGroup.Count ? newGroup[i] : null;
                IndexedPlayer<NormalizedReplayPlayerDto>? oldPlayer = i < oldGroup.Count ? oldGroup[i] : null;
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

    private static Dictionary<ToonKey, List<IndexedPlayer<NormalizedReplayPlayerDto>>> GroupPlayersByToon(IReadOnlyList<NormalizedReplayPlayerDto> players)
    {
        Dictionary<ToonKey, List<IndexedPlayer<NormalizedReplayPlayerDto>>> playersByToon = [];
        for (int i = 0; i < players.Count; i++)
        {
            ToonKey key = ToonKey.FromPlayer(players[i]);
            if (!playersByToon.TryGetValue(key, out List<IndexedPlayer<NormalizedReplayPlayerDto>>? group))
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
                    match.NewPlayer is null ? "<missing>" : DescribePlayer(match.NewPlayer),
                    match.OldPlayer is null ? "<missing>" : DescribePlayer(match.OldPlayer)));
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

            Dictionary<int, List<NormalizedSpawnDto>> newSpawnsByBreakpoint = GroupSpawnsByBreakpoint(match.NewPlayer.Spawns);
            Dictionary<int, List<NormalizedSpawnDto>> oldSpawnsByBreakpoint = GroupSpawnsByBreakpoint(match.OldPlayer.Spawns);
            int[] breakpoints = [.. newSpawnsByBreakpoint.Keys.Concat(oldSpawnsByBreakpoint.Keys).Distinct().Order()];

            foreach (int breakpoint in breakpoints)
            {
                string category = $"Spawn:{FormatBreakpoint(breakpoint)}";
                bool hasNew = newSpawnsByBreakpoint.TryGetValue(breakpoint, out List<NormalizedSpawnDto>? newSpawns);
                bool hasOld = oldSpawnsByBreakpoint.TryGetValue(breakpoint, out List<NormalizedSpawnDto>? oldSpawns);
                if (!hasNew || !hasOld)
                {
                    differences.Add(new(
                        replayPath,
                        match.PlayerLabel,
                        category,
                        "Spawn",
                        hasNew ? DescribeSpawn(newSpawns![^1]) : "<missing>",
                        hasOld ? DescribeSpawn(oldSpawns![^1]) : "<missing>"));
                    continue;
                }

                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "SpawnCount", newSpawns!.Count, oldSpawns!.Count);

                NormalizedSpawnDto newSpawn = newSpawns[^1];
                NormalizedSpawnDto oldSpawn = oldSpawns[^1];
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "Income", newSpawn.Income, oldSpawn.Income);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "GasCount", newSpawn.GasCount, oldSpawn.GasCount);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "ArmyValue", newSpawn.ArmyValue, oldSpawn.ArmyValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "KilledValue", newSpawn.KilledValue, oldSpawn.KilledValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "LostValue", newSpawn.LostValue, oldSpawn.LostValue);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UpgradeSpent", newSpawn.UpgradeSpent, oldSpawn.UpgradeSpent);
                int newUnitTotal = GetUnitTotal(newSpawn.Units);
                int oldUnitTotal = GetUnitTotal(oldSpawn.Units);
                string newUnitHash = CreateUnitHash(newSpawn.Units);
                string oldUnitHash = CreateUnitHash(oldSpawn.Units);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UnitTotal", newUnitTotal, oldUnitTotal);
                AddDifferenceIfChanged(differences, replayPath, match.PlayerLabel, category, "UnitPositionHash", newUnitHash, oldUnitHash);

                if (newUnitTotal != oldUnitTotal || !string.Equals(newUnitHash, oldUnitHash, StringComparison.Ordinal))
                {
                    AddUnitDetailDifferences(differences, replayPath, match.PlayerLabel, category, newSpawn.Units, oldSpawn.Units);
                }
            }
        }
    }

    private static Dictionary<int, List<NormalizedSpawnDto>> GroupSpawnsByBreakpoint(IReadOnlyCollection<NormalizedSpawnDto> spawns)
    {
        Dictionary<int, List<NormalizedSpawnDto>> spawnsByBreakpoint = [];
        foreach (NormalizedSpawnDto spawn in spawns)
        {
            if (!spawnsByBreakpoint.TryGetValue(spawn.Breakpoint, out List<NormalizedSpawnDto>? breakpointSpawns))
            {
                breakpointSpawns = [];
                spawnsByBreakpoint.Add(spawn.Breakpoint, breakpointSpawns);
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
        IReadOnlyCollection<NormalizedUnitDto> newUnits,
        IReadOnlyCollection<NormalizedUnitDto> oldUnits)
    {
        Dictionary<UnitToken, int> newUnitCounts = CreateUnitTokenCounts(newUnits);
        Dictionary<UnitToken, int> oldUnitCounts = CreateUnitTokenCounts(oldUnits);

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

    private static Dictionary<UnitToken, int> CreateUnitTokenCounts(IReadOnlyCollection<NormalizedUnitDto> units)
    {
        Dictionary<UnitToken, int> counts = [];
        foreach (NormalizedUnitDto unit in units)
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

    private static string DescribePlayer(NormalizedReplayPlayerDto player)
        => $"{player.Name} team={player.TeamId} pos={player.GamePos} toon={FormatToon(player.ToonId)}";

    private static string DescribeSpawn(NormalizedSpawnDto spawn)
        => $"income={spawn.Income} army={spawn.ArmyValue} kills={spawn.KilledValue} units={GetUnitTotal(spawn.Units)}";

    private static string FormatToon(NormalizedToonId toonId)
        => $"{toonId.Region}:{toonId.Realm}:{toonId.Id}";

    private static string FormatBreakpoint(int value)
        => value switch
        {
            0 => "None",
            1 => "Min5",
            2 => "Min10",
            3 => "Min15",
            4 => "All",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    private static int GetUnitTotal(IReadOnlyCollection<NormalizedUnitDto> units)
        => units.Sum(static unit => unit.Count);

    private static string CreateUnitHash(IReadOnlyCollection<NormalizedUnitDto> units)
    {
        StringBuilder builder = new();
        foreach (NormalizedUnitDto unit in units.OrderBy(static unit => unit.Name, StringComparer.Ordinal))
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
        CompareWorkerResult newResult,
        CompareWorkerResult oldResult,
        IReadOnlyCollection<ReplayComparison> comparisons,
        IReadOnlyCollection<ReplayError> errors)
    {
        Console.WriteLine("Old parser comparison sample");
        Console.WriteLine("Input: {0}", inputPath);
        Console.WriteLine("Pattern: {0}", ReplaySearchPattern);
        Console.WriteLine("Max size: {0:N0} bytes", maxReplaySizeBytes);
        Console.WriteLine("Decode threads: {0:N0}", Math.Min(DecodeThreads, Math.Max(1, candidateCount)));
        Console.WriteLine("Parse iterations: {0:N0}", iterations);
        Console.WriteLine("New stack: {0} + s2protocol.NET {1}", newResult.ParserName, newResult.S2ProtocolVersion);
        Console.WriteLine("Old stack: {0} + s2protocol.NET {1}", oldResult.ParserName, oldResult.S2ProtocolVersion);
        Console.WriteLine();
        Console.WriteLine("Candidates: {0:N0}", candidateCount);
        Console.WriteLine("New decoded/parsed: {0:N0}/{1:N0}", newResult.Replays.Count + CountErrors(newResult, "DecodeError", false), newResult.Replays.Count);
        Console.WriteLine("Old decoded/parsed: {0:N0}/{1:N0}", oldResult.Replays.Count + CountErrors(oldResult, "DecodeError", false), oldResult.Replays.Count);
        Console.WriteLine("Compared: {0:N0}", comparisons.Count);
        Console.WriteLine("Errors: {0:N0}", errors.Count);
        Console.WriteLine("Roster-diff replays: {0:N0}", comparisons.Count(static comparison => comparison.RosterDifferences.Count > 0));
        Console.WriteLine("Spawn-diff replays: {0:N0}", comparisons.Count(static comparison => comparison.SpawnDifferences.Count > 0));
        Console.WriteLine("Roster differences: {0:N0}", comparisons.Sum(static comparison => comparison.RosterDifferences.Count));
        Console.WriteLine("Spawn differences: {0:N0}", comparisons.Sum(static comparison => comparison.SpawnDifferences.Count));
        Console.WriteLine();
        PrintTimingComparison("Decode time", newResult.DecodeElapsedTicks, oldResult.DecodeElapsedTicks, Math.Max(1, candidateCount));
        PrintTimingComparison("Parse time (decode excluded)", newResult.ParseElapsedTicks, oldResult.ParseElapsedTicks, Math.Max(1, comparisons.Count * iterations));
        PrintTimingComparison("Decode + parse one-pass time", newResult.OnePassElapsedTicks, oldResult.OnePassElapsedTicks, Math.Max(1, candidateCount));
        Console.WriteLine();
    }

    private static int CountErrors(CompareWorkerResult result, string errorType, bool equal)
        => result.Errors.Count(error => string.Equals(error.ErrorType, errorType, StringComparison.Ordinal) == equal);

    private static void PrintTimingComparison(string title, long newTicks, long oldTicks, int averageDivisor)
    {
        TimeSpan newTotal = TimeSpan.FromTicks(newTicks);
        TimeSpan oldTotal = TimeSpan.FromTicks(oldTicks);
        double newAverageMs = newTotal.TotalMilliseconds / averageDivisor;
        double oldAverageMs = oldTotal.TotalMilliseconds / averageDivisor;
        double savedMs = oldTotal.TotalMilliseconds - newTotal.TotalMilliseconds;
        double savedPercent = oldTotal.TotalMilliseconds == 0D ? 0D : savedMs / oldTotal.TotalMilliseconds * 100D;
        double speedup = newTotal.TotalMilliseconds == 0D ? 0D : oldTotal.TotalMilliseconds / newTotal.TotalMilliseconds;

        Console.WriteLine(title + ":");
        Console.WriteLine("  New total: {0:N2} ms", newTotal.TotalMilliseconds);
        Console.WriteLine("  Old total: {0:N2} ms", oldTotal.TotalMilliseconds);
        Console.WriteLine("  New avg: {0:N2} ms", newAverageMs);
        Console.WriteLine("  Old avg: {0:N2} ms", oldAverageMs);
        Console.WriteLine("  Saved: {0:+0.00;-0.00;0.00} ms ({1:+0.00;-0.00;0.00}%)", savedMs, savedPercent);
        Console.WriteLine("  Speedup: {0:N2}x", speedup);
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

    private static ReplayError PrefixError(CompareWorkerResult result, ReplayError error)
        => new(error.Path, $"{result.StackName}:{error.ErrorType}", error.Message);

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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ReplayComparison(
        string ReplayPath,
        List<Difference> RosterDifferences,
        List<Difference> SpawnDifferences)
    {
        public bool HasDifferences => RosterDifferences.Count > 0 || SpawnDifferences.Count > 0;
    }

    private sealed record PlayerMatch(
        int? NewIndex,
        int? OldIndex,
        string MatchKind,
        ToonKey ToonKey,
        NormalizedReplayPlayerDto? NewPlayer,
        NormalizedReplayPlayerDto? OldPlayer)
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
        public static ToonKey FromPlayer(NormalizedReplayPlayerDto player)
            => new(player.ToonId.Region, player.ToonId.Realm, player.ToonId.Id);

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
}
