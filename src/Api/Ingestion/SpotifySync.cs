using Api.Data;
using Api.Spotify;
using Microsoft.AspNetCore.DataProtection;

namespace Api.Ingestion;

/// <summary>Pulls one user's recently played tracks from Spotify into the Plays table.</summary>
public class SpotifySync(AppDb db, SpotifyClient spotify, IDataProtector protector)
{
    /// <returns>Number of new plays stored.</returns>
    public async Task<int> SyncAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException($"User {userId} not found.");
        if (user.EncryptedRefreshToken is null) throw new SpotifyReauthRequiredException();

        TokenResponse token;
        try
        {
            token = await spotify.RefreshAsync(protector.Unprotect(user.EncryptedRefreshToken), ct);
        }
        catch (SpotifyReauthRequiredException)
        {
            user.EncryptedRefreshToken = null;
            await db.SaveChangesAsync(ct);
            throw;
        }
        // Spotify may rotate the refresh token.
        if (token.RefreshToken is not null) user.EncryptedRefreshToken = protector.Protect(token.RefreshToken);

        var items = await spotify.GetRecentlyPlayedAsync(token.AccessToken, ct);
        var plays = items
            .Where(i => i.Track.Artists.Count > 0)
            .Select(i => new Play
            {
                PlayedAt = Play.NormalizeTime(i.PlayedAt.ToUniversalTime()),
                TrackId = i.Track.Id,
                TrackName = i.Track.Name,
                ArtistName = i.Track.Artists[0].Name,
                ArtistKey = Play.KeyFor(i.Track.Artists[0].Name),
                // The API doesn't say how much of the track was heard, so count the full duration.
                MsPlayed = i.Track.DurationMs,
                Source = PlaySource.Api,
            })
            .ToList();

        var added = await db.AddPlaysAsync(user.Id, plays, ct);
        user.LastPolledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return added;
    }
}
