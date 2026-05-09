namespace Sc2DirectStrike.Parser;

public sealed class MiddleControlHelper
{
    private readonly TimeSpan duration;
    private readonly int firstTeam;
    private readonly TimeSpan[] changes;

    public MiddleControlHelper(ReplayDto replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        duration = replay.Duration < TimeSpan.Zero ? TimeSpan.Zero : replay.Duration;
        firstTeam = replay.FirstTeamCrossedMiddle;
        changes = [.. replay.MiddleChanges
            .Where(change => change >= TimeSpan.Zero)
            .Order()];
    }

    public (TimeSpan Team1, TimeSpan Team2) GetControl(TimeSpan atTime)
    {
        if (changes.Length == 0 || firstTeam is not (1 or 2) || duration <= TimeSpan.Zero)
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }

        TimeSpan target = Clamp(atTime);
        if (target <= changes[0])
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }

        TimeSpan team1 = TimeSpan.Zero;
        TimeSpan team2 = TimeSpan.Zero;
        int team = firstTeam;

        for (int i = 0; i < changes.Length; i++)
        {
            TimeSpan start = changes[i];
            if (start >= target)
            {
                break;
            }

            TimeSpan end = i + 1 < changes.Length ? changes[i + 1] : target;
            if (end > target)
            {
                end = target;
            }

            if (end > start)
            {
                TimeSpan control = end - start;
                if (team == 1)
                {
                    team1 += control;
                }
                else
                {
                    team2 += control;
                }
            }

            team = team == 1 ? 2 : 1;
        }

        return (team1, team2);
    }

    public (double Team1, double Team2) GetPercent(TimeSpan atTime)
    {
        TimeSpan target = Clamp(atTime);
        if (target <= TimeSpan.Zero)
        {
            return (0, 0);
        }

        (TimeSpan team1, TimeSpan team2) = GetControl(target);
        double elapsedSeconds = target.TotalSeconds;

        return (
            Math.Round(team1.TotalSeconds * 100D / elapsedSeconds, 2),
            Math.Round(team2.TotalSeconds * 100D / elapsedSeconds, 2));
    }

    private TimeSpan Clamp(TimeSpan atTime)
    {
        if (atTime <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return atTime > duration ? duration : atTime;
    }
}
