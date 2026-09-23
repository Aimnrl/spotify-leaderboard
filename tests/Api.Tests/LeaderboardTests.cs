using Api.Data;
using Api.Stats;
using static Api.Tests.TestDb;

namespace Api.Tests;

public class LeaderboardTests
{
    static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    // Ana and Ben share a group; Cy is an outsider whose plays must never count.
    readonly AppDb _db = Create();
    readonly User _ana, _ben, _cy;
    readonly int _groupId;

    public LeaderboardTests()
    {
        _ana = _db.AddUser("Ana");
        _ben = _db.AddUser("Ben");
        _cy = _db.AddUser("Cy");
        var group = new Group { Name = "Friends", InviteCode = "ABCD2345" };
        group.Members.Add(new GroupMember { UserId = _ana.Id });
        group.Members.Add(new GroupMember { UserId = _ben.Id });
        _db.Groups.Add(group);
        _db.SaveChanges();
        _groupId = group.Id;

        Seed(_ana,
            Play(Now.AddHours(-1), "Radiohead", 10),
            Play(Now.AddHours(-2), "Radiohead", 10),
            Play(Now.AddDays(-1), "Björk", 5),
            Play(Now.AddDays(-40), "Radiohead", 100)); // only in "year"/"all"
        Seed(_ben,
            Play(Now.AddHours(-1), "radiohead ", 30), // same artist, different spelling
            Play(Now.AddHours(-3), "Portishead", 1),
            Play(Now.AddHours(-4), "Massive Attack", 1),
            Play(Now.AddHours(-5), "Tricky", 1));
        Seed(_cy, Play(Now.AddHours(-1), "Radiohead", 999));
    }

    void Seed(User user, params Play[] plays) => _db.AddPlaysAsync(user.Id, plays).GetAwaiter().GetResult();

    [Fact]
    public async Task Minutes_ranks_members_by_listening_time_in_the_period()
    {
        var week = await Leaderboards.RankAsync(_db, _groupId, Metric.Minutes, Period.Week, Now);
        Assert.Equal([("Ben", 33.0), ("Ana", 25.0)], week.Select(e => (e.DisplayName, e.Value)));
        Assert.Equal([1, 2], week.Select(e => e.Rank));

        var all = await Leaderboards.RankAsync(_db, _groupId, Metric.Minutes, Period.All, Now);
        Assert.Equal([("Ana", 125.0), ("Ben", 33.0)], all.Select(e => (e.DisplayName, e.Value)));
    }

    [Fact]
    public async Task Diversity_counts_distinct_artists()
    {
        var entries = await Leaderboards.RankAsync(_db, _groupId, Metric.Diversity, Period.Week, Now);
        Assert.Equal([("Ben", 4.0), ("Ana", 2.0)], entries.Select(e => (e.DisplayName, e.Value)));
    }

    [Fact]
    public async Task Streak_reports_current_and_longest()
    {
        var entries = await Leaderboards.RankAsync(_db, _groupId, Metric.Streak, Period.Week, Now);
        var ana = entries.Single(e => e.DisplayName == "Ana");
        Assert.Equal(2, ana.Value); // today + yesterday
        Assert.Equal(2, ana.Best);
        Assert.Equal(1, entries.Single(e => e.DisplayName == "Ben").Value);
    }

    [Fact]
    public async Task Superfans_matches_artist_names_loosely_and_ignores_outsiders()
    {
        var fans = await Leaderboards.SuperfansAsync(_db, _groupId, "RADIOHEAD", Period.Week, Now);
        Assert.Equal([("Ben", 30.0), ("Ana", 20.0)], fans.Select(e => (e.DisplayName, e.Value)));
    }

    [Fact]
    public async Task Top_artists_name_the_number_one_fan()
    {
        var artists = await Leaderboards.TopArtistsAsync(_db, _groupId, Period.Week, Now);
        var top = artists[0];
        Assert.Equal(50.0, top.Minutes);
        Assert.Equal(2, top.Listeners);
        Assert.Equal("Ben", top.TopFan.DisplayName);
    }

    [Fact]
    public async Task Ties_share_a_rank()
    {
        _db.GroupMembers.Add(new GroupMember { GroupId = _groupId, UserId = _cy.Id });
        var dee = _db.AddUser("Dee");
        _db.GroupMembers.Add(new GroupMember { GroupId = _groupId, UserId = dee.Id });
        _db.SaveChanges();

        var entries = await Leaderboards.RankAsync(_db, _groupId, Metric.Minutes, Period.Week, Now);
        // Dee has no plays; everyone else has minutes, so Dee is last with 0.
        Assert.Equal(("Dee", 0.0, 4), (entries[^1].DisplayName, entries[^1].Value, entries[^1].Rank));

        Seed(dee, Play(Now.AddHours(-1), "X", 25));
        entries = await Leaderboards.RankAsync(_db, _groupId, Metric.Minutes, Period.Week, Now);
        Assert.Equal([1, 2, 3, 3], entries.Select(e => e.Rank)); // Ana and Dee both on 25
    }
}
