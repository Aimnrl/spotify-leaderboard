using Api.Data;
using Api.Endpoints;
using Api.Ingestion;
using Api.Spotify;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Room for a multi-year Spotify export zip.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 50 * 1024 * 1024);

builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Db") ?? "Data Source=leaderboard.db"));

builder.Services.AddOptions<SpotifyOptions>()
    .Bind(builder.Configuration.GetSection("Spotify"))
    .Validate(o => o.ClientId != "" && o.ClientSecret != "" && o.RedirectUri != "",
        "Set Spotify:ClientId, Spotify:ClientSecret and Spotify:RedirectUri (see README).")
    .ValidateOnStart();
builder.Services.AddHttpClient<SpotifyClient>();
builder.Services.AddScoped<SpotifySync>();
builder.Services.AddHostedService<PollingService>();

// Encrypts refresh tokens at rest. In production, persist the key ring somewhere durable
// (e.g. PersistKeysToFileSystem) or every stored token becomes unreadable on redeploy.
builder.Services.AddDataProtection();
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Spotify.RefreshToken"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "lb_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax; // blocks cross-site POSTs, i.e. CSRF
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        // It's an API: answer 401 instead of redirecting to a login page.
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.Migrate();

app.UseDefaultFiles();
app.UseStaticFiles(); // the built React app (client/ builds into wwwroot)
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapMeEndpoints();
api.MapGroupEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

// Lets the integration tests boot the app with WebApplicationFactory<Program>.
public partial class Program;
