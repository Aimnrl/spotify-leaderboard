using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Ingestion;

/// <summary>Every 30 minutes, syncs every connected user. Spotify only keeps the last 50 plays,
/// so anyone playing more than 50 tracks between polls loses the oldest ones.</summary>
public class PollingService(IServiceScopeFactory scopes, ILogger<PollingService> log) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do await PollAllAsync(ct);
        while (await timer.WaitForNextTickAsync(ct));
    }

    async Task PollAllAsync(CancellationToken ct)
    {
        List<int> userIds;
        using (var scope = scopes.CreateScope())
        {
            userIds = await scope.ServiceProvider.GetRequiredService<AppDb>().Users
                .Where(u => u.EncryptedRefreshToken != null)
                .Select(u => u.Id)
                .ToListAsync(ct);
        }

        foreach (var id in userIds)
        {
            // A fresh scope per user so one failure can't leave a DbContext in a bad state for the rest.
            using var scope = scopes.CreateScope();
            try
            {
                var added = await scope.ServiceProvider.GetRequiredService<SpotifySync>().SyncAsync(id, ct);
                log.LogInformation("Polled user {UserId}: {Added} new plays", id, added);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Polling failed for user {UserId}", id);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct); // stay well under Spotify's rate limit
        }
    }
}
