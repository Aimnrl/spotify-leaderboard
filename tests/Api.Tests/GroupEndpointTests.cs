using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Boots the real app (routing, auth, membership filter) with a header-based test login.</summary>
public class GroupEndpointTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lb-test-{Guid.NewGuid():N}.db");
    readonly WebApplicationFactory<Program> _app;
    readonly int _ana, _ben, _cy;

    public GroupEndpointTests()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Db", $"Data Source={_dbPath};Pooling=False");
            b.UseSetting("Spotify:ClientId", "test");
            b.UseSetting("Spotify:ClientSecret", "test");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IHostedService>(); // no Spotify polling in tests
                s.AddAuthentication(o => o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = HeaderAuth.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuth>(HeaderAuth.SchemeName, null);
            });
        });

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        (_ana, _ben, _cy) = (db.AddUser("Ana").Id, db.AddUser("Ben").Id, db.AddUser("Cy").Id);
    }

    HttpClient As(int? userId)
    {
        var client = _app.CreateClient();
        if (userId is { } id) client.DefaultRequestHeaders.Add(HeaderAuth.Header, id.ToString());
        return client;
    }

    record GroupDto(int Id, string Name, string InviteCode, int MemberCount);
    record DetailDto(int Id, string Name, List<object> Members);

    [Fact]
    public async Task Create_join_rank_and_leave()
    {
        var created = await (await As(_ana).PostAsJsonAsync("/api/groups", new { name = "  Crew  " }))
            .Content.ReadFromJsonAsync<GroupDto>();
        Assert.Equal("Crew", created!.Name);

        var join = await As(_ben).PostAsJsonAsync("/api/groups/join", new { code = created.InviteCode.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);

        var detail = await As(_ben).GetFromJsonAsync<DetailDto>($"/api/groups/{created.Id}");
        Assert.Equal(2, detail!.Members.Count);

        var board = await As(_ben).GetFromJsonAsync<List<object>>($"/api/groups/{created.Id}/leaderboard?metric=Streak&period=all");
        Assert.Equal(2, board!.Count);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await As(_ben).GetAsync($"/api/groups/{created.Id}/leaderboard?metric=loudness")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await As(_ben).GetAsync($"/api/groups/{created.Id}/superfans?artist=Radiohead")).StatusCode);

        await As(_ana).DeleteAsync($"/api/groups/{created.Id}/members/me");
        await As(_ben).DeleteAsync($"/api/groups/{created.Id}/members/me");
        Assert.Empty((await As(_ben).GetFromJsonAsync<List<GroupDto>>("/api/groups"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await As(_ana).GetAsync($"/api/groups/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Outsiders_and_anonymous_users_are_kept_out()
    {
        var created = await (await As(_ana).PostAsJsonAsync("/api/groups", new { name = "Private" }))
            .Content.ReadFromJsonAsync<GroupDto>();

        foreach (var path in new[] { "", "/leaderboard", "/artists", "/superfans?artist=x" })
            Assert.Equal(HttpStatusCode.NotFound, (await As(_cy).GetAsync($"/api/groups/{created!.Id}{path}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await As(null).GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await As(_cy).PostAsJsonAsync("/api/groups/join", new { code = "NOPE0000" })).StatusCode);
    }

    [Fact]
    public async Task Rejects_bad_input()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await As(_ana).PostAsJsonAsync("/api/groups", new { name = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(_ana).PutAsJsonAsync("/api/me/timezone", new { timeZone = "Mars/Olympus" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(_ana).PutAsJsonAsync("/api/me/timezone", new { timeZone = "Europe/Berlin" })).StatusCode);
    }

    public void Dispose()
    {
        _app.Dispose();
        File.Delete(_dbPath);
    }

    class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        public const string SchemeName = "Test", Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.TryGetValue(Header, out var id)
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id!)], SchemeName)), SchemeName))
                : AuthenticateResult.NoResult());
    }
}
