using Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

static class TestDb
{
    /// <summary>A real SQLite database in memory, so queries run through the same provider as production.</summary>
    public static AppDb Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        db.Database.Migrate();
        return db;
    }

    public static User AddUser(this AppDb db, string name, string timeZone = "UTC")
    {
        var user = new User { SpotifyId = name.ToLowerInvariant(), DisplayName = name, TimeZone = timeZone };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static Play Play(DateTime playedAt, string artist, int minutes = 3) => new()
    {
        PlayedAt = Api.Data.Play.NormalizeTime(playedAt),
        TrackName = "Track",
        ArtistName = artist,
        ArtistKey = Api.Data.Play.KeyFor(artist),
        MsPlayed = minutes * 60_000,
    };
}
