using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
{
    private const double GameLoopsPerSecond = 22.4D;
    private const int SpawnGroupWindowGameloops = 112;
    private const string PlayerStateVictoryUpgrade = "PlayerStateVictory";
    private const string PlayerStateGameOverUpgrade = "PlayerStateGameOver";

    private static void SetTrackerData(Sc2Replay replay, DirectStrikePlayerContext[] playerContexts, DirectStrikeReplay directStrikeReplay)
    {
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId = GetPlayerContextsByControlPlayerId(replay, playerContexts);
        Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts = new(playerContexts.Length);
        Dictionary<(int UnitTagIndex, int UnitTagRecycle), DirectStrikePlayerRefinery> refineriesByTag = [];
        HashSet<int> mappedCommanderControlPlayerIds = new(playerContexts.Length);
        List<MiddleControlChange> middleControlChanges = [];
        MapLayout mapLayout = new();

        SetPlayerStats(replay, playerContextsByControlPlayerId, playerContexts);

        foreach (SUnitBornEvent bornEvent in replay.TrackerEvents?.SUnitBornEvents ?? [])
        {
            if (bornEvent.Gameloop <= 1440
                && playerContextsByControlPlayerId.TryGetValue(bornEvent.ControlPlayerId, out DirectStrikePlayerContext? commanderContext)
                && TryParseWorkerCommander(bornEvent.UnitTypeName, out Commander commander)
                && mappedCommanderControlPlayerIds.Add(bornEvent.ControlPlayerId))
            {
                commanderContext.Player.Commander = commander;
            }

            if (bornEvent.Gameloop != 0)
            {
                continue;
            }

            Pos pos = new(bornEvent.X, bornEvent.Y);
            switch (bornEvent.UnitTypeName)
            {
                case "StagingAreaFootprintSouth":
                case "AreaMarkerSouth":
                    if (TryGetPlayerLayout(playerContextsByControlPlayerId, playerLayouts, bornEvent.ControlPlayerId, out DirectStrikePlayer? southPlayer, out PlayerLayout? southLayout))
                    {
                        southLayout.South = pos;
                        southPlayer.TeamId = bornEvent.Y * bornEvent.Y > 10000 ? 1 : 2;
                    }

                    break;

                case "StagingAreaFootprintWest":
                case "AreaMarkerWest":
                    if (TryGetPlayerLayout(playerContextsByControlPlayerId, playerLayouts, bornEvent.ControlPlayerId, out _, out PlayerLayout? westLayout))
                    {
                        westLayout.West = pos;
                    }

                    break;

                case "StagingAreaFootprintNorth":
                case "AreaMarkerNorth":
                    if (TryGetPlayerLayout(playerContextsByControlPlayerId, playerLayouts, bornEvent.ControlPlayerId, out _, out PlayerLayout? northLayout))
                    {
                        northLayout.North = pos;
                    }

                    break;

                case "StagingAreaFootprintEast":
                case "AreaMarkerEast":
                    if (TryGetPlayerLayout(playerContextsByControlPlayerId, playerLayouts, bornEvent.ControlPlayerId, out _, out PlayerLayout? eastLayout))
                    {
                        eastLayout.East = pos;
                    }

                    break;

                case "ObjectiveNexus":
                    mapLayout.Nexus = pos;
                    if (bornEvent.SUnitDiedEvent is { } nexusDeath)
                    {
                        directStrikeReplay.GameEndTime = ToTimeSpan(nexusDeath.Gameloop);
                        directStrikeReplay.WinnerTeam = 1;
                    }

                    break;

                case "ObjectivePlanetaryFortress":
                    mapLayout.Planetary = pos;
                    if (bornEvent.SUnitDiedEvent is { } planetaryDeath)
                    {
                        directStrikeReplay.GameEndTime = ToTimeSpan(planetaryDeath.Gameloop);
                        directStrikeReplay.WinnerTeam = 2;
                    }

                    break;

                case "ObjectivePhotonCannon":
                    mapLayout.Cannon = pos;
                    if (bornEvent.SUnitDiedEvent is { } cannonDeath)
                    {
                        directStrikeReplay.CannonTime = ToTimeSpan(cannonDeath.Gameloop);
                    }

                    break;

                case "ObjectiveBunker":
                    mapLayout.Bunker = pos;
                    if (bornEvent.SUnitDiedEvent is { } bunkerDeath)
                    {
                        directStrikeReplay.BunkerTime = ToTimeSpan(bunkerDeath.Gameloop);
                    }

                    break;
            }

            if (bornEvent.UnitTypeName.StartsWith("MineralField", StringComparison.Ordinal)
                && playerContextsByControlPlayerId.TryGetValue(bornEvent.ControlPlayerId, out DirectStrikePlayerContext? refineryContext))
            {
                DirectStrikePlayerRefinery refinery = new()
                {
                    UnitTagIndex = bornEvent.UnitTagIndex,
                    UnitTagRecycle = bornEvent.UnitTagRecycle,
                };
                refineryContext.Refineries.Add(refinery);
                refineriesByTag.TryAdd((refinery.UnitTagIndex, refinery.UnitTagRecycle), refinery);
            }
        }

        SetPlayerUpgrades(replay, playerContextsByControlPlayerId, playerContexts, directStrikeReplay);

        foreach (SUnitTypeChangeEvent typeChangeEvent in replay.TrackerEvents?.SUnitTypeChangeEvents ?? [])
        {
            if (!IsRefineryMinerals(typeChangeEvent.UnitTypeName)
                || !refineriesByTag.TryGetValue((typeChangeEvent.UnitTagIndex, typeChangeEvent.UnitTagRecycle), out DirectStrikePlayerRefinery? refinery)
                || refinery.Taken)
            {
                continue;
            }

            refinery.Gameloop = typeChangeEvent.Gameloop;
            refinery.Taken = true;
        }

        foreach (SUnitOwnerChangeEvent ownerChangeEvent in replay.TrackerEvents?.SUnitOwnerChangeEvents ?? [])
        {
            if (ownerChangeEvent.UnitTagIndex != 20
                || !TryGetMiddleControlTeam(ownerChangeEvent.UpkeepPlayerId, out int team))
            {
                continue;
            }

            middleControlChanges.Add(new(ownerChangeEvent.Gameloop, team));
        }

        if (middleControlChanges.Count > 0)
        {
            directStrikeReplay.FirstMiddleControlTeam = middleControlChanges[0].Team;
            TimeSpan[] middleChanges = new TimeSpan[middleControlChanges.Count];
            for (int i = 0; i < middleControlChanges.Count; i++)
            {
                middleChanges[i] = ToTimeSpan(middleControlChanges[i].Gameloop);
            }

            directStrikeReplay.MiddleChanges = middleChanges;
        }

        foreach (DirectStrikePlayerContext context in playerContexts)
        {
            context.Refineries.Sort(static (left, right) => left.Gameloop.CompareTo(right.Gameloop));
            List<TimeSpan> refineryTimes = new(context.Refineries.Count);
            foreach (DirectStrikePlayerRefinery refinery in context.Refineries)
            {
                if (refinery.Taken)
                {
                    refineryTimes.Add(ToTimeSpan(refinery.Gameloop));
                }
            }

            context.Player.RefineryTimes = [.. refineryTimes];
        }

        if (mapLayout.Planetary is { } planetary)
        {
            SetGamePositions(playerLayouts, planetary);
        }

        SetPlayerSpawns(replay, playerContexts, playerContextsByControlPlayerId, playerLayouts);
    }

    private static void SetPlayerSpawns(
        Sc2Replay replay,
        DirectStrikePlayerContext[] playerContexts,
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId,
        Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts)
    {
        Dictionary<DirectStrikePlayer, HashSet<string>> builtUnitNamesByPlayer = new(playerContexts.Length);
        Dictionary<DirectStrikePlayer, List<TrackedSpawnUnit>> spawnUnitsByPlayer = new(playerContexts.Length);
        Dictionary<DirectStrikePlayer, Polygon> stagingAreasByPlayer = GetStagingAreasByPlayer(playerLayouts);
        ICollection<SUnitBornEvent> unitBornEvents = replay.TrackerEvents?.SUnitBornEvents ?? [];

        if (IsOrderedByGameloop(unitBornEvents))
        {
            foreach (SUnitBornEvent unitBornEvent in unitBornEvents)
            {
                TrackSpawnUnit(unitBornEvent, playerContextsByControlPlayerId, stagingAreasByPlayer, builtUnitNamesByPlayer, spawnUnitsByPlayer);
            }
        }
        else
        {
            List<OrderedUnitBornEvent> orderedUnitBornEvents = new(unitBornEvents.Count);
            int index = 0;
            foreach (SUnitBornEvent unitBornEvent in unitBornEvents)
            {
                orderedUnitBornEvents.Add(new(unitBornEvent, index));
                index++;
            }

            orderedUnitBornEvents.Sort(static (left, right) =>
            {
                int gameloopComparison = left.Event.Gameloop.CompareTo(right.Event.Gameloop);
                return gameloopComparison != 0 ? gameloopComparison : left.Index.CompareTo(right.Index);
            });

            foreach (OrderedUnitBornEvent orderedUnitBornEvent in orderedUnitBornEvents)
            {
                TrackSpawnUnit(orderedUnitBornEvent.Event, playerContextsByControlPlayerId, stagingAreasByPlayer, builtUnitNamesByPlayer, spawnUnitsByPlayer);
            }
        }

        foreach (DirectStrikePlayerContext context in playerContexts)
        {
            context.Player.BuildUnitNames = builtUnitNamesByPlayer.TryGetValue(context.Player, out HashSet<string>? builtUnitNames)
                ? ToSortedReadOnlyCollection(builtUnitNames)
                : [];
            context.Player.Spawns = spawnUnitsByPlayer.TryGetValue(context.Player, out List<TrackedSpawnUnit>? spawnUnits)
                ? GroupPlayerSpawns(spawnUnits, context.Player.Stats)
                : [];
        }
    }

    private static ReadOnlyCollection<string> ToSortedReadOnlyCollection(HashSet<string> values)
    {
        List<string> sortedValues = [.. values];
        sortedValues.Sort(StringComparer.Ordinal);
        return sortedValues.AsReadOnly();
    }

    private static bool IsOrderedByGameloop(ICollection<SUnitBornEvent> unitBornEvents)
    {
        int previousGameloop = 0;
        bool hasPrevious = false;
        foreach (SUnitBornEvent unitBornEvent in unitBornEvents)
        {
            if (hasPrevious && unitBornEvent.Gameloop < previousGameloop)
            {
                return false;
            }

            previousGameloop = unitBornEvent.Gameloop;
            hasPrevious = true;
        }

        return true;
    }

    private static void TrackSpawnUnit(
        SUnitBornEvent unitBornEvent,
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId,
        Dictionary<DirectStrikePlayer, Polygon> stagingAreasByPlayer,
        Dictionary<DirectStrikePlayer, HashSet<string>> builtUnitNamesByPlayer,
        Dictionary<DirectStrikePlayer, List<TrackedSpawnUnit>> spawnUnitsByPlayer)
    {
        if (unitBornEvent.Gameloop == 0
            || !playerContextsByControlPlayerId.TryGetValue(unitBornEvent.ControlPlayerId, out DirectStrikePlayerContext? context))
        {
            return;
        }

        DirectStrikePlayer player = context.Player;
        Pos position = new(unitBornEvent.X, unitBornEvent.Y);
        if (stagingAreasByPlayer.TryGetValue(player, out Polygon? stagingArea)
            && stagingArea.Contains(position))
        {
            GetBuiltUnitNames(builtUnitNamesByPlayer, player).Add(unitBornEvent.UnitTypeName);
        }

        if (player.TeamId is not (1 or 2)
            || !MapLayout.IsSpawnUnit(position, player.TeamId)
            || !builtUnitNamesByPlayer.TryGetValue(player, out HashSet<string>? builtUnitNames)
            || !IsAllowedSpawnUnitName(builtUnitNames, unitBornEvent.UnitTypeName))
        {
            return;
        }

        GetSpawnUnits(spawnUnitsByPlayer, player).Add(new(
            unitBornEvent.UnitIndex,
            unitBornEvent.UnitTypeName,
            unitBornEvent.Gameloop,
            position,
            unitBornEvent.SUnitDiedEvent is { } diedEvent ? new(diedEvent.X, diedEvent.Y) : null,
            unitBornEvent.SUnitDiedEvent?.Gameloop));
    }

    private static bool IsAllowedSpawnUnitName(HashSet<string> builtUnitNames, string spawnUnitName)
    {
        foreach (string builtUnitName in builtUnitNames)
        {
            if (IsAllowedSpawnUnitName(builtUnitName, spawnUnitName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedSpawnUnitName(string builtUnitName, string spawnUnitName)
    {
        return string.Equals(spawnUnitName, builtUnitName, StringComparison.Ordinal)
            || string.Equals(spawnUnitName, builtUnitName + "Lightweight", StringComparison.Ordinal)
            || string.Equals(spawnUnitName, builtUnitName + "Starlight", StringComparison.Ordinal)
            || string.Equals(spawnUnitName, builtUnitName + "MP", StringComparison.Ordinal)
            || string.Equals(spawnUnitName, builtUnitName + "AP", StringComparison.Ordinal);
    }

    private static void SetPlayerStats(
        Sc2Replay replay,
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId,
        DirectStrikePlayerContext[] playerContexts)
    {
        Dictionary<DirectStrikePlayer, List<DirectStrikePlayerStats>> statsByPlayer = new(playerContexts.Length);

        foreach (SPlayerStatsEvent statsEvent in replay.TrackerEvents?.SPlayerStatsEvents ?? [])
        {
            if (!playerContextsByControlPlayerId.TryGetValue(statsEvent.PlayerId, out DirectStrikePlayerContext? context))
            {
                continue;
            }

            DirectStrikePlayerStats stats = new()
            {
                Gameloop = statsEvent.Gameloop,
                Time = ToTimeSpan(statsEvent.Gameloop),
                MineralsCollectionRate = statsEvent.MineralsCollectionRate,
                MineralsUsedActiveForces = statsEvent.MineralsUsedActiveForces,
                MineralsUsedCurrentTechnology = statsEvent.MineralsUsedCurrentTechnology,
                MineralsKilledArmy = statsEvent.MineralsKilledArmy,
                MineralsLostArmy = statsEvent.MineralsLostArmy,
            };

            GetPlayerStats(statsByPlayer, context.Player).Add(stats);
            if (stats.MineralsCollectionRate > 0 && stats.Gameloop >= context.Player.DurationGameloop)
            {
                context.Player.DurationGameloop = stats.Gameloop;
                context.Player.Duration = stats.Time;
            }
        }

        foreach (DirectStrikePlayerContext context in playerContexts)
        {
            context.Player.Stats = statsByPlayer.TryGetValue(context.Player, out List<DirectStrikePlayerStats>? stats)
                ? SortStats(stats)
                : [];
        }
    }

    private static ReadOnlyCollection<DirectStrikePlayerStats> SortStats(List<DirectStrikePlayerStats> stats)
    {
        stats.Sort(static (left, right) => left.Gameloop.CompareTo(right.Gameloop));
        return stats.AsReadOnly();
    }

    private static void SetPlayerUpgrades(
        Sc2Replay replay,
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId,
        DirectStrikePlayerContext[] playerContexts,
        DirectStrikeReplay directStrikeReplay)
    {
        Dictionary<DirectStrikePlayer, List<int>> tierUpgradesByPlayer = new(playerContexts.Length);
        Dictionary<DirectStrikePlayer, Dictionary<string, int>> upgradesByPlayer = new(playerContexts.Length);
        int? victoryTeam = null;
        bool hasInvalidVictoryTeam = false;

        foreach (SUpgradeEvent upgradeEvent in replay.TrackerEvents?.SUpgradeEvents ?? [])
        {
            if (upgradeEvent.Gameloop == 0
                || !playerContextsByControlPlayerId.TryGetValue(upgradeEvent.PlayerId, out DirectStrikePlayerContext? context))
            {
                continue;
            }

            DirectStrikePlayer player = context.Player;
            string upgradeName = upgradeEvent.UpgradeTypeName;
            if (upgradeName is PlayerStateVictoryUpgrade or PlayerStateGameOverUpgrade)
            {
                if (upgradeEvent.Gameloop > player.DurationGameloop)
                {
                    player.DurationGameloop = upgradeEvent.Gameloop;
                    player.Duration = ToTimeSpan(upgradeEvent.Gameloop);
                }

                if (upgradeName == PlayerStateVictoryUpgrade)
                {
                    if (player.TeamId is not (1 or 2))
                    {
                        hasInvalidVictoryTeam = true;
                    }
                    else if (victoryTeam is null)
                    {
                        victoryTeam = player.TeamId;
                    }
                    else if (victoryTeam.Value != player.TeamId)
                    {
                        hasInvalidVictoryTeam = true;
                    }
                }

                continue;
            }

            if (upgradeName is "Tier2" or "Tier3")
            {
                GetTierUpgrades(tierUpgradesByPlayer, player).Add(upgradeEvent.Gameloop);
                continue;
            }

            if (FilterUpgrades(upgradeName)
                || (upgradeName.Contains("Level", StringComparison.Ordinal) && !IsNormalizedLevelUpgrade(upgradeName, player.Commander)))
            {
                continue;
            }

            GetPlayerUpgrades(upgradesByPlayer, player).TryAdd(upgradeName, upgradeEvent.Gameloop);
        }

        if (victoryTeam is { } team && !hasInvalidVictoryTeam)
        {
            directStrikeReplay.WinnerTeam = team;
        }

        foreach (DirectStrikePlayerContext context in playerContexts)
        {
            DirectStrikePlayer player = context.Player;
            if (tierUpgradesByPlayer.TryGetValue(player, out List<int>? tierUpgrades))
            {
                tierUpgrades.Sort();
                TimeSpan[] tierUpgradeTimes = new TimeSpan[tierUpgrades.Count];
                for (int i = 0; i < tierUpgrades.Count; i++)
                {
                    tierUpgradeTimes[i] = ToTimeSpan(tierUpgrades[i]);
                }

                player.TierUpgrades = tierUpgradeTimes;
            }

            if (upgradesByPlayer.TryGetValue(player, out Dictionary<string, int>? upgrades))
            {
                Dictionary<string, TimeSpan> upgradeTimes = new(upgrades.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> pair in upgrades)
                {
                    upgradeTimes.Add(pair.Key, ToTimeSpan(pair.Value));
                }

                player.Upgrades = new ReadOnlyDictionary<string, TimeSpan>(upgradeTimes);
            }
        }
    }

    private static Dictionary<DirectStrikePlayer, Polygon> GetStagingAreasByPlayer(Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts)
    {
        Dictionary<DirectStrikePlayer, Polygon> stagingAreasByPlayer = new(playerLayouts.Count);
        foreach (KeyValuePair<DirectStrikePlayer, PlayerLayout> pair in playerLayouts)
        {
            if (pair.Value.TryGetStagingArea(out Polygon? stagingArea))
            {
                stagingAreasByPlayer.Add(pair.Key, stagingArea);
            }
        }

        return stagingAreasByPlayer;
    }

    private static HashSet<string> GetBuiltUnitNames(Dictionary<DirectStrikePlayer, HashSet<string>> builtUnitNamesByPlayer, DirectStrikePlayer player)
    {
        if (!builtUnitNamesByPlayer.TryGetValue(player, out HashSet<string>? builtUnitNames))
        {
            builtUnitNames = new(StringComparer.Ordinal);
            builtUnitNamesByPlayer.Add(player, builtUnitNames);
        }

        return builtUnitNames;
    }

    private static List<TrackedSpawnUnit> GetSpawnUnits(Dictionary<DirectStrikePlayer, List<TrackedSpawnUnit>> spawnUnitsByPlayer, DirectStrikePlayer player)
    {
        if (!spawnUnitsByPlayer.TryGetValue(player, out List<TrackedSpawnUnit>? spawnUnits))
        {
            spawnUnits = [];
            spawnUnitsByPlayer.Add(player, spawnUnits);
        }

        return spawnUnits;
    }

    private static List<DirectStrikePlayerStats> GetPlayerStats(Dictionary<DirectStrikePlayer, List<DirectStrikePlayerStats>> statsByPlayer, DirectStrikePlayer player)
    {
        if (!statsByPlayer.TryGetValue(player, out List<DirectStrikePlayerStats>? stats))
        {
            stats = [];
            statsByPlayer.Add(player, stats);
        }

        return stats;
    }

    private static List<int> GetTierUpgrades(Dictionary<DirectStrikePlayer, List<int>> tierUpgradesByPlayer, DirectStrikePlayer player)
    {
        if (!tierUpgradesByPlayer.TryGetValue(player, out List<int>? tierUpgrades))
        {
            tierUpgrades = [];
            tierUpgradesByPlayer.Add(player, tierUpgrades);
        }

        return tierUpgrades;
    }

    private static Dictionary<string, int> GetPlayerUpgrades(Dictionary<DirectStrikePlayer, Dictionary<string, int>> upgradesByPlayer, DirectStrikePlayer player)
    {
        if (!upgradesByPlayer.TryGetValue(player, out Dictionary<string, int>? upgrades))
        {
            upgrades = new(StringComparer.Ordinal);
            upgradesByPlayer.Add(player, upgrades);
        }

        return upgrades;
    }

    private static ReadOnlyCollection<DirectStrikePlayerSpawn> GroupPlayerSpawns(List<TrackedSpawnUnit> spawnUnits, IReadOnlyList<DirectStrikePlayerStats> playerStats)
    {
        List<DirectStrikePlayerSpawn> spawns = [];
        List<TrackedSpawnUnit> currentSpawnUnits = [];
        int currentSpawnGameloop = 0;
        int lastUnitGameloop = 0;
        int spawnNumber = 1;

        foreach (TrackedSpawnUnit spawnUnit in spawnUnits)
        {
            if (currentSpawnUnits.Count == 0)
            {
                currentSpawnGameloop = spawnUnit.Gameloop;
            }
            else if (spawnUnit.Gameloop - lastUnitGameloop > SpawnGroupWindowGameloops)
            {
                spawns.Add(CreatePlayerSpawn(spawnNumber, currentSpawnGameloop, currentSpawnUnits, playerStats));
                spawnNumber++;
                currentSpawnUnits = [];
                currentSpawnGameloop = spawnUnit.Gameloop;
            }

            currentSpawnUnits.Add(spawnUnit);
            lastUnitGameloop = spawnUnit.Gameloop;
        }

        if (currentSpawnUnits.Count > 0)
        {
            spawns.Add(CreatePlayerSpawn(spawnNumber, currentSpawnGameloop, currentSpawnUnits, playerStats));
        }

        return spawns.AsReadOnly();
    }

    private static DirectStrikePlayerSpawn CreatePlayerSpawn(int number, int startGameloop, List<TrackedSpawnUnit> spawnUnits, IReadOnlyList<DirectStrikePlayerStats> playerStats)
    {
        int endGameloop = spawnUnits[^1].Gameloop;
        List<DirectStrikeSpawnUnit> units = new(spawnUnits.Count);
        foreach (TrackedSpawnUnit unit in spawnUnits)
        {
            units.Add(new()
            {
                UnitIndex = unit.UnitIndex,
                Name = unit.Name,
                Gameloop = unit.Gameloop,
                X = unit.Position.X,
                Y = unit.Position.Y,
                DiedGameloop = unit.DiedGameloop,
                DiedX = unit.DiedPosition?.X,
                DiedY = unit.DiedPosition?.Y,
            });
        }

        return new()
        {
            Number = number,
            StartGameloop = startGameloop,
            EndGameloop = endGameloop,
            SummaryStats = GetSummaryStats(playerStats, endGameloop),
            Units = units.AsReadOnly(),
        };
    }

    private static DirectStrikePlayerStats? GetSummaryStats(IReadOnlyList<DirectStrikePlayerStats> playerStats, int endGameloop)
    {
        foreach (DirectStrikePlayerStats stat in playerStats)
        {
            if (stat.Gameloop >= endGameloop)
            {
                return stat;
            }
        }

        return null;
    }

    private static bool TryGetPlayerLayout(
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId,
        Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts,
        int controlPlayerId,
        [NotNullWhen(true)] out DirectStrikePlayer? player,
        [NotNullWhen(true)] out PlayerLayout? playerLayout)
    {
        if (!playerContextsByControlPlayerId.TryGetValue(controlPlayerId, out DirectStrikePlayerContext? context))
        {
            player = null;
            playerLayout = null;
            return false;
        }

        player = context.Player;
        if (!playerLayouts.TryGetValue(player, out playerLayout))
        {
            playerLayout = new();
            playerLayouts.Add(player, playerLayout);
        }

        return true;
    }

    private static bool IsRefineryMinerals(string unitTypeName)
    {
        return unitTypeName.StartsWith("RefineryMinerals", StringComparison.Ordinal)
            || unitTypeName.StartsWith("AssimilatorMinerals", StringComparison.Ordinal)
            || unitTypeName.StartsWith("ExtractorMinerals", StringComparison.Ordinal);
    }

    private static bool TryGetMiddleControlTeam(int upkeepPlayerId, out int team)
    {
        team = upkeepPlayerId switch
        {
            13 => 1,
            14 => 2,
            _ => 0,
        };

        return team != 0;
    }

    private static void SetGamePositions(Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts, Pos planetary)
    {
        List<PlayerLayoutEntry> playersTeam1 = GetLayoutEntries(playerLayouts, 1);
        List<PlayerLayoutEntry> playersTeam2 = GetLayoutEntries(playerLayouts, 2);

        SetTeamGamePositions(playersTeam1, planetary);
        SetTeamGamePositions(playersTeam2, planetary);

        foreach (PlayerLayoutEntry entry in playersTeam2)
        {
            if (entry.Player.GamePos > 0)
            {
                entry.Player.GamePos += 3;
            }
        }
    }

    private static List<PlayerLayoutEntry> GetLayoutEntries(Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts, int teamId)
    {
        List<PlayerLayoutEntry> entries = new(3);
        foreach (KeyValuePair<DirectStrikePlayer, PlayerLayout> pair in playerLayouts)
        {
            if (pair.Key.TeamId == teamId)
            {
                entries.Add(new(pair.Key, pair.Value));
            }
        }

        return entries;
    }

    private static void SetTeamGamePositions(List<PlayerLayoutEntry> teamPlayers, Pos planetary)
    {
        if (teamPlayers.Count == 1)
        {
            teamPlayers[0].Player.GamePos = 1;
        }
        else if (teamPlayers.Count == 2)
        {
            SetTwoPlayerTeamPositions(teamPlayers[0], teamPlayers[1], planetary);
        }
        else if (teamPlayers.Count == 3)
        {
            SetThreePlayerTeamPositions(teamPlayers, planetary);
        }
    }

    private static void SetTwoPlayerTeamPositions(PlayerLayoutEntry player1, PlayerLayoutEntry player2, Pos planetary)
    {
        if (player1.Layout.South is not { } south1 || player2.Layout.South is not { } south2)
        {
            return;
        }

        double d1 = DistanceSquared(planetary, south1);
        double d2 = DistanceSquared(planetary, south2);

        if (d1 > d2)
        {
            player1.Player.GamePos = 1;
            player2.Player.GamePos = 2;
        }
        else if (d2 > d1)
        {
            player1.Player.GamePos = 2;
            player2.Player.GamePos = 1;
        }
    }

    private static void SetThreePlayerTeamPositions(List<PlayerLayoutEntry> teamPlayers, Pos planetary)
    {
        List<PlayerLayoutEntry> playersWithSouth = new(3);
        foreach (PlayerLayoutEntry player in teamPlayers)
        {
            if (player.Layout.South.HasValue)
            {
                playersWithSouth.Add(player);
            }
        }

        if (playersWithSouth.Count != 3)
        {
            return;
        }

        PlayerLayoutEntry middlePlayer = playersWithSouth[0];
        double middleDistance = DistanceSquared(planetary, middlePlayer.Layout.South!.Value);
        for (int i = 1; i < playersWithSouth.Count; i++)
        {
            double distance = DistanceSquared(planetary, playersWithSouth[i].Layout.South!.Value);
            if (distance < middleDistance)
            {
                middlePlayer = playersWithSouth[i];
                middleDistance = distance;
            }
        }

        PlayerLayoutEntry sidePlayer1 = default;
        PlayerLayoutEntry sidePlayer2 = default;
        bool hasSidePlayer1 = false;
        foreach (PlayerLayoutEntry player in playersWithSouth)
        {
            if (!ReferenceEquals(player.Player, middlePlayer.Player))
            {
                if (hasSidePlayer1)
                {
                    sidePlayer2 = player;
                }
                else
                {
                    sidePlayer1 = player;
                    hasSidePlayer1 = true;
                }
            }
        }

        SetThreePlayerSidePositions(middlePlayer, sidePlayer1, sidePlayer2);
    }

    private static void SetThreePlayerSidePositions(PlayerLayoutEntry middlePlayer, PlayerLayoutEntry player1, PlayerLayoutEntry player2)
    {
        if (middlePlayer.Layout.West is not { } middleWest
            || player1.Layout.South is not { } south1
            || player2.Layout.South is not { } south2)
        {
            return;
        }

        double dm1 = DistanceSquared(middleWest, south1);
        double dm2 = DistanceSquared(middleWest, south2);

        if (dm1 < dm2)
        {
            middlePlayer.Player.GamePos = 2;
            player1.Player.GamePos = 1;
            player2.Player.GamePos = 3;
        }
        else if (dm2 < dm1)
        {
            middlePlayer.Player.GamePos = 2;
            player1.Player.GamePos = 3;
            player2.Player.GamePos = 1;
        }
    }

    private static double DistanceSquared(Pos p1, Pos p2)
    {
        double x = p1.X - p2.X;
        double y = p1.Y - p2.Y;

        return (x * x) + (y * y);
    }

    private static TimeSpan ToTimeSpan(int gameloop)
    {
        return TimeSpan.FromSeconds(gameloop / GameLoopsPerSecond);
    }

    private static bool IsNormalizedLevelUpgrade(string upgradeName, Commander commander)
    {
        string? normalizedCommanderPrefix = GetNormalizedCommanderPrefix(commander);
        return normalizedCommanderPrefix is not null && upgradeName.StartsWith(normalizedCommanderPrefix, StringComparison.Ordinal);
    }

    private static string? GetNormalizedCommanderPrefix(Commander commander)
    {
        return commander switch
        {
            Commander.Zagara or Commander.Abathur or Commander.Kerrigan => nameof(Commander.Zerg),
            Commander.Alarak or Commander.Artanis or Commander.Vorazun or Commander.Fenix or Commander.Karax or Commander.Zeratul => nameof(Commander.Protoss),
            Commander.Raynor or Commander.Swann or Commander.Nova or Commander.Stukov => nameof(Commander.Terran),
            Commander.None => nameof(Commander.None),
            Commander.Protoss => nameof(Commander.Protoss),
            Commander.Terran => nameof(Commander.Terran),
            Commander.Zerg => nameof(Commander.Zerg),
            Commander.Dehaka => nameof(Commander.Dehaka),
            Commander.Horner => nameof(Commander.Horner),
            Commander.Mengsk => nameof(Commander.Mengsk),
            Commander.Stetmann => nameof(Commander.Stetmann),
            Commander.Tychus => nameof(Commander.Tychus),
            Commander.Random => nameof(Commander.Random),
            _ => null,
        };
    }

    private sealed class MapLayout
    {
        private static readonly Polygon Team1SpawnArea = new(new(165, 174), new(182, 157), new(171, 146), new(154, 163));
        private static readonly Polygon Team2SpawnArea = new(new(84, 93), new(101, 76), new(90, 65), new(73, 82));

        public Pos? Bunker { get; set; }
        public Pos? Cannon { get; set; }
        public Pos? Nexus { get; set; }
        public Pos? Planetary { get; set; }

        public static bool IsSpawnUnit(Pos position, int teamId)
        {
            return teamId switch
            {
                1 => Team1SpawnArea.Contains(position),
                2 => Team2SpawnArea.Contains(position),
                _ => false,
            };
        }
    }

    private sealed class PlayerLayout
    {
        public Pos? East { get; set; }
        public Pos? North { get; set; }
        public Pos? South { get; set; }
        public Pos? West { get; set; }

        public bool TryGetStagingArea([NotNullWhen(true)] out Polygon? stagingArea)
        {
            if (South is { } south && East is { } east && North is { } north && West is { } west)
            {
                stagingArea = new(south, east, north, west);
                return true;
            }

            stagingArea = null;
            return false;
        }
    }

    private sealed class Polygon(params Pos[] points)
    {
        public bool Contains(Pos position)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Pos pi = points[i];
                Pos pj = points[j];
                if (IsOnSegment(pj, pi, position))
                {
                    return true;
                }

                bool intersects = (pi.Y > position.Y) != (pj.Y > position.Y)
                    && position.X < ((double)(pj.X - pi.X) * (position.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X;
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool IsOnSegment(Pos start, Pos end, Pos position)
        {
            long crossProduct = ((long)position.Y - start.Y) * (end.X - start.X)
                - ((long)position.X - start.X) * (end.Y - start.Y);
            if (crossProduct != 0)
            {
                return false;
            }

            return position.X >= Math.Min(start.X, end.X)
                && position.X <= Math.Max(start.X, end.X)
                && position.Y >= Math.Min(start.Y, end.Y)
                && position.Y <= Math.Max(start.Y, end.Y);
        }
    }

    private readonly record struct PlayerLayoutEntry(DirectStrikePlayer Player, PlayerLayout Layout);

    private readonly record struct MiddleControlChange(int Gameloop, int Team);

    private readonly record struct OrderedUnitBornEvent(SUnitBornEvent Event, int Index);

    private readonly record struct TrackedSpawnUnit(int UnitIndex, string Name, int Gameloop, Pos Position, Pos? DiedPosition, int? DiedGameloop);

    private readonly record struct Pos(int X, int Y);
}
