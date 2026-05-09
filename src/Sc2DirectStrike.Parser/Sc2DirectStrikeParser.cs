using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static class Sc2DirectStrikeParser
{
    public static DirectStrikeReplay Parse(Sc2Replay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(replay.Details);

        if (!replay.Details.Title.StartsWith("Direct Strike", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("no direct strike replay.");
        }

        return new()
        {
            GameTime = replay.Details.DateTimeUTC,
            TE = replay.Details.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase),
            Players = [.. replay.Details.Players.Select(s => ParseDetailsPlayer(s))]
        };
    }

    private static DirectStrikePlayer ParseDetailsPlayer(DetailsPlayer player)
    {
        Commander commander = Commander.None;
        if (Enum.TryParse<Commander>(player.Race, out Commander race))
        {
            commander = race;
        }

        return new()
        {
            Name = player.Name,
            Clan = player.ClanName,
            Commander = commander,
            Id = player.Toon.Id,
            Region = player.Toon.Region,
            Realm = player.Toon.Realm,
            SlotId = player.WorkingSetSlotId,
        };
    }
}

