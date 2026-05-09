namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikeSpawnUnit
{
    public int UnitIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Gameloop { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public TimeSpan Time { get; set; }
    public int? DiedX { get; set; }
    public int? DiedY { get; set; }
    public TimeSpan? DiedTime { get; set; }
}
