using System.Reflection;
using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
{
    private static readonly int[] RefineryCosts = [150, 225, 300, 375, 500];

    private static readonly BreakpointDefinition[] BreakpointDefinitions =
    [
        new(Breakpoint.Min5, 6_720),
        new(Breakpoint.Min10, 13_440),
        new(Breakpoint.Min15, 20_160),
    ];

    public static ReplayDto ParseDto(Sc2Replay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        DirectStrikeReplay directStrikeReplay = Parse(replay);
        Dictionary<DirectStrikePlayer, MessageCounts> messageCountsByPlayer = GetMessageCountsByPlayer(replay, directStrikeReplay);

        return new()
        {
            FileName = replay.FileName ?? string.Empty,
            CompatHash = string.Empty,
            Title = replay.Details?.Title ?? replay.Metadata?.Title ?? string.Empty,
            Version = GetReplayVersion(replay),
            GameMode = directStrikeReplay.GameMode,
            RegionId = directStrikeReplay.Players.Select(player => player.Region).FirstOrDefault(region => region != 0),
            Gametime = directStrikeReplay.GameTime,
            BaseBuild = ParseBaseBuild(directStrikeReplay.BaseBuild, replay),
            Duration = directStrikeReplay.Duration,
            Cannon = directStrikeReplay.CannonTime,
            Bunker = directStrikeReplay.BunkerTime,
            WinnerTeam = directStrikeReplay.WinnerTeam,
            FirstTeamCrossedMiddle = directStrikeReplay.FirstMiddleControlTeam,
            MiddleChanges = directStrikeReplay.MiddleChanges,
            Players = [.. directStrikeReplay.Players.Select(player => CreatePlayerDto(player, messageCountsByPlayer.GetValueOrDefault(player)))],
        };
    }

    private static ReplayPlayerDto CreatePlayerDto(DirectStrikePlayer player, MessageCounts messageCounts)
    {
        Dictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn = GetArmyValuesBySpawn(player);
        Dictionary<DirectStrikePlayerSpawn, int> incomesBySpawn = GetIncomesBySpawn(player);

        return new()
        {
            Name = player.Name,
            Clan = player.Clan,
            Race = player.Commander,
            SelectedRace = ToCommander(player.SelectedRace),
            TeamId = player.TeamId,
            GamePos = player.GamePos,
            Result = player.Result,
            Duration = player.Duration,
            Apm = (int)Math.Round(player.APM),
            Messages = messageCounts.Messages,
            Pings = messageCounts.Pings,
            IsMvp = false,
            Spawns = [.. GetBreakpointSpawns(player).Select(spawn => CreateSpawnDto(spawn.Breakpoint, spawn.Spawn, player, armyValuesBySpawn, incomesBySpawn))],
            Upgrades = [.. player.Upgrades
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new UpgradeDto
                {
                    Name = pair.Key,
                    Time = pair.Value,
                })],
            TierUpgrades = player.TierUpgrades,
            Refineries = player.RefineryTimes,
            Player = new()
            {
                PlayerId = player.Id,
                Name = player.Name,
                ToonId = new()
                {
                    Region = player.Region,
                    Realm = player.Realm,
                    Id = player.Id,
                },
            },
        };
    }

    private static IEnumerable<SelectedBreakpointSpawn> GetBreakpointSpawns(DirectStrikePlayer player)
    {
        DirectStrikePlayerSpawn[] statsBackedSpawns = [.. player.Spawns.Where(spawn => spawn.SummaryStats is not null)];
        if (statsBackedSpawns.Length == 0)
        {
            yield break;
        }

        foreach (BreakpointDefinition breakpoint in BreakpointDefinitions)
        {
            if (player.DurationGameloop > 0 && breakpoint.Gameloop > player.DurationGameloop)
            {
                continue;
            }

            DirectStrikePlayerSpawn? spawn = statsBackedSpawns
                .OrderBy(spawn => Math.Abs(spawn.EndGameloop - breakpoint.Gameloop))
                .ThenBy(spawn => spawn.EndGameloop)
                .FirstOrDefault();
            if (spawn is not null)
            {
                yield return new(breakpoint.Breakpoint, spawn);
            }
        }

        yield return new(Breakpoint.All, statsBackedSpawns[^1]);
    }

    private static SpawnDto CreateSpawnDto(
        Breakpoint breakpoint,
        DirectStrikePlayerSpawn spawn,
        DirectStrikePlayer player,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> incomesBySpawn)
    {
        DirectStrikePlayerStats stats = spawn.SummaryStats
            ?? throw new InvalidOperationException("Breakpoint spawns must have summary stats.");

        return new()
        {
            Breakpoint = breakpoint,
            Income = incomesBySpawn.GetValueOrDefault(spawn),
            GasCount = player.RefineryTimes.Count(refinery => refinery <= stats.Time),
            ArmyValue = armyValuesBySpawn.GetValueOrDefault(spawn),
            KilledValue = stats.MineralsKilledArmy,
            LostValue = stats.MineralsLostArmy,
            UpgradeSpent = stats.MineralsUsedCurrentTechnology,
            Units = [.. spawn.Units
                .GroupBy(unit => unit.Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new UnitDto
                {
                    Name = group.Key,
                    Count = group.Count(),
                    Positions = [.. group.SelectMany(unit => new[] { unit.X, unit.Y })],
                })],
        };
    }

    private static Dictionary<DirectStrikePlayerSpawn, int> GetIncomesBySpawn(DirectStrikePlayer player)
    {
        Dictionary<DirectStrikePlayerSpawn, int> incomesBySpawn = new(player.Spawns.Count);
        if (player.Stats.Count == 0)
        {
            return incomesBySpawn;
        }

        foreach (DirectStrikePlayerSpawn spawn in player.Spawns)
        {
            if (spawn.SummaryStats is not { } stats)
            {
                continue;
            }

            incomesBySpawn.Add(spawn, GetAccumulatedIncome(player, stats.Gameloop));
        }

        return incomesBySpawn;
    }

    private static int GetAccumulatedIncome(DirectStrikePlayer player, int targetGameloop)
    {
        var stats = player.Stats;
        if (targetGameloop <= 0 || stats.Count == 0)
        {
            return 0;
        }

        double income = 0;
        int previousGameloop = 0;
        int previousRate = stats[0].MineralsCollectionRate;

        foreach (DirectStrikePlayerStats stat in stats)
        {
            int currentGameloop = Math.Min(stat.Gameloop, targetGameloop);
            if (currentGameloop > previousGameloop)
            {
                income += GetIncomeForInterval(previousRate, currentGameloop - previousGameloop);
                previousGameloop = currentGameloop;
            }

            previousRate = stat.MineralsCollectionRate;
            if (stat.Gameloop >= targetGameloop)
            {
                return (int)income - GetRefineryCost(player, targetGameloop);
            }
        }

        if (previousGameloop < targetGameloop)
        {
            income += GetIncomeForInterval(previousRate, targetGameloop - previousGameloop);
        }

        return (int)income - GetRefineryCost(player, targetGameloop);
    }

    private static double GetIncomeForInterval(int mineralsPerMinute, int gameloopInterval)
    {
        return mineralsPerMinute * (gameloopInterval / GameLoopsPerSecond) / 60D;
    }

    private static int GetRefineryCost(DirectStrikePlayer player, int targetGameloop)
    {
        int refineryCount = player.RefineryTimes.Count(refinery => ToGameloop(refinery) < targetGameloop);
        int cost = 0;
        for (int i = 0; i < refineryCount && i < RefineryCosts.Length; i++)
        {
            cost += RefineryCosts[i];
        }

        return cost;
    }

    private static int ToGameloop(TimeSpan time)
    {
        return (int)(time.TotalSeconds * GameLoopsPerSecond);
    }

    private static Dictionary<DirectStrikePlayerSpawn, int> GetArmyValuesBySpawn(DirectStrikePlayer player)
    {
        Dictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn = new(player.Spawns.Count);
        int cumulativePreviousArmyValue = 0;

        foreach (DirectStrikePlayerSpawn spawn in player.Spawns)
        {
            if (spawn.SummaryStats is not { } stats)
            {
                continue;
            }

            int armyValue = (stats.MineralsUsedActiveForces - cumulativePreviousArmyValue + stats.MineralsLostArmy) / 2;
            armyValuesBySpawn.Add(spawn, armyValue);
            cumulativePreviousArmyValue += armyValue;
        }

        return armyValuesBySpawn;
    }

    private static Dictionary<DirectStrikePlayer, MessageCounts> GetMessageCountsByPlayer(Sc2Replay replay, DirectStrikeReplay directStrikeReplay)
    {
        Dictionary<int, DirectStrikePlayer> playersByUserId = GetPlayersByUserId(replay, directStrikeReplay);
        Dictionary<DirectStrikePlayer, MessageCounts> countsByPlayer = new(directStrikeReplay.Players.Count);

        foreach (ChatMessageEvent chatMessage in replay.ChatMessages ?? [])
        {
            if (playersByUserId.TryGetValue(chatMessage.UserId, out DirectStrikePlayer? player))
            {
                countsByPlayer[player] = countsByPlayer.GetValueOrDefault(player).AddMessage();
            }
        }

        foreach (PingMessageEvent pingMessage in replay.PingMessages ?? [])
        {
            if (playersByUserId.TryGetValue(pingMessage.UserId, out DirectStrikePlayer? player))
            {
                countsByPlayer[player] = countsByPlayer.GetValueOrDefault(player).AddPing();
            }
        }

        return countsByPlayer;
    }

    private static Dictionary<int, DirectStrikePlayer> GetPlayersByUserId(Sc2Replay replay, DirectStrikeReplay directStrikeReplay)
    {
        Dictionary<int, DirectStrikePlayer> playersByUserId = [];
        Dictionary<(int Region, int Realm, int Id), DirectStrikePlayer> playersByToon = [];
        Dictionary<int, DirectStrikePlayer> playersBySlotId = [];

        foreach (DirectStrikePlayer player in directStrikeReplay.Players)
        {
            playersByToon.TryAdd((player.Region, player.Realm, player.Id), player);
            playersBySlotId.TryAdd(player.SlotId, player);
        }

        foreach (Slot slot in replay.Initdata?.LobbyState?.Slots ?? [])
        {
            if (slot.UserId is not { } userId)
            {
                continue;
            }

            if ((TryParseToonHandle(slot.ToonHandle, out int region, out int realm, out int id)
                    && playersByToon.TryGetValue((region, realm, id), out DirectStrikePlayer? player))
                || playersBySlotId.TryGetValue(slot.WorkingSetSlotId, out player))
            {
                playersByUserId.TryAdd(userId, player);
            }
        }

        return playersByUserId;
    }

    private static Commander ToCommander(Race race)
    {
        return race switch
        {
            Race.Terran => Commander.Terran,
            Race.Protoss => Commander.Protoss,
            Race.Zerg => Commander.Zerg,
            Race.Random => Commander.Random,
            _ => Commander.None,
        };
    }

    private static string GetReplayVersion(Sc2Replay replay)
    {
        string metadataVersion = replay.Metadata?.GameVersion?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(metadataVersion))
        {
            return metadataVersion;
        }

        return GetHeaderProperty(replay, "Version")?.ToString() ?? string.Empty;
    }

    private static int ParseBaseBuild(string baseBuild, Sc2Replay replay)
    {
        if (int.TryParse(baseBuild, out int parsedBaseBuild))
        {
            return parsedBaseBuild;
        }

        return GetHeaderProperty(replay, "BaseBuild") is int headerBaseBuild
            ? headerBaseBuild
            : 0;
    }

    private static object? GetHeaderProperty(Sc2Replay replay, string propertyName)
    {
        object? header = replay.Header;
        return header?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(header);
    }

    private readonly record struct BreakpointDefinition(Breakpoint Breakpoint, int Gameloop);

    private readonly record struct SelectedBreakpointSpawn(Breakpoint Breakpoint, DirectStrikePlayerSpawn Spawn);

    private readonly record struct MessageCounts(int Messages, int Pings)
    {
        public MessageCounts AddMessage()
        {
            return this with { Messages = Messages + 1 };
        }

        public MessageCounts AddPing()
        {
            return this with { Pings = Pings + 1 };
        }
    }
}
