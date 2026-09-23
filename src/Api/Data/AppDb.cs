using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class User
{
    public int Id { get; set; }
    public required string SpotifyId { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    /// <summary>Data Protection-encrypted. Null means Spotify access was revoked and the user must sign in again.</summary>
    public string? EncryptedRefreshToken { get; set; }
    /// <summary>IANA or Windows time zone id, used to decide which calendar day a play belongs to.</summary>
    public string TimeZone { get; set; } = "UTC";
    public DateTime? LastPolledAt { get; set; }
}

public class Group
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string InviteCode { get; set; }
    public List<GroupMember> Members { get; set; } = [];
}

public class GroupMember
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public enum PlaySource { Api, Export }

public class Play
{
    public long Id { get; set; }
    public int UserId { get; set; }
    /// <summary>UTC, truncated to whole seconds so API plays and export rows line up for de-duplication.</summary>
    public DateTime PlayedAt { get; set; }
    public string? TrackId { get; set; }
    public required string TrackName { get; set; }
    public required string ArtistName { get; set; }
    /// <summary>Normalized artist name. The export has no artist ids, so names are the only key both sources share.</summary>
    public required string ArtistKey { get; set; }
    public int MsPlayed { get; set; }
    public PlaySource Source { get; set; }

    public static string KeyFor(string artistName) => artistName.Trim().ToLowerInvariant();

    public static DateTime NormalizeTime(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Play> Plays => Set<Play>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.SpotifyId).IsUnique();
        b.Entity<Group>().HasIndex(g => g.InviteCode).IsUnique();
        b.Entity<GroupMember>().HasKey(m => new { m.GroupId, m.UserId });
        b.Entity<Play>().HasIndex(p => new { p.UserId, p.PlayedAt }).IsUnique();
        b.Entity<Play>().HasIndex(p => p.ArtistKey);
        b.Entity<Play>().HasOne<User>().WithMany().HasForeignKey(p => p.UserId);
    }

    /// <summary>Inserts plays the user doesn't already have (same PlayedAt). Returns how many were added.</summary>
    public async Task<int> AddPlaysAsync(int userId, IReadOnlyCollection<Play> plays, CancellationToken ct = default)
    {
        if (plays.Count == 0) return 0;
        var min = plays.Min(p => p.PlayedAt);
        var max = plays.Max(p => p.PlayedAt);
        var seen = (await Plays
            .Where(p => p.UserId == userId && p.PlayedAt >= min && p.PlayedAt <= max)
            .Select(p => p.PlayedAt)
            .ToListAsync(ct)).ToHashSet();

        var fresh = plays.Where(p => seen.Add(p.PlayedAt)).ToList();
        foreach (var p in fresh) p.UserId = userId;
        Plays.AddRange(fresh);
        await SaveChangesAsync(ct);
        return fresh.Count;
    }
}
