using Microsoft.EntityFrameworkCore;

namespace SubscriberClient;

public class DbContext(DbContextOptions<DbContext> options) : Microsoft.EntityFrameworkCore.DbContext(options)
{
    public DbSet<EventRecord> Events => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<EventRecord>();
        record.HasKey(r => r.ID);
        record.HasIndex(r => r.TrackingID).IsUnique();
        record.HasIndex(r => new { r.SubscriberID, r.EventType, r.IsComplete, r.ID });
        record.HasIndex(r => r.ExpireOn);
    }
}