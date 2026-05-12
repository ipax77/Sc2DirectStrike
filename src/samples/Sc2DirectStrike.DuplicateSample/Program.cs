using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using s2protocol.NET;
using s2protocol.NET.Models;
using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.DuplicateSample;

public static class Program
{
    private const int DefaultMaxReplaySizeKb = 300;
    private const int DecodeThreads = 8;
    private const int MaxRosterGroupsToPrint = 10;
    private const string ReplaySearchPattern = "Direct Strike TE*.SC2Replay";
    private static readonly TimeSpan MinimumCheckedDuration = TimeSpan.FromMinutes(5);

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
        string replayDirectory = args.Length > 0 ? args[0] : DefaultReplayDirectory;
        if (!TryGetMaxReplaySizeBytes(args, out long maxReplaySizeBytes))
        {
            Console.Error.WriteLine("Usage: Sc2DirectStrike.DuplicateSample [replay-directory] [max-size-kb]");
            return 2;
        }

        if (!Directory.Exists(replayDirectory))
        {
            Console.Error.WriteLine($"Replay directory does not exist: {replayDirectory}");
            return 2;
        }

        FileInfo[] candidates = GetCandidateReplayFiles(replayDirectory, maxReplaySizeBytes);
        if (candidates.Length == 0)
        {
            Console.Error.WriteLine(
                "No replay candidates found in '{0}' matching '{1}' with size <= {2:N0} bytes.",
                replayDirectory,
                ReplaySearchPattern,
                maxReplaySizeBytes);
            return 2;
        }

        using ReplayDecoder replayDecoder = new();
        ReplayDecoderOptions replayDecoderOptions = CreateDecoderOptions();
        List<CheckedReplay> checkedReplays = [];
        List<ReplayError> errors = [];
        int parsedCount = 0;
        int skippedShortCount = 0;
        int skippedNonTeCount = 0;
        int emptyHashCount = 0;
        string[] candidatePaths = [.. candidates.Select(static candidate => candidate.FullName)];

        IAsyncEnumerable<DecodeParallelResult> decodeResults = replayDecoder.DecodeParallelWithErrorReport(
            candidatePaths,
            DecodeThreads,
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
                ReplayDto dto = Sc2DirectStrikeParser.ParseDto(replay);
                parsedCount++;

                if (!dto.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase))
                {
                    skippedNonTeCount++;
                    continue;
                }

                if (dto.Duration <= MinimumCheckedDuration)
                {
                    skippedShortCount++;
                    continue;
                }

                if (string.IsNullOrEmpty(dto.CompatHash))
                {
                    emptyHashCount++;
                    continue;
                }

