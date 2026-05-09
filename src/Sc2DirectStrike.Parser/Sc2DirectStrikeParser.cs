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

        MetadataPlayer[] metadataPlayers = [.. replay.Metadata?.Players ?? []];
        Dictionary<int, MetadataPlayer> metadataPlayersById = [];
        foreach (MetadataPlayer metadataPlayer in metadataPlayers)
        {
            metadataPlayersById.TryAdd(metadataPlayer.PlayerID, metadataPlayer);
        }

        return new()
        {
            BaseBuild = replay.Metadata?.BaseBuild ?? string.Empty,
            Duration = replay.Metadata is null ? TimeSpan.Zero : TimeSpan.FromSeconds(replay.Metadata.Duration),
            GameTime = replay.Details.DateTimeUTC,
            TE = replay.Details.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase),
            Players = [.. replay.Details.Players.Select((player, index) => ParseDetailsPlayer(player, GetMetadataPlayer(metadataPlayers, metadataPlayersById, index)))]
        };
    }

    private static DirectStrikePlayer ParseDetailsPlayer(DetailsPlayer player, MetadataPlayer? metadataPlayer)
    {
        Commander commander = Commander.None;
        if (Enum.TryParse<Commander>(player.Race, out Commander race))
        {
            commander = race;
        }

        PlayerResult result = metadataPlayer is null ? PlayerResult.None : ParsePlayerResult(metadataPlayer.Result);
        if (result == PlayerResult.None)
        {
            result = ParsePlayerResult(player.Result);
        }

        return new()
        {
            APM = metadataPlayer?.APM ?? 0,
            Name = player.Name,
            Clan = player.ClanName,
            Commander = commander,
            Id = player.Toon.Id,
            Result = result,
            Region = player.Toon.Region,
            Realm = player.Toon.Realm,
            SelectedRace = ParseRace(metadataPlayer?.SelectedRace),
            SlotId = player.WorkingSetSlotId,
        };
    }

    private static MetadataPlayer? GetMetadataPlayer(MetadataPlayer[] metadataPlayers, Dictionary<int, MetadataPlayer> metadataPlayersById, int detailsPlayerIndex)
    {
        if (metadataPlayersById.TryGetValue(detailsPlayerIndex + 1, out MetadataPlayer? playerById))
        {
            return playerById;
        }

        return detailsPlayerIndex < metadataPlayers.Length
            ? metadataPlayers[detailsPlayerIndex]
            : null;
    }

    private static Race ParseRace(string? race)
    {
        return race?.Trim() switch
        {
            { } value when value.Equals("Rand", StringComparison.OrdinalIgnoreCase) => Race.Random,
            { } value when value.Equals("Random", StringComparison.OrdinalIgnoreCase) => Race.Random,
            { } value when value.Equals("Terr", StringComparison.OrdinalIgnoreCase) => Race.Terran,
            { } value when value.Equals("Terran", StringComparison.OrdinalIgnoreCase) => Race.Terran,
            { } value when value.Equals("Prot", StringComparison.OrdinalIgnoreCase) => Race.Protoss,
            { } value when value.Equals("Protoss", StringComparison.OrdinalIgnoreCase) => Race.Protoss,
            { } value when value.Equals("Zerg", StringComparison.OrdinalIgnoreCase) => Race.Zerg,
            _ => Race.None,
        };
    }

    private static PlayerResult ParsePlayerResult(string? result)
    {
        return result?.Trim() switch
        {
            { } value when value.Equals("Win", StringComparison.OrdinalIgnoreCase) => PlayerResult.Win,
            { } value when value.Equals("Loss", StringComparison.OrdinalIgnoreCase) => PlayerResult.Loss,
            { } value when value.Equals("Undecided", StringComparison.OrdinalIgnoreCase) => PlayerResult.Undecided,
            _ => PlayerResult.None,
        };
    }

    private static PlayerResult ParsePlayerResult(int result)
    {
        return result switch
        {
            0 => PlayerResult.Undecided,
            1 => PlayerResult.Win,
            2 => PlayerResult.Loss,
            _ => PlayerResult.None,
        };
    }
}

