using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Data;

namespace Api.Ingestion;

/// <summary>One row from either Spotify export format.</summary>
public class ExportRow
{
    // "Extended streaming history" (Streaming_History_Audio_*.json)
    [JsonPropertyName("ts")] public string? Ts { get; set; }
    [JsonPropertyName("ms_played")] public int? MsPlayedExtended { get; set; }
    [JsonPropertyName("master_metadata_track_name")] public string? TrackNameExtended { get; set; }
    [JsonPropertyName("master_metadata_album_artist_name")] public string? ArtistNameExtended { get; set; }
    [JsonPropertyName("spotify_track_uri")] public string? TrackUri { get; set; }

    // "Account data" export (StreamingHistory_music_*.json / StreamingHistory*.json)
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("msPlayed")] public int? MsPlayed { get; set; }
    [JsonPropertyName("trackName")] public string? TrackName { get; set; }
    [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
}

public record ImportResult(int Imported, int Skipped)
{
    public static ImportResult operator +(ImportResult a, ImportResult b) => new(a.Imported + b.Imported, a.Skipped + b.Skipped);
}

public static class ExportImporter
{
    const int MinMsPlayed = 30_000; // Spotify's own threshold for counting a stream
    const int BatchSize = 1_000;

    /// <summary>Imports a single history .json file, or the export .zip containing them.</summary>
    public static async Task<ImportResult> ImportAsync(AppDb db, int userId, Stream file, string fileName, CancellationToken ct = default)
    {
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return await ImportJsonAsync(db, userId, file, ct);

        var total = new ImportResult(0, 0);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.Where(e => IsHistoryFile(e.Name)))
        {
            await using var s = entry.Open();
            total += await ImportJsonAsync(db, userId, s, ct);
        }
        return total;
    }

    static bool IsHistoryFile(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        (name.StartsWith("Streaming_History_Audio", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("StreamingHistory", StringComparison.OrdinalIgnoreCase));

    static async Task<ImportResult> ImportJsonAsync(AppDb db, int userId, Stream json, CancellationToken ct)
    {
        int rows = 0, imported = 0;
        var batch = new List<Play>(BatchSize);

        async Task Flush()
        {
            imported += await db.AddPlaysAsync(userId, batch, ct);
            batch.Clear();
            db.ChangeTracker.Clear(); // keep memory flat on multi-year exports
        }

        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<ExportRow>(json, cancellationToken: ct))
        {
            rows++;
            if (row is not null && ToPlay(row) is { } play) batch.Add(play);
            if (batch.Count >= BatchSize) await Flush();
        }
        await Flush();
        return new ImportResult(imported, rows - imported);
    }

    /// <summary>Null for rows that shouldn't count: podcasts/videos (no track) and plays under 30 s.</summary>
    static Play? ToPlay(ExportRow row)
    {
        var time = row.Ts ?? row.EndTime;
        var track = row.TrackNameExtended ?? row.TrackName;
        var artist = row.ArtistNameExtended ?? row.ArtistName;
        var ms = row.MsPlayedExtended ?? row.MsPlayed ?? 0;
        if (time is null || string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(artist) || ms < MinMsPlayed)
            return null;
        if (!DateTime.TryParse(time, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var playedAt))
            return null;

        // The older export only has minute precision, so two long plays ending in the same minute collapse into one.
        return new Play
        {
            PlayedAt = Play.NormalizeTime(playedAt),
            TrackId = row.TrackUri?.StartsWith("spotify:track:") == true ? row.TrackUri["spotify:track:".Length..] : null,
            TrackName = track,
            ArtistName = artist,
            ArtistKey = Play.KeyFor(artist),
            MsPlayed = ms,
            Source = PlaySource.Export,
        };
    }
}
