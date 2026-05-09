using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
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

        DetailsPlayer[] detailsPlayers = [.. replay.Details.Players];
        DirectStrikePlayerContext[] playerContexts = [.. detailsPlayers.Select((player, index) =>
        {
            MetadataPlayer? metadataPlayer = GetMetadataPlayer(metadataPlayers, metadataPlayersById, index);
            return new DirectStrikePlayerContext(ParseDetailsPlayer(player, metadataPlayer), index, metadataPlayer?.PlayerID);
        })];

        SetTrackerCommanders(replay, playerContexts);

        DirectStrikeReplay directStrikeReplay = new()
        {
            BaseBuild = replay.Metadata?.BaseBuild ?? string.Empty,
            Duration = replay.Metadata is null ? TimeSpan.Zero : TimeSpan.FromSeconds(replay.Metadata.Duration),
            GameMode = GetGameMode(GetGameModeUpgradeNames(replay), detailsPlayers.Length),
            GameTime = replay.Details.DateTimeUTC,
            Observers = [.. ParseObservers(replay)],
            TE = replay.Details.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase),
            Players = [.. playerContexts.Select(context => context.Player)]
        };

        SetTrackerLayout(replay, playerContexts, directStrikeReplay);

        return directStrikeReplay;
    }

    private sealed record DirectStrikePlayerContext(DirectStrikePlayer Player, int DetailsIndex, int? MetadataPlayerId);
}
