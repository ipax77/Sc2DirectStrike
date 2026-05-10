
namespace Sc2DirectStrike.Parser;

public sealed class ReplayDto
{
    public required string FileName { get; init; }
    public required string CompatHash { get; init; }
    public required string Title { get; init; }
    public required string Version { get; init; }
    public GameMode GameMode { get; init; }
    public int RegionId { get; init; }
    public DateTime Gametime { get; init; }
    public int BaseBuild { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan Cannon { get; init; }
    public TimeSpan Bunker { get; init; }
    public int WinnerTeam { get; init; }
    public int FirstTeamCrossedMiddle { get; init; }
    public IReadOnlyList<TimeSpan> MiddleChanges { get; init; } = [];
    public IReadOnlyCollection<ReplayPlayerDto> Players { get; init; } = [];
}

public sealed class ReplayPlayerDto
{
    public required string CompatHash { get; init; }
    public required string Name { get; init; }
    public string? Clan { get; init; }
    public Commander Race { get; init; }
    public Commander SelectedRace { get; init; }
    public int TeamId { get; init; }
    public int GamePos { get; init; }
    public PlayerResult Result { get; init; }
    public TimeSpan Duration { get; init; }
    public int Apm { get; init; }
    public int Messages { get; init; }
    public int Pings { get; init; }
    public bool IsMvp { get; init; }
    public IReadOnlyCollection<SpawnDto> Spawns { get; init; } = [];
    public IReadOnlyCollection<UpgradeDto> Upgrades { get; init; } = [];
    public IReadOnlyCollection<TimeSpan> TierUpgrades { get; init; } = [];
    public IReadOnlyCollection<TimeSpan> Refineries { get; init; } = [];
    public required PlayerDto Player { get; init; }
}

public sealed class PlayerDto
{
    public int PlayerId { get; init; }
    public required string Name { get; init; }
    public required ToonIdDto ToonId { get; init; }
}

public sealed record ToonIdDto
{
    public int Region { get; init; }
    public int Realm { get; init; }
    public int Id { get; init; }
}

public sealed class SpawnDto
{
    public Breakpoint Breakpoint { get; init; }
    public int Income { get; init; }
    public int GasCount { get; init; }
    public int ArmyValue { get; init; }
    public int KilledValue { get; init; }
    public int LostValue { get; init; }
    public int UpgradeSpent { get; init; }
    public IReadOnlyCollection<UnitDto> Units { get; init; } = [];
}

public sealed class UnitDto
{
    public required string Name { get; init; }
    public int Count { get; init; }
    /// <summary>
    /// List of all x|y coords
    /// </summary>
    public IReadOnlyList<int> Positions { get; init; } = [];
}

public sealed class UpgradeDto
{
    public required string Name { get; init; }
    public TimeSpan Time { get; init; }
}
