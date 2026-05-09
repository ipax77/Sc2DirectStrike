using System.Collections.ObjectModel;

namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikeReplay
{
    public GameMode GameMode { get; set; }
    public DateTime GameTime { get; set; }
    public bool TE { get; set; }
    public int WinnerTeam { get; set; }
    public ReadOnlyCollection<DirectStrikePlayer> Players { get; set; } = [];
}
