namespace Sc2DirectStrike.Parser;

public sealed class DirectStrikePlayer
{
    public int TeamId { get; set; }
    public int SlotId { get; set; }
    public Commander Commander { get; set; }
    public PlayerResult Result { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Clan { get; set; }
    public int Id { get; set; }
    public int Region { get; set; }
    public int Realm { get; set; }
}
