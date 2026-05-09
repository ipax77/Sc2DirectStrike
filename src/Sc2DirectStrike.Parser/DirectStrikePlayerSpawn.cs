using System.Collections.ObjectModel;

namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikePlayerSpawn
{
    public int Gameloop { get; set; }
    public TimeSpan Time { get; set; }
    public ReadOnlyCollection<DirectStrikeSpawnUnit> Units { get; set; } = [];
}
