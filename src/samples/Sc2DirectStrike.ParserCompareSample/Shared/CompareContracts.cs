namespace Sc2DirectStrike.ParserCompareSample.Shared;

internal sealed class CompareRequest
{
    public string[] ReplayPaths { get; set; } = [];

    public int DecodeThreads { get; set; }

    public int ParseIterations { get; set; }

    public CompareDecoderOptions DecoderOptions { get; set; } = new();
}

internal sealed class CompareDecoderOptions
{
    public bool Details { get; set; }

    public bool Initdata { get; set; }

    public bool Metadata { get; set; }

    public bool GameEvents { get; set; }

    public bool MessageEvents { get; set; }

    public bool TrackerEvents { get; set; }

    public bool AttributeEvents { get; set; }
}

internal sealed class CompareWorkerResult
{
    public string StackName { get; set; } = string.Empty;

    public string ParserName { get; set; } = string.Empty;

    public string S2ProtocolVersion { get; set; } = string.Empty;

    public long DecodeElapsedTicks { get; set; }

    public long ParseElapsedTicks { get; set; }

    public long OnePassElapsedTicks { get; set; }

    public int ParseIterations { get; set; }

    public List<NormalizedReplayResult> Replays { get; set; } = [];

    public List<ReplayError> Errors { get; set; } = [];
}

internal sealed class NormalizedReplayResult
{
    public string ReplayPath { get; set; } = string.Empty;

    public NormalizedReplayDto Replay { get; set; } = new();
}

internal sealed class NormalizedReplayDto
{
    public List<NormalizedReplayPlayerDto> Players { get; set; } = [];
}

internal sealed class NormalizedReplayPlayerDto
{
    public string Name { get; set; } = string.Empty;

    public int TeamId { get; set; }

    public int GamePos { get; set; }

    public NormalizedToonId ToonId { get; set; } = new();

    public List<NormalizedSpawnDto> Spawns { get; set; } = [];
}

internal sealed class NormalizedToonId
{
    public int Region { get; set; }

    public int Realm { get; set; }

    public int Id { get; set; }
}

internal sealed class NormalizedSpawnDto
{
    public int Breakpoint { get; set; }

    public int Income { get; set; }

    public int GasCount { get; set; }

    public int ArmyValue { get; set; }

    public int KilledValue { get; set; }

    public int LostValue { get; set; }

    public int UpgradeSpent { get; set; }

    public List<NormalizedUnitDto> Units { get; set; } = [];
}

internal sealed class NormalizedUnitDto
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<int> Positions { get; set; } = [];
}

internal sealed record ReplayError(string Path, string ErrorType, string Message);
