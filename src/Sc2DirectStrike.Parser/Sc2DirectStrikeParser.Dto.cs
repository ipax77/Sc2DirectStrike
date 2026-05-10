using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
{
    private const int CompatHashGameloop = 6_720;

    private static readonly int[] RefineryCosts = [150, 225, 300, 375, 500];

    private static readonly BreakpointDefinition[] BreakpointDefinitions =
    [
        new(Breakpoint.Min5, CompatHashGameloop),
        new(Breakpoint.Min10, 13_440),
        new(Breakpoint.Min15, 20_160),
    ];

    public static ReplayDto ParseDto(Sc2Replay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        DirectStrikeReplay directStrikeReplay = Parse(replay);
        Dictionary<DirectStrikePlayer, MessageCounts> messageCountsByPlayer = GetMessageCountsByPlayer(replay, directStrikeReplay);
        int compatHashGameloop = GetCompatHashGameloop(directStrikeReplay);
        List<ReplayPlayerDto> players = new(directStrikeReplay.Players.Count);
        foreach (DirectStrikePlayer player in directStrikeReplay.Players)
        {
            players.Add(CreatePlayerDto(player, messageCountsByPlayer.GetValueOrDefault(player), compatHashGameloop));
        }

        string title = replay.Details?.Title ?? replay.Metadata?.Title ?? string.Empty;
        string version = GetReplayVersion(replay);
        int regionId = GetRegionId(directStrikeReplay);
        int baseBuild = ParseBaseBuild(directStrikeReplay.BaseBuild, replay);

        return new()
        {
            FileName = replay.FileName ?? string.Empty,
            CompatHash = CreateCompatHash(title, version, directStrikeReplay.GameMode, regionId, baseBuild, directStrikeReplay.Duration, players),
            Title = title,
            Version = version,
            GameMode = directStrikeReplay.GameMode,
            RegionId = regionId,
            Gametime = directStrikeReplay.GameTime,
            BaseBuild = baseBuild,
            Duration = directStrikeReplay.Duration,
            Cannon = directStrikeReplay.CannonTime,
            Bunker = directStrikeReplay.BunkerTime,
            WinnerTeam = directStrikeReplay.WinnerTeam,
            FirstTeamCrossedMiddle = directStrikeReplay.FirstMiddleControlTeam,
            MiddleChanges = directStrikeReplay.MiddleChanges,
            Players = players,
        };
    }

    private static int GetCompatHashGameloop(DirectStrikeReplay replay)
    {
        if (ToGameloop(replay.Duration) < CompatHashGameloop)
        {
            return 0;
        }

        int compatHashGameloop = CompatHashGameloop;
        foreach (DirectStrikePlayer player in replay.Players)
        {
            if (player.DurationGameloop > 0 && player.DurationGameloop < compatHashGameloop)
            {
                compatHashGameloop = player.DurationGameloop;
            }
        }

        return compatHashGameloop;
    }

    private static string CreateCompatHash(
        string title,
        string version,
        GameMode gameMode,
        int regionId,
        int baseBuild,
        TimeSpan duration,
        List<ReplayPlayerDto> players)
    {
        if (players.Count == 0 || ToGameloop(duration) < CompatHashGameloop)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        AppendString(builder, "ds-compat-v1");
        AppendString(builder, title);
        AppendString(builder, version);
        AppendInt(builder, (int)gameMode);
        AppendInt(builder, regionId);
        AppendInt(builder, baseBuild);
        AppendInt(builder, players.Count);

        foreach (ReplayPlayerDto player in players
            .OrderBy(static player => player.TeamId)
            .ThenBy(static player => player.GamePos)
            .ThenBy(static player => player.Player.ToonId.Region)
            .ThenBy(static player => player.Player.ToonId.Realm)
            .ThenBy(static player => player.Player.ToonId.Id)
            .ThenBy(static player => player.Player.PlayerId)
            .ThenBy(static player => player.Name, StringComparer.Ordinal))
        {
            AppendString(builder, player.CompatHash);
        }

        return builder.ToString();
    }

    private static string CreatePlayerCompatHash(
        DirectStrikePlayer player,
        PlayerDto playerDto,
        Commander selectedRace,
        SpawnDto? snapshot)
    {
        StringBuilder builder = new();
        AppendString(builder, "ds-player-compat-v1");
        AppendInt(builder, player.TeamId);
        AppendInt(builder, player.GamePos);
        AppendInt(builder, (int)player.Commander);
        AppendInt(builder, (int)selectedRace);
        AppendInt(builder, playerDto.ToonId.Region);
        AppendInt(builder, playerDto.ToonId.Realm);
        AppendInt(builder, playerDto.ToonId.Id);
        AppendInt(builder, playerDto.PlayerId);
        AppendString(builder, player.Name);
        AppendString(builder, player.Clan ?? string.Empty);
        AppendInt(builder, snapshot is null ? 0 : (int)snapshot.Breakpoint);
        AppendInt(builder, snapshot?.Income ?? 0);
        AppendInt(builder, snapshot?.GasCount ?? 0);
        AppendInt(builder, snapshot?.ArmyValue ?? 0);
        AppendInt(builder, snapshot?.KilledValue ?? 0);
        AppendInt(builder, snapshot?.LostValue ?? 0);
        AppendInt(builder, snapshot?.UpgradeSpent ?? 0);
        AppendInt(builder, snapshot?.Units.Count ?? 0);

        if (snapshot is not null)
        {
            foreach (UnitDto unit in snapshot.Units.OrderBy(static unit => unit.Name, StringComparer.Ordinal))
            {
                AppendString(builder, unit.Name);
                AppendInt(builder, unit.Count);
                AppendInt(builder, unit.Positions.Count);
                foreach (int position in unit.Positions)
                {
                    AppendInt(builder, position);
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendInt(StringBuilder builder, int value)
    {
        builder.Append('i');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('s');
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static ReplayPlayerDto CreatePlayerDto(DirectStrikePlayer player, MessageCounts messageCounts, int compatHashGameloop)
    {
        Dictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn = GetArmyValuesBySpawn(player);
        Dictionary<DirectStrikePlayerSpawn, int> incomesBySpawn = GetIncomesBySpawn(player);
        SpawnDto? compatHashSnapshot = CreateCompatHashSnapshotDto(player, compatHashGameloop, armyValuesBySpawn, incomesBySpawn);
        Commander selectedRace = ToCommander(player.SelectedRace);
        PlayerDto playerDto = new()
        {
            PlayerId = player.Id,
            Name = player.Name,
            ToonId = new()
            {
                Region = player.Region,
                Realm = player.Realm,
                Id = player.Id,
            },
        };

        return new()
        {
            CompatHash = CreatePlayerCompatHash(player, playerDto, selectedRace, compatHashSnapshot),
            Name = player.Name,
            Clan = player.Clan,
            Race = player.Commander,
            SelectedRace = selectedRace,
            TeamId = player.TeamId,
            GamePos = player.GamePos,
            Result = player.Result,
            Duration = player.Duration,
            Apm = (int)Math.Round(player.APM),
            Messages = messageCounts.Messages,
            Pings = messageCounts.Pings,
            IsMvp = false,
            Spawns = CreateSpawnDtos(player, armyValuesBySpawn, incomesBySpawn),
            Upgrades = CreateUpgradeDtos(player),
            TierUpgrades = player.TierUpgrades,
            Refineries = player.RefineryTimes,
            Player = playerDto,
        };
    }

    private static int GetRegionId(DirectStrikeReplay directStrikeReplay)
    {
        foreach (DirectStrikePlayer player in directStrikeReplay.Players)
        {
            if (player.Region != 0)
            {
                return player.Region;
            }
        }

        return 0;
    }

    private static List<SpawnDto> CreateSpawnDtos(
        DirectStrikePlayer player,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> incomesBySpawn)
    {
        List<DirectStrikePlayerSpawn> statsBackedSpawns = GetStatsBackedSpawns(player);
        if (statsBackedSpawns.Count == 0)
        {
            return [];
        }

        List<SpawnDto> spawns = new(BreakpointDefinitions.Length + 1);
        foreach (BreakpointDefinition breakpoint in BreakpointDefinitions)
        {
            if (player.DurationGameloop > 0 && breakpoint.Gameloop > player.DurationGameloop)
            {
                continue;
            }

            DirectStrikePlayerSpawn spawn = FindClosestBreakpointSpawn(statsBackedSpawns, breakpoint.Gameloop);
            spawns.Add(CreateSpawnDto(breakpoint.Breakpoint, spawn, player, armyValuesBySpawn, incomesBySpawn));
        }

        spawns.Add(CreateSpawnDto(Breakpoint.All, statsBackedSpawns[^1], player, armyValuesBySpawn, incomesBySpawn));
        return spawns;
    }

    private static SpawnDto? CreateCompatHashSnapshotDto(
        DirectStrikePlayer player,
        int compatHashGameloop,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> armyValuesBySpawn,
        IReadOnlyDictionary<DirectStrikePlayerSpawn, int> incomesBySpawn)
    {
        if (compatHashGameloop <= 0)
        {
            return null;
        }

        List<DirectStrikePlayerSpawn> statsBackedSpawns = new(player.Spawns.Count);
        foreach (DirectStrikePlayerSpawn spawn in player.Spawns)
        {
            if (spawn.SummaryStats is { MineralsCollectionRate: > 0 } stats && stats.Gameloop <= compatHashGameloop)
            {
                statsBackedSpawns.Add(spawn);
            }
        }

        if (statsBackedSpawns.Count == 0)
        {
            return null;
        }

        DirectStrikePlayerSpawn compatHashSpawn = FindClosestBreakpointSpawn(statsBackedSpawns, compatHashGameloop);
        return CreateSpawnDto(Breakpoint.Min5, compatHashSpawn, player, armyValuesBySpawn, incomesBySpawn);
    }

    private static List<DirectStrikePlayerSpawn> GetStatsBackedSpawns(DirectStrikePlayer player)
    {
        List<DirectStrikePlayerSpawn> statsBackedSpawns = new(player.Spawns.Count);
        foreach (DirectStrikePlayerSpawn spawn in player.Spawns)
        {
            if (spawn.SummaryStats is { MineralsCollectionRate: > 0 })
            {
                statsBackedSpawns.Add(spawn);
            }
        }

        return statsBackedSpawns;
    }

    private static DirectStrikePlayerSpawn FindClosestBreakpointSpawn(List<DirectStrikePlayerSpawn> spawns, int targetGameloop)
    {
        DirectStrikePlayerSpawn bestSpawn = spawns[0];
        int bestDistance = Math.Abs(bestSpawn.EndGameloop - targetGameloop);

        for (int i = 1; i < spawns.Count; i++)
        {
            DirectStrikePlayerSpawn spawn = spawns[i];
            int distance = Math.Abs(spawn.EndGameloop - targetGameloop);
            if (distance < bestDistance || (distance == bestDistance && spawn.EndGameloop < bestSpawn.EndGameloop))
            {
                bestSpawn = spawn;
                bestDistance = distance;
            }
        }

        return bestSpawn;
    }

    private static List<UpgradeDto> CreateUpgradeDtos(DirectStrikePlayer player)
    {
        List<KeyValuePair<string, TimeSpan>> upgrades = new(player.Upgrades.Count);
        foreach (KeyValuePair<string, TimeSpan> upgrade in player.Upgrades)
        {
            upgrades.Add(upgrade);
        }

        upgrades.Sort(static (left, right) =>
        {
            int timeComparison = left.Value.CompareTo(right.Value);
            return timeComparison != 0 ? timeComparison : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        });

        List<UpgradeDto> upgradeDtos = new(upgrades.Count);
        foreach (KeyValuePair<string, TimeSpan> upgrade in upgrades)
        {
            upgradeDtos.Add(new()
            {
                Name = upgrade.Key,
                Time = upgrade.Value,
            });
        }

        return upgradeDtos;
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
            GasCount = GetGasCount(player, stats.Time),
            ArmyValue = armyValuesBySpawn.GetValueOrDefault(spawn),
            KilledValue = stats.MineralsKilledArmy,
            LostValue = stats.MineralsLostArmy,
            UpgradeSpent = stats.MineralsUsedCurrentTechnology,
            Units = CreateUnitDtos(spawn),
        };
    }

    private static int GetGasCount(DirectStrikePlayer player, TimeSpan targetTime)
    {
        int gasCount = 0;
        foreach (TimeSpan refinery in player.RefineryTimes)
        {
            if (refinery <= targetTime)
            {
                gasCount++;
            }
        }

        return gasCount;
    }

    private static List<UnitDto> CreateUnitDtos(DirectStrikePlayerSpawn spawn)
    {
        Dictionary<string, UnitDtoBuilder> unitsByName = new(StringComparer.Ordinal);
        foreach (DirectStrikeSpawnUnit unit in spawn.Units)
        {
            if (!unitsByName.TryGetValue(unit.Name, out UnitDtoBuilder? builder))
            {
                builder = new(unit.Name);
                unitsByName.Add(unit.Name, builder);
            }

            builder.Count++;
            builder.Positions.Add(unit.X);
            builder.Positions.Add(unit.Y);
        }

        List<UnitDtoBuilder> builders = new(unitsByName.Count);
        foreach (UnitDtoBuilder builder in unitsByName.Values)
        {
            builders.Add(builder);
        }

        builders.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));

        List<UnitDto> units = new(builders.Count);
        foreach (UnitDtoBuilder builder in builders)
        {
            units.Add(new()
            {
                Name = builder.Name,
                Count = builder.Count,
                Positions = builder.Positions,
            });
        }

        return units;
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
        int refineryCount = 0;
        foreach (TimeSpan refinery in player.RefineryTimes)
        {
            if (ToGameloop(refinery) < targetGameloop)
            {
                refineryCount++;
            }
        }

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

        return replay.Header is Header header ? header.Version.ToString() : string.Empty;
    }

    private static int ParseBaseBuild(string baseBuild, Sc2Replay replay)
    {
        if (int.TryParse(baseBuild, out int parsedBaseBuild))
        {
            return parsedBaseBuild;
        }

        return replay.Header is Header header ? header.BaseBuild : 0;
    }

    private readonly record struct BreakpointDefinition(Breakpoint Breakpoint, int Gameloop);

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

    private sealed class UnitDtoBuilder(string name)
    {
        public string Name { get; } = name;

        public int Count { get; set; }

        public List<int> Positions { get; } = [];
    }
}