                checkedReplays.Add(new(
                    replayPath,
                    dto.Title,
                    dto.Gametime,
                    dto.Duration,
                    CreateSha256HexHash(dto.CompatHash),
                    CreatePlayerRosterHash(dto)));
            }
            catch (Exception ex)
            {
                errors.Add(new(replayPath, ex.GetType().Name, ex.Message));
            }
        }

        CheckedReplay[][] duplicateGroups = [..
            checkedReplays
                .GroupBy(static replay => replay.Sha256CompatHash, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.OrderBy(static replay => replay.Path, StringComparer.OrdinalIgnoreCase).ToArray())
                .OrderBy(static group => group[0].Sha256CompatHash, StringComparer.Ordinal)];
        CheckedReplay[][] rosterGroups = [..
            checkedReplays
                .GroupBy(static replay => replay.PlayerRosterHash, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.OrderBy(static replay => replay.GameTime).ThenBy(static replay => replay.Path, StringComparer.OrdinalIgnoreCase).ToArray())
                .OrderByDescending(static group => group.Length)
                .ThenBy(static group => group[0].PlayerRosterHash, StringComparer.Ordinal)];

        PrintSummary(
            replayDirectory,
            maxReplaySizeBytes,
            candidates.Length,
            parsedCount,
            checkedReplays.Count,
            skippedShortCount,
            skippedNonTeCount,
            emptyHashCount,
            errors.Count,
            rosterGroups.Length,
            rosterGroups.Sum(static group => group.Length));

        PrintDuplicateGroups(duplicateGroups);
        PrintRosterGroups(rosterGroups);
        PrintErrors(errors);

        if (errors.Count > 0)
        {
            return 3;
        }

        if (duplicateGroups.Length > 0)
        {
            return 1;
        }

        Console.WriteLine("PASS: no duplicate SHA256 compat keys found.");
        return 0;
    }

    private static bool TryGetMaxReplaySizeBytes(string[] args, out long maxReplaySizeBytes)
    {
        maxReplaySizeBytes = DefaultMaxReplaySizeKb * 1024L;
        if (args.Length <= 1)
        {
            return true;
        }

        if (!long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out long maxReplaySizeKb)
            || maxReplaySizeKb < 0)
        {
            return false;
        }

        maxReplaySizeBytes = maxReplaySizeKb * 1024L;
        return true;
    }

    private static FileInfo[] GetCandidateReplayFiles(string replayDirectory, long maxReplaySizeBytes)
    {
        return [..
            Directory
                .EnumerateFiles(replayDirectory, ReplaySearchPattern, SearchOption.TopDirectoryOnly)
                .Select(static path => new FileInfo(path))
                .Where(file => file.Length <= maxReplaySizeBytes)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)];
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

    private static string CreateSha256HexHash(string compatHash)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(compatHash));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string CreatePlayerRosterHash(ReplayDto dto)
    {
        StringBuilder builder = new();
        AppendRosterString(builder, "ds-player-roster-v1");
        AppendRosterInt(builder, dto.Players.Count);

        foreach (ReplayPlayerDto player in dto.Players
            .OrderBy(static player => player.Player.ToonId.Region)
            .ThenBy(static player => player.Player.ToonId.Realm)
            .ThenBy(static player => player.Player.ToonId.Id)
            .ThenBy(static player => player.Player.PlayerId)
            .ThenBy(static player => player.Name, StringComparer.Ordinal)
            .ThenBy(static player => player.Clan ?? string.Empty, StringComparer.Ordinal))
        {
            AppendRosterInt(builder, player.Player.ToonId.Region);
            AppendRosterInt(builder, player.Player.ToonId.Realm);
            AppendRosterInt(builder, player.Player.ToonId.Id);
            AppendRosterInt(builder, player.Player.PlayerId);
            AppendRosterString(builder, player.Name);
            AppendRosterString(builder, player.Clan ?? string.Empty);
        }

        return CreateSha256HexHash(builder.ToString());
    }

    private static void AppendRosterInt(StringBuilder builder, int value)
    {
        builder.Append('i');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
    }

    private static void AppendRosterString(StringBuilder builder, string value)
    {
        builder.Append('s');
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static void PrintSummary(
        string replayDirectory,
        long maxReplaySizeBytes,
        int candidateCount,
        int parsedCount,
        int checkedCount,
        int skippedShortCount,
        int skippedNonTeCount,
        int emptyHashCount,
        int errorCount,
        int repeatedRosterGroupCount,
        int repeatedRosterReplayCount)
    {
        Console.WriteLine("TE duplicate false-positive sample");
        Console.WriteLine("Replay directory: {0}", replayDirectory);
        Console.WriteLine("Pattern: {0}", ReplaySearchPattern);
        Console.WriteLine("Max size: {0:N0} bytes", maxReplaySizeBytes);
        Console.WriteLine("Decode threads: {0:N0}", DecodeThreads);
        Console.WriteLine();
        Console.WriteLine("Candidates: {0:N0}", candidateCount);
        Console.WriteLine("Parsed: {0:N0}", parsedCount);
        Console.WriteLine("Checked: {0:N0}", checkedCount);
        Console.WriteLine("Skipped short: {0:N0}", skippedShortCount);
        Console.WriteLine("Skipped non-TE: {0:N0}", skippedNonTeCount);
        Console.WriteLine("Empty hash: {0:N0}", emptyHashCount);
        Console.WriteLine("Errors: {0:N0}", errorCount);
        Console.WriteLine("Repeated player roster groups: {0:N0}", repeatedRosterGroupCount);
        Console.WriteLine("Replays with repeated player roster: {0:N0}", repeatedRosterReplayCount);
        Console.WriteLine();
    }

    private static void PrintDuplicateGroups(IReadOnlyCollection<CheckedReplay[]> duplicateGroups)
    {
        if (duplicateGroups.Count == 0)
        {
            return;
        }

        Console.WriteLine("Duplicate SHA256 compat keys:");
        foreach (CheckedReplay[] group in duplicateGroups)
        {
            Console.WriteLine("Hash: {0}", group[0].Sha256CompatHash);
            foreach (CheckedReplay replay in group)
            {
                Console.WriteLine(
                    "  {0:yyyy-MM-dd HH:mm:ss} | {1:c} | {2} | {3}",
                    replay.GameTime,
                    replay.Duration,
                    replay.Title,
                    replay.Path);
            }

            Console.WriteLine();
        }
    }

    private static void PrintRosterGroups(IReadOnlyCollection<CheckedReplay[]> rosterGroups)
    {
        if (rosterGroups.Count == 0)
        {
            return;
        }

        Console.WriteLine("Repeated player roster groups:");
        int printedGroupCount = 0;
        foreach (CheckedReplay[] group in rosterGroups.Take(MaxRosterGroupsToPrint))
        {
            printedGroupCount++;
            Console.WriteLine("Roster hash: {0} ({1:N0} replays)", group[0].PlayerRosterHash, group.Length);
            foreach (CheckedReplay replay in group)
            {
                Console.WriteLine(
                    "  {0:yyyy-MM-dd HH:mm:ss} | {1:c} | {2}",
                    replay.GameTime,
                    replay.Duration,
                    replay.Path);
            }

            Console.WriteLine();
        }

        int omittedGroupCount = rosterGroups.Count - printedGroupCount;
        if (omittedGroupCount > 0)
        {
            Console.WriteLine("Omitted {0:N0} smaller repeated roster groups.", omittedGroupCount);
            Console.WriteLine();
        }
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

    private sealed record CheckedReplay(
        string Path,
        string Title,
        DateTime GameTime,
        TimeSpan Duration,
        string Sha256CompatHash,
        string PlayerRosterHash);

    private sealed record ReplayError(string Path, string ErrorType, string Message);
}
