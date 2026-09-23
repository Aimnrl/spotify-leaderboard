using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Stats;

public enum Metric { Minutes, Diversity, Streak }
public enum Period { Week, Month, Year, All }

public record Member(int UserId, string DisplayName, string? AvatarUrl, string TimeZone);
/// <param name="Best">Streak metric only: the longest streak ever.</param>
public record Entry(int Rank, int UserId, string DisplayName, string? AvatarUrl, double Value, double? Best = null);
public record ArtistRow(string Artist, double Minutes, int Listeners, Entry TopFan);

/// <summary>Group rankings, computed on request. Groups are small, so no pre-aggregated tables are needed.</summary>
public static class Leaderboards
{
    public static async Task<List<Entry>> RankAsync(AppDb db, int groupId, Metric metric, Period period, DateTime nowUtc, CancellationToken ct = default)
    {
        var members = await MembersAsync(db, groupId, ct);
        var plays = PlaysOf(db, groupId, period, nowUtc);

        switch (metric)
        {
            case Metric.Minutes:
                var ms = await plays.GroupBy(p => p.UserId)
                    .Select(g => new { g.Key, Ms = g.Sum(p => (long)p.MsPlayed) })
                    .ToDictionaryAsync(x => x.Key, x => x.Ms, ct);
                return Rank(members.Select(m => (m, Minutes(ms.GetValueOrDefault(m.UserId)), (double?)null)));

            case Metric.Diversity:
                var artists = await plays.GroupBy(p => p.UserId)
                    .Select(g => new { g.Key, Count = g.Select(p => p.ArtistKey).Distinct().Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
                return Rank(members.Select(m => (m, (double)artists.GetValueOrDefault(m.UserId), (double?)null)));

            case Metric.Streak:
                // A streak is about "now", so it ignores the period.
                // ponytail: loads every play timestamp; pre-aggregate per-day if groups or histories get huge.
                var times = (await PlaysOf(db, groupId, Period.All, nowUtc)
                        .Select(p => new { p.UserId, p.PlayedAt })
                        .ToListAsync(ct))
                    .ToLookup(p => p.UserId, p => p.PlayedAt);
                return Rank(members.Select(m =>
                {
                    var tz = FindTimeZone(m.TimeZone);
                    var (current, longest) = Streaks(times[m.UserId].Select(t => LocalDay(t, tz)), LocalDay(nowUtc, tz));
                    return (m, (double)current, (double?)longest);
                }));

            default:
                throw new ArgumentOutOfRangeException(nameof(metric));
        }
    }

    /// <summary>Who in the group listens to this artist the most. Only members who've played the artist are listed.</summary>
    public static async Task<List<Entry>> SuperfansAsync(AppDb db, int groupId, string artist, Period period, DateTime nowUtc, CancellationToken ct = default)
    {
        var key = Play.KeyFor(artist);
        var members = await MembersAsync(db, groupId, ct);
        var ms = await PlaysOf(db, groupId, period, nowUtc)
            .Where(p => p.ArtistKey == key)
            .GroupBy(p => p.UserId)
            .Select(g => new { g.Key, Ms = g.Sum(p => (long)p.MsPlayed) })
            .ToDictionaryAsync(x => x.Key, x => x.Ms, ct);
        return Rank(members.Where(m => ms.ContainsKey(m.UserId)).Select(m => (m, Minutes(ms[m.UserId]), (double?)null)));
    }

    /// <summary>The group's most-played artists, each with its #1 fan.</summary>
    public static async Task<List<ArtistRow>> TopArtistsAsync(AppDb db, int groupId, Period period, DateTime nowUtc, int take = 20, CancellationToken ct = default)
    {
        var members = (await MembersAsync(db, groupId, ct)).ToDictionary(m => m.UserId);
        var rows = await PlaysOf(db, groupId, period, nowUtc)
            .GroupBy(p => new { p.ArtistKey, p.UserId })
            .Select(g => new { g.Key.ArtistKey, g.Key.UserId, Name = g.Max(p => p.ArtistName)!, Ms = g.Sum(p => (long)p.MsPlayed) })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.ArtistKey)
            .OrderByDescending(g => g.Sum(r => r.Ms))
            .Take(take)
            .Select(g =>
            {
                var top = g.MaxBy(r => r.Ms)!;
                var fan = members[top.UserId];
                return new ArtistRow(g.First().Name, Minutes(g.Sum(r => r.Ms)), g.Count(),
                    new Entry(1, fan.UserId, fan.DisplayName, fan.AvatarUrl, Minutes(top.Ms)));
            })
            .ToList();
    }

    /// <summary>Current streak counts back from today, or from yesterday if nothing's been played yet today.</summary>
    public static (int Current, int Longest) Streaks(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.ToHashSet();
        int longest = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in set.Order())
        {
            run = prev is { } p && d == p.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }

        var start = set.Contains(today) ? today : today.AddDays(-1);
        var current = 0;
        while (set.Contains(start.AddDays(-current))) current++;
        return (current, longest);
    }

    public static DateOnly LocalDay(DateTime utc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz));

    public static TimeZoneInfo FindTimeZone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;

    public static DateTime? Since(Period period, DateTime nowUtc) => period switch
    {
        Period.Week => nowUtc.AddDays(-7),
        Period.Month => nowUtc.AddDays(-30),
        Period.Year => nowUtc.AddDays(-365),
        _ => null,
    };

    static IQueryable<Play> PlaysOf(AppDb db, int groupId, Period period, DateTime nowUtc)
    {
        var memberIds = db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId);
        var plays = db.Plays.Where(p => memberIds.Contains(p.UserId));
        return Since(period, nowUtc) is { } since ? plays.Where(p => p.PlayedAt >= since) : plays;
    }

    static Task<List<Member>> MembersAsync(AppDb db, int groupId, CancellationToken ct) =>
        db.GroupMembers.Where(m => m.GroupId == groupId)
            .Select(m => new Member(m.UserId, m.User.DisplayName, m.User.AvatarUrl, m.User.TimeZone))
            .ToListAsync(ct);

    static double Minutes(long ms) => Math.Round(ms / 60_000.0, 1);

    /// <summary>Highest value first; ties share a rank (1, 2, 2, 4).</summary>
    static List<Entry> Rank(IEnumerable<(Member m, double value, double? best)> scores)
    {
        var sorted = scores.OrderByDescending(s => s.value).ThenBy(s => s.m.DisplayName).ToList();
        return sorted.Select((s, i) =>
        {
            var rank = sorted.FindIndex(x => x.value == s.value) + 1;
            return new Entry(rank, s.m.UserId, s.m.DisplayName, s.m.AvatarUrl, s.value, s.best);
        }).ToList();
    }
}
