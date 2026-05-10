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

        MetadataPlayer[] metadataPlayers = CopyToArray(replay.Metadata?.Players);
        Dictionary<int, MetadataPlayer> metadataPlayersById = new(metadataPlayers.Length);
        foreach (MetadataPlayer metadataPlayer in metadataPlayers)
        {
            metadataPlayersById.TryAdd(metadataPlayer.PlayerID, metadataPlayer);
        }

        DetailsPlayer[] detailsPlayers = CopyToArray(replay.Details.Players);
        DirectStrikePlayerContext[] playerContexts = new DirectStrikePlayerContext[detailsPlayers.Length];
        DirectStrikePlayer[] players = new DirectStrikePlayer[detailsPlayers.Length];
        for (int i = 0; i < detailsPlayers.Length; i++)
        {
            MetadataPlayer? metadataPlayer = GetMetadataPlayer(metadataPlayers, metadataPlayersById, i);
            DirectStrikePlayer player = ParseDetailsPlayer(detailsPlayers[i], metadataPlayer);
            playerContexts[i] = new(player, i, metadataPlayer?.PlayerID);
            players[i] = player;
        }

        DirectStrikeReplay directStrikeReplay = new()
        {
            BaseBuild = replay.Metadata?.BaseBuild ?? string.Empty,
            Duration = replay.Metadata is null ? TimeSpan.Zero : TimeSpan.FromSeconds(replay.Metadata.Duration),
            GameMode = GetGameMode(GetGameModeUpgradeNames(replay), detailsPlayers.Length),
            GameTime = replay.Details.DateTimeUTC,
            Observers = ParseObservers(replay),
            TE = replay.Details.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase),
            Players = Array.AsReadOnly(players),
        };

        SetTrackerData(replay, playerContexts, directStrikeReplay);

        return directStrikeReplay;
    }

    private static T[] CopyToArray<T>(ICollection<T>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        T[] array = new T[values.Count];
        values.CopyTo(array, 0);
        return array;
    }

    private sealed record DirectStrikePlayerContext(DirectStrikePlayer Player, int DetailsIndex, int? MetadataPlayerId)
    {
        public List<DirectStrikePlayerRefinery> Refineries { get; } = [];
    }

    private sealed class DirectStrikePlayerRefinery
    {
        public int UnitTagIndex { get; set; }
        public int UnitTagRecycle { get; set; }
        public int Gameloop { get; set; }
        public bool Taken { get; set; }
    }
}
