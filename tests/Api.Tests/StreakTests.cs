using Api.Stats;

namespace Api.Tests;

public class StreakTests
{
    static readonly DateOnly Today = new(2026, 9, 22);
    static DateOnly Ago(int days) => Today.AddDays(-days);

    [Fact]
    public void No_plays_means_no_streak() =>
        Assert.Equal((0, 0), Leaderboards.Streaks([], Today));

    [Fact]
    public void Counts_consecutive_days_ending_today() =>
        Assert.Equal((3, 3), Leaderboards.Streaks([Ago(0), Ago(1), Ago(2)], Today));

    [Fact]
    public void Streak_survives_until_today_is_over()
    {
        // Played the last 3 days but not yet today: still a live streak of 3.
        Assert.Equal((3, 3), Leaderboards.Streaks([Ago(1), Ago(2), Ago(3)], Today));
    }

    [Fact]
    public void Gap_breaks_current_streak_but_longest_is_kept() =>
        Assert.Equal((1, 4), Leaderboards.Streaks([Ago(0), Ago(3), Ago(4), Ago(5), Ago(6)], Today));

    [Fact]
    public void Missing_yesterday_and_today_means_zero_current() =>
        Assert.Equal((0, 2), Leaderboards.Streaks([Ago(2), Ago(3)], Today));

    [Fact]
    public void Duplicate_days_count_once() =>
        Assert.Equal((2, 2), Leaderboards.Streaks([Ago(0), Ago(0), Ago(1)], Today));

    [Fact]
    public void Local_day_uses_the_users_time_zone()
    {
        var utc = new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc); // 9 pm Dec 31 in New York
        Assert.Equal(new DateOnly(2025, 12, 31), Leaderboards.LocalDay(utc, Leaderboards.FindTimeZone("America/New_York")));
        Assert.Equal(new DateOnly(2026, 1, 1), Leaderboards.LocalDay(utc, TimeZoneInfo.Utc));
    }
}
