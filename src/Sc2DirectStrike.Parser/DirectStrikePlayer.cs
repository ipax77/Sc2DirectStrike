using System.Diagnostics.CodeAnalysis;

namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikePlayer
{
    public double APM { get; set; }
    public int GamePos { get; set; }
    public int TeamId { get; set; }
    public int SlotId { get; set; }
    public Commander Commander { get; set; }
    public PlayerResult Result { get; set; }
    public Race SelectedRace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Clan { get; set; }
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Refinery timings are intentionally exposed as an array by the parser API contract.")]
    public TimeSpan[] RefineryTimes { get; set; } = [];
    public int Id { get; set; }
    public int Region { get; set; }
    public int Realm { get; set; }
}
