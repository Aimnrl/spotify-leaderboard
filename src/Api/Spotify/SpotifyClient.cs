using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Api.Spotify;

public class SpotifyOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}

/// <summary>The refresh token was revoked or expired; the user has to sign in again.</summary>
public class SpotifyReauthRequiredException() : Exception("Spotify access was revoked. Please sign in again.");

public record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);
public record SpotifyImage(string Url);
public record SpotifyProfile(string Id, string? DisplayName, List<SpotifyImage>? Images);
public record SpotifyArtist(string? Id, string Name);
public record SpotifyTrack(string? Id, string Name, int DurationMs, List<SpotifyArtist> Artists);
public record RecentItem(SpotifyTrack Track, DateTime PlayedAt);
record RecentlyPlayed(List<RecentItem> Items);

public class SpotifyClient(HttpClient http, IOptions<SpotifyOptions> options)
{
    const string Scopes = "user-read-recently-played";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    readonly SpotifyOptions _o = options.Value;

    public string AuthorizeUrl(string state) =>
        "https://accounts.spotify.com/authorize?" + string.Join('&',
            $"client_id={Uri.EscapeDataString(_o.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(_o.RedirectUri)}",
            $"scope={Uri.EscapeDataString(Scopes)}",
            $"state={state}");

    public Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken ct) =>
        TokenAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = _o.RedirectUri }, ct);

    public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    public Task<SpotifyProfile> GetProfileAsync(string accessToken, CancellationToken ct) =>
        GetAsync<SpotifyProfile>("https://api.spotify.com/v1/me", accessToken, ct);

    /// <summary>Spotify only exposes the last 50 plays, so this has to be polled regularly.</summary>
    public async Task<List<RecentItem>> GetRecentlyPlayedAsync(string accessToken, CancellationToken ct) =>
        (await GetAsync<RecentlyPlayed>("https://api.spotify.com/v1/me/player/recently-played?limit=50", accessToken, ct)).Items;

    async Task<TokenResponse> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_o.ClientId}:{_o.ClientSecret}")));

        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode == HttpStatusCode.BadRequest && body.Contains("invalid_grant"))
            throw new SpotifyReauthRequiredException();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Spotify token request failed ({(int)res.StatusCode}): {body}");
        return JsonSerializer.Deserialize<TokenResponse>(body, Json)!;
    }

    async Task<T> GetAsync<T>(string url, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new SpotifyReauthRequiredException();
        // A 429 just fails this sync; the next poll 30 minutes later is the retry.
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException($"Spotify rate limit hit, retry after {res.Headers.RetryAfter?.Delta}.");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }
}
