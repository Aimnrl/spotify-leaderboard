using System.Security.Claims;
using Api.Data;
using Api.Ingestion;
using Api.Spotify;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record TimeZoneRequest(string TimeZone);

public static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            var id = me.UserId();
            var user = await db.Users.Where(u => u.Id == id)
                .Select(u => new
                {
                    u.Id, u.DisplayName, u.AvatarUrl, u.TimeZone, u.LastPolledAt,
                    NeedsReauth = u.EncryptedRefreshToken == null,
                    PlayCount = db.Plays.Count(p => p.UserId == id),
                })
                .SingleOrDefaultAsync(ct);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });

        api.MapPut("/me/timezone", async (TimeZoneRequest req, ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(req.TimeZone, out _))
                return Results.BadRequest(new { detail = "Unknown time zone." });
            await db.Users.Where(u => u.Id == me.UserId())
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TimeZone, req.TimeZone), ct);
            return Results.NoContent();
        });

        api.MapPost("/me/sync", async (ClaimsPrincipal me, SpotifySync sync, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(new { added = await sync.SyncAsync(me.UserId(), ct) });
            }
            catch (SpotifyReauthRequiredException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        // Reads the form by hand: CSRF protection comes from the SameSite=Lax session cookie.
        api.MapPost("/imports", async (HttpRequest req, ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { detail = "Expected a multipart upload." });
            var form = await req.ReadFormAsync(ct);
            if (form.Files.Count == 0) return Results.BadRequest(new { detail = "No files uploaded." });

            var total = new ImportResult(0, 0);
            foreach (var file in form.Files)
            {
                await using var stream = file.OpenReadStream();
                try
                {
                    total += await ExportImporter.ImportAsync(db, me.UserId(), stream, file.FileName, ct);
                }
                catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
                {
                    return Results.BadRequest(new { detail = $"{file.FileName} isn't a Spotify streaming history file." });
                }
            }
            return Results.Ok(total);
        });
    }
}
