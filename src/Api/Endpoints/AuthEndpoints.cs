using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Api.Data;
using Api.Ingestion;
using Api.Spotify;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public static class AuthEndpoints
{
    const string StateCookie = "spotify_oauth_state";

    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/login", (HttpContext ctx, SpotifyClient spotify) =>
        {
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            ctx.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax, // must survive the top-level redirect back from Spotify
                MaxAge = TimeSpan.FromMinutes(10),
            });
            return Results.Redirect(spotify.AuthorizeUrl(state));
        });

        app.MapGet("/auth/callback", async (
            HttpContext ctx, string? code, string? state, string? error,
            SpotifyClient spotify, SpotifySync sync, AppDb db, IDataProtector protector,
            ILogger<SpotifyClient> log, CancellationToken ct) =>
        {
            var expected = ctx.Request.Cookies[StateCookie];
            ctx.Response.Cookies.Delete(StateCookie);
            if (error is not null || code is null || state is null || expected is null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state), Encoding.ASCII.GetBytes(expected)))
                return Results.Redirect("/?login=failed");

            var token = await spotify.ExchangeCodeAsync(code, ct);
            var profile = await spotify.GetProfileAsync(token.AccessToken, ct);

            var user = await db.Users.SingleOrDefaultAsync(u => u.SpotifyId == profile.Id, ct);
            if (user is null)
            {
                user = new User { SpotifyId = profile.Id, DisplayName = profile.Id };
                db.Users.Add(user);
            }
            user.DisplayName = profile.DisplayName ?? profile.Id;
            user.AvatarUrl = profile.Images?.FirstOrDefault()?.Url;
            user.EncryptedRefreshToken = protector.Protect(token.RefreshToken!);
            await db.SaveChangesAsync(ct);

            await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme)));

            // Pull recent plays right away so a new user isn't staring at an empty dashboard.
            try { await sync.SyncAsync(user.Id, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Initial sync failed for user {UserId}", user.Id); }

            return Results.Redirect("/");
        });

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.NoContent();
        });
    }
}
