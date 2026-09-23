using System.Security.Claims;
using System.Security.Cryptography;
using Api.Data;
using Api.Stats;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record CreateGroupRequest(string Name);
public record JoinGroupRequest(string Code);

public static class GroupEndpoints
{
    // No 0/O or 1/I, so codes are easy to read out loud.
    const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static void MapGroupEndpoints(this RouteGroupBuilder api)
    {
        var groups = api.MapGroup("/groups");

        groups.MapGet("", async (ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            var uid = me.UserId();
            return await db.GroupMembers.Where(m => m.UserId == uid)
                .Select(m => new { m.Group.Id, m.Group.Name, m.Group.InviteCode, MemberCount = m.Group.Members.Count })
                .ToListAsync(ct);
        });

        groups.MapPost("", async (CreateGroupRequest req, ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is 0 or > 50) return Results.BadRequest(new { detail = "Name must be 1-50 characters." });

            var group = new Group { Name = name, InviteCode = RandomNumberGenerator.GetString(CodeAlphabet, 8) };
            group.Members.Add(new GroupMember { UserId = me.UserId() });
            db.Groups.Add(group);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { group.Id, group.Name, group.InviteCode, MemberCount = 1 });
        });

        groups.MapPost("/join", async (JoinGroupRequest req, ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            var code = req.Code?.Trim().ToUpperInvariant() ?? "";
            var group = await db.Groups.SingleOrDefaultAsync(g => g.InviteCode == code, ct);
            if (group is null) return Results.NotFound(new { detail = "No group with that invite code." });

            var uid = me.UserId();
            if (!await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id && m.UserId == uid, ct))
            {
                db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = uid });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { group.Id });
        });

        groups.MapDelete("/{id:int}/members/me", async (int id, ClaimsPrincipal me, AppDb db, CancellationToken ct) =>
        {
            var uid = me.UserId();
            await db.GroupMembers.Where(m => m.GroupId == id && m.UserId == uid).ExecuteDeleteAsync(ct);
            // Nobody left: the group goes too.
            await db.Groups.Where(g => g.Id == id && !g.Members.Any()).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        // Everything below is visible to members only. Non-members get 404 so group ids don't leak.
        var group = groups.MapGroup("/{id:int}").AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var id = int.Parse((string)http.GetRouteValue("id")!);
            var db = http.RequestServices.GetRequiredService<AppDb>();
            var uid = http.User.UserId();
            return await db.GroupMembers.AnyAsync(m => m.GroupId == id && m.UserId == uid)
                ? await next(ctx)
                : Results.NotFound();
        });

        group.MapGet("", async (int id, AppDb db, CancellationToken ct) =>
            await db.Groups.Where(g => g.Id == id)
                .Select(g => new
                {
                    g.Id, g.Name, g.InviteCode,
                    Members = g.Members.Select(m => new { m.UserId, m.User.DisplayName, m.User.AvatarUrl }),
                })
                .SingleAsync(ct));

        group.MapGet("/leaderboard", async (int id, string? metric, string? period, AppDb db, CancellationToken ct) =>
            TryParse<Metric>(metric, Metric.Minutes, out var m) && TryParse<Period>(period, Period.Week, out var p)
                ? Results.Ok(await Leaderboards.RankAsync(db, id, m, p, DateTime.UtcNow, ct))
                : Results.BadRequest(new { detail = "Unknown metric or period." }));

        group.MapGet("/artists", async (int id, string? period, AppDb db, CancellationToken ct) =>
            TryParse<Period>(period, Period.Week, out var p)
                ? Results.Ok(await Leaderboards.TopArtistsAsync(db, id, p, DateTime.UtcNow, ct: ct))
                : Results.BadRequest(new { detail = "Unknown period." }));

        group.MapGet("/superfans", async (int id, string artist, string? period, AppDb db, CancellationToken ct) =>
            TryParse<Period>(period, Period.Week, out var p)
                ? Results.Ok(await Leaderboards.SuperfansAsync(db, id, artist, p, DateTime.UtcNow, ct))
                : Results.BadRequest(new { detail = "Unknown period." }));
    }

    static bool TryParse<T>(string? value, T fallback, out T result) where T : struct, Enum
    {
        result = fallback;
        return value is null || (Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result));
    }
}
