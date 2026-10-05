using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace SubscriberClient;

public class SubscriberStorageProvider(IDbContextFactory<DbContext> factory) : IEventSubscriberDeliveryAck<EventRecord>
{
    public async ValueTask StoreEventAsync(EventRecord r, CancellationToken ct)
    {
        r.ExpireOn = DateTime.UtcNow.AddHours(24); //override default expiry time
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(r);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 } sqlite &&
                                           sqlite.Message.Contains("Events.TrackingID", StringComparison.Ordinal))
        {
            throw new DuplicateEventDeliveryException(r.TrackingID);
        }
    }

    public async ValueTask<IEnumerable<EventRecord>> GetNextBatchAsync(PendingRecordSearchParams<EventRecord> p)
    {
        await using var db = await factory.CreateDbContextAsync(p.CancellationToken);

        return await db.Events
                       .AsNoTracking()
                       .Where(p.Match)
                       .OrderBy(r => r.ID)
                       .Take(p.Limit)
                       .ToListAsync(p.CancellationToken);
    }

    public async ValueTask MarkEventAsCompleteAsync(EventRecord r, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Events
                .Where(e => e.ID == r.ID)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsComplete, true), ct);
    }

    public async ValueTask PurgeStaleRecordsAsync(StaleRecordSearchParams<EventRecord> p)
    {
        await using var db = await factory.CreateDbContextAsync(p.CancellationToken);
        await db.Events
                .Where(p.Match)
                .ExecuteDeleteAsync(p.CancellationToken);
    }
}