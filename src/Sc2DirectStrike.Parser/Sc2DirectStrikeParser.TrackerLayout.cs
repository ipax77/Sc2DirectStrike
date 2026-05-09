using System.Diagnostics.CodeAnalysis;
using s2protocol.NET;
using s2protocol.NET.Models;

namespace Sc2DirectStrike.Parser;

public static partial class Sc2DirectStrikeParser
{
    private const double GameLoopsPerSecond = 22.4D;

    private static void SetTrackerData(Sc2Replay replay, DirectStrikePlayerContext[] playerContexts, DirectStrikeReplay directStrikeReplay)
    {
        Dictionary<int, DirectStrikePlayerContext> playerContextsByControlPlayerId = GetPlayerContextsByControlPlayerId(replay, playerContexts);
        Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts = [];
        Dictionary<(int UnitTagIndex, int UnitTagRecycle), DirectStrikePlayerRefinery> refineriesByTag = [];
        HashSet<int> mappedCommanderControlPlayerIds = [];
        MapLayout mapLayout = new();

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

        foreach (DirectStrikePlayerContext context in playerContexts)
        {
            context.Player.RefineryTimes = [.. context.Refineries
                .Where(refinery => refinery.Taken)
                .OrderBy(refinery => refinery.Gameloop)
                .Select(refinery => ToTimeSpan(refinery.Gameloop))];
        }

        if (mapLayout.Planetary is { } planetary)
        {
            SetGamePositions(playerLayouts, planetary);
        }
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

    private static void SetGamePositions(Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts, Pos planetary)
    {
        List<PlayerLayoutEntry> playersTeam1 = [.. GetLayoutEntries(playerLayouts, 1)];
        List<PlayerLayoutEntry> playersTeam2 = [.. GetLayoutEntries(playerLayouts, 2)];

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

    private static IEnumerable<PlayerLayoutEntry> GetLayoutEntries(Dictionary<DirectStrikePlayer, PlayerLayout> playerLayouts, int teamId)
    {
        foreach (KeyValuePair<DirectStrikePlayer, PlayerLayout> pair in playerLayouts)
        {
            if (pair.Key.TeamId == teamId)
            {
                yield return new(pair.Key, pair.Value);
            }
        }
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
        List<PlayerLayoutEntry> playersWithSouth = [.. teamPlayers.Where(player => player.Layout.South.HasValue)];
        if (playersWithSouth.Count != 3)
        {
            return;
        }

        PlayerLayoutEntry middlePlayer = playersWithSouth
            .OrderBy(player => DistanceSquared(planetary, player.Layout.South!.Value))
            .First();
        PlayerLayoutEntry[] sidePlayers = [.. playersWithSouth.Where(player => !ReferenceEquals(player.Player, middlePlayer.Player))];

        SetThreePlayerSidePositions(middlePlayer, sidePlayers[0], sidePlayers[1]);
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

    private sealed class MapLayout
    {
        public Pos? Bunker { get; set; }
        public Pos? Cannon { get; set; }
        public Pos? Nexus { get; set; }
        public Pos? Planetary { get; set; }
    }

    private sealed class PlayerLayout
    {
        public Pos? East { get; set; }
        public Pos? North { get; set; }
        public Pos? South { get; set; }
        public Pos? West { get; set; }
    }

    private readonly record struct PlayerLayoutEntry(DirectStrikePlayer Player, PlayerLayout Layout);

    private readonly record struct Pos(int X, int Y);
}
