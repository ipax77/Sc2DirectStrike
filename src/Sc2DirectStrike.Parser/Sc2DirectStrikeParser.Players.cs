using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
{
    private static IEnumerable<DirectStrikeObserver> ParseObservers(Sc2Replay replay)
    {
        UserInitialData[] userInitialData = [.. replay.Initdata?.UserInitialData ?? []];

        foreach (Slot slot in replay.Initdata?.LobbyState?.Slots ?? [])
        {
            if (slot.Observe != 1 || slot.UserId is not { } userId || userId < 0 || userId >= userInitialData.Length)
            {
                continue;
            }

            UserInitialData user = userInitialData[userId];
            if (string.IsNullOrWhiteSpace(user.Name) || !TryParseToonHandle(slot.ToonHandle, out int region, out int realm, out int id))
            {
                continue;
            }

            yield return new()
            {
                Clan = string.IsNullOrWhiteSpace(user.ClanTag) ? null : user.ClanTag,
                Id = id,
                Name = user.Name,
                Realm = realm,
                Region = region,
                SlotId = slot.WorkingSetSlotId,
            };
        }
    }

    private static DirectStrikePlayer ParseDetailsPlayer(DetailsPlayer player, MetadataPlayer? metadataPlayer)
    {
        Commander commander = Commander.None;
        if (Enum.TryParse(player.Race, out Commander race))
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

    private static void SetTrackerCommanders(Sc2Replay replay, DirectStrikePlayerContext[] playerContexts)
    {
        Dictionary<int, DirectStrikePlayer> playersByControlPlayerId = GetPlayersByControlPlayerId(replay, playerContexts);
        HashSet<int> mappedControlPlayerIds = [];

        foreach (SUnitBornEvent bornEvent in replay.TrackerEvents?.SUnitBornEvents ?? [])
        {
            if (bornEvent.Gameloop > 1440
                || !playersByControlPlayerId.TryGetValue(bornEvent.ControlPlayerId, out DirectStrikePlayer? player)
                || !TryParseWorkerCommander(bornEvent.UnitTypeName, out Commander commander))
            {
                continue;
            }

            if (mappedControlPlayerIds.Add(bornEvent.ControlPlayerId))
            {
                player.Commander = commander;
            }
        }
    }

    private static Dictionary<int, DirectStrikePlayer> GetPlayersByControlPlayerId(Sc2Replay replay, DirectStrikePlayerContext[] playerContexts)
    {
        Dictionary<int, DirectStrikePlayer> playersByControlPlayerId = [];
        Slot[] slots = [.. replay.Initdata?.LobbyState?.Slots ?? []];

        Dictionary<(int Region, int Realm, int Id), DirectStrikePlayerContext> playersByToon = [];
        Dictionary<int, DirectStrikePlayerContext> playersByMetadataPlayerId = [];
        for (int i = 0; i < playerContexts.Length; i++)
        {
            DirectStrikePlayerContext context = playerContexts[i];
            playersByToon.TryAdd((context.Player.Region, context.Player.Realm, context.Player.Id), context);

            if (context.MetadataPlayerId is { } metadataPlayerId)
            {
                playersByMetadataPlayerId.TryAdd(metadataPlayerId, context);
            }
        }

        foreach (SPlayerSetupEvent setupEvent in replay.TrackerEvents?.SPlayerSetupEvents ?? [])
        {
            if ((TryGetPlayerContextByUserId(setupEvent.UserId, slots, playersByToon, out DirectStrikePlayerContext? context)
                    || TryGetPlayerContextBySlotId(setupEvent.SlotId, playerContexts, out context)
                    || TryGetPlayerContextByPlayerId(setupEvent.PlayerId, playerContexts, playersByMetadataPlayerId, out context))
                && context is not null)
            {
                playersByControlPlayerId.TryAdd(setupEvent.PlayerId, context.Player);
            }
        }

        return playersByControlPlayerId;
    }

    private static bool TryGetPlayerContextByUserId(int? userId, Slot[] slots, Dictionary<(int Region, int Realm, int Id), DirectStrikePlayerContext> playersByToon, out DirectStrikePlayerContext? context)
    {
        context = null;
        if (userId is null)
        {
            return false;
        }

        foreach (Slot slot in slots)
        {
            if (slot.UserId == userId
                && TryParseToonHandle(slot.ToonHandle, out int region, out int realm, out int id)
                && playersByToon.TryGetValue((region, realm, id), out context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPlayerContextBySlotId(int slotId, DirectStrikePlayerContext[] playerContexts, out DirectStrikePlayerContext? context)
    {
        if (slotId >= 0 && slotId < playerContexts.Length)
        {
            context = playerContexts[slotId];
            return true;
        }

        context = null;
        return false;
    }

    private static bool TryGetPlayerContextByPlayerId(int playerId, DirectStrikePlayerContext[] playerContexts, Dictionary<int, DirectStrikePlayerContext> playersByMetadataPlayerId, out DirectStrikePlayerContext? context)
    {
        if (playersByMetadataPlayerId.TryGetValue(playerId, out context))
        {
            return true;
        }

        int detailsIndex = playerId - 1;
        if (detailsIndex >= 0 && detailsIndex < playerContexts.Length)
        {
            context = playerContexts[detailsIndex];
            return true;
        }

        context = null;
        return false;
    }

    private static bool TryParseToonHandle(string? toonHandle, out int region, out int realm, out int id)
    {
        region = 0;
        realm = 0;
        id = 0;

        string[] parts = toonHandle?.Split('-') ?? [];
        return parts.Length == 4
            && string.Equals(parts[1], "S2", StringComparison.Ordinal)
            && int.TryParse(parts[0], out region)
            && int.TryParse(parts[2], out realm)
            && int.TryParse(parts[3], out id);
    }

    private static bool TryParseWorkerCommander(string unitTypeName, out Commander commander)
    {
        const string workerPrefix = "Worker";

        commander = Commander.None;
        if (!unitTypeName.StartsWith(workerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string commanderName = unitTypeName[workerPrefix.Length..];
        commander = commanderName switch
        {
            nameof(Commander.Protoss) => Commander.Protoss,
            nameof(Commander.Terran) => Commander.Terran,
            nameof(Commander.Zerg) => Commander.Zerg,
            _ => Enum.TryParse(commanderName, out Commander parsedCommander) ? parsedCommander : Commander.None,
        };

        return commander != Commander.None;
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
