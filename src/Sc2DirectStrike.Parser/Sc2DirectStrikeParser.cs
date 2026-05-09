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

        DetailsPlayer[] detailsPlayers = [.. replay.Details.Players];

        return new()
        {
            BaseBuild = replay.Metadata?.BaseBuild ?? string.Empty,
            Duration = replay.Metadata is null ? TimeSpan.Zero : TimeSpan.FromSeconds(replay.Metadata.Duration),
            GameMode = GetGameMode(GetGameModeUpgradeNames(replay), detailsPlayers.Length),
            GameTime = replay.Details.DateTimeUTC,
            Observers = [.. ParseObservers(replay)],
            TE = replay.Details.Title.EndsWith("TE", StringComparison.OrdinalIgnoreCase),
            Players = [.. detailsPlayers.Select((player, index) => ParseDetailsPlayer(player, GetMetadataPlayer(metadataPlayers, metadataPlayersById, index)))]
        };
    }

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

    private static HashSet<string> GetGameModeUpgradeNames(Sc2Replay replay)
    {
        HashSet<string> modes = new(StringComparer.Ordinal);

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop != 0)
            {
                continue;
            }

            string upgradeTypeName = upgradeEvent.UpgradeTypeName;
            if (upgradeTypeName.StartsWith("GameMode", StringComparison.Ordinal)
                || upgradeTypeName.StartsWith("Mutation", StringComparison.Ordinal))
            {
                modes.Add(upgradeTypeName);
            }
        }

        return modes;
    }

    private static GameMode GetGameMode(HashSet<string> modes, int playerCount)
    {
        if (playerCount == 1)
        {
            return GameMode.Tutorial;
        }

        bool isBrawl = false;
        bool isCommanders = false;
        bool isStandard = false;

        foreach (string mode in modes)
        {
            if (mode == "GameModeBrawl")
            {
                isBrawl = true;
            }
            else if (mode == "GameModeBrawlCommanders")
            {
                return GameMode.BrawlCommanders;
            }
            else if (mode == "GameModeBrawlStandard")
            {
                return GameMode.BrawlStandard;
            }
            else if (mode == "GameModeHeroicCommanders" || mode == "GameModeCommandersHeroic")
            {
                return GameMode.CommandersHeroic;
            }
            else if (mode == "GameModeCommanders" || mode == "MutationCommanders")
            {
                isCommanders = true;
            }
            else if (mode == "GameModeStandard")
            {
                isStandard = true;
            }
            else if (mode == "GameModeGear")
            {
                return GameMode.Gear;
            }
            else if (mode == "GameModeSwitch")
            {
                return GameMode.Switch;
            }
            else if (mode == "GameModeSabotage")
            {
                return GameMode.Sabotage;
            }
        }

        if (isBrawl && isCommanders)
        {
            return GameMode.BrawlCommanders;
        }
        else if (isBrawl && isStandard)
        {
            return GameMode.BrawlStandard;
        }
        else if (isCommanders)
        {
            return GameMode.Commanders;
        }
        else if (isStandard)
        {
            return GameMode.Standard;
        }

        return GameMode.None;
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

