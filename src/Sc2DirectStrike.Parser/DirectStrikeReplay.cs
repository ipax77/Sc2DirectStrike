using System.Collections.ObjectModel;

namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikeReplay
{
    public string BaseBuild { get; set; } = string.Empty;
    public TimeSpan BunkerTime { get; set; }
    public TimeSpan CannonTime { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan GameEndTime { get; set; }
    public GameMode GameMode { get; set; }
    public DateTime GameTime { get; set; }
    public bool TE { get; set; }
    public int WinnerTeam { get; set; }
    public ReadOnlyCollection<DirectStrikeObserver> Observers { get; set; } = [];
    public ReadOnlyCollection<DirectStrikePlayer> Players { get; set; } = [];
}
