using System.IO.Compression;
using System.Text;
using Api.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

public class ExportImporterTests
{
    const string Extended = """
        [
          {"ts":"2024-03-01T10:00:00Z","ms_played":200000,"master_metadata_track_name":"Song A","master_metadata_album_artist_name":"Artist One","spotify_track_uri":"spotify:track:abc"},
          {"ts":"2024-03-01T10:05:00Z","ms_played":5000,"master_metadata_track_name":"Skipped","master_metadata_album_artist_name":"Artist One","spotify_track_uri":"spotify:track:def"},
          {"ts":"2024-03-01T11:00:00Z","ms_played":1800000,"master_metadata_track_name":null,"master_metadata_album_artist_name":null,"episode_name":"A podcast"},
          {"ts":"2024-03-01T12:00:00Z","ms_played":180000,"master_metadata_track_name":"Song B","master_metadata_album_artist_name":"Artist Two","spotify_track_uri":"spotify:track:ghi"}
        ]
        """;

    const string AccountData = """
        [
          {"endTime":"2024-03-02 09:30","artistName":"Artist Three","trackName":"Song C","msPlayed":240000}
        ]
        """;

    static Stream Utf8(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Imports_real_plays_and_skips_short_plays_and_podcasts()
    {
        using var db = TestDb.Create();
        var user = db.AddUser("Ana");

        var result = await ExportImporter.ImportAsync(db, user.Id, Utf8(Extended), "Streaming_History_Audio_2024.json");

        Assert.Equal(new ImportResult(2, 2), result);
        var plays = await db.Plays.OrderBy(p => p.PlayedAt).ToListAsync();
        Assert.Equal(["Song A", "Song B"], plays.Select(p => p.TrackName));
        Assert.Equal("abc", plays[0].TrackId);
        Assert.Equal("artist one", plays[0].ArtistKey);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_adds_nothing()
    {
        using var db = TestDb.Create();
        var user = db.AddUser("Ana");

        await ExportImporter.ImportAsync(db, user.Id, Utf8(Extended), "a.json");
        var again = await ExportImporter.ImportAsync(db, user.Id, Utf8(Extended), "a.json");

        Assert.Equal(0, again.Imported);
        Assert.Equal(2, await db.Plays.CountAsync());
    }

    [Fact]
    public async Task Reads_history_files_from_a_zip_in_both_formats_and_ignores_the_rest()
    {
        using var db = TestDb.Create();
        var user = db.AddUser("Ana");

        var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(content);
            }
            Add("Spotify Extended Streaming History/Streaming_History_Audio_2024.json", Extended);
            Add("MyData/StreamingHistory_music_0.json", AccountData);
            Add("MyData/Userdata.json", """{"username":"ana"}""");
        }
        zipBytes.Position = 0;

        var result = await ExportImporter.ImportAsync(db, user.Id, zipBytes, "my_spotify_data.zip");

        Assert.Equal(3, result.Imported);
        var c = await db.Plays.SingleAsync(p => p.TrackName == "Song C");
        Assert.Equal(new DateTime(2024, 3, 2, 9, 30, 0), c.PlayedAt);
    }
}
