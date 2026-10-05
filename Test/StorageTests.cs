using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HubRecord = PublisherServer.EventRecord;
using SubscriberRecord = SubscriberClient.EventRecord;

namespace Test;

public class StorageTests
{
    [Fact]
    public async Task Publisher_Restores_Orders_Completes_And_Purges_Persisted_Events()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using var services = new ServiceCollection()
                                       .AddDbContextFactory<PublisherServer.DbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"))
                                       .BuildServiceProvider();
            var factory = services.GetRequiredService<IDbContextFactory<PublisherServer.DbContext>>();
            await using (var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var storage = new PublisherServer.HubStorageProvider(factory);
            var records = Enumerable.Range(1, 3).Select(
                i =>
                {
                    var r = new HubRecord
                    {
                        SubscriberID = "subscriber", EventType = typeof(SomethingHappened).FullName!, TrackingID = Guid.NewGuid(),
                        ExpireOn = DateTime.UtcNow.AddHours(i == 3 ? -1 : 1)
                    };
                    r.SetEvent(new SomethingHappened { Id = i, Description = "persisted" });

                    return r;
                }).ToArray();
            await storage.StoreEventsAsync(records, TestContext.Current.CancellationToken);
            var search = Parameters<PendingRecordSearchParams<HubRecord>>(
                ("Match", (Expression<Func<HubRecord, bool>>)(r => r.SubscriberID == "subscriber" && !r.IsComplete && r.ExpireOn > DateTime.UtcNow)),
                ("Limit", 1));
            var batch = (await storage.GetNextBatchAsync(search)).ToArray();
            Assert.Single(batch);
            Assert.Equal(1, batch[0].GetEvent<SomethingHappened>().Id);
            Assert.Equal(records[0].TrackingID, batch[0].TrackingID);
            await storage.MarkEventAsCompleteAsync(batch[0], TestContext.Current.CancellationToken);
            Assert.Equal(2, (await storage.GetNextBatchAsync(search)).Single().GetEvent<SomethingHappened>().Id);
            var restore = Parameters<SubscriberIDRestorationParams<HubRecord>>(
                ("Match", (Expression<Func<HubRecord, bool>>)(r => r.EventType == typeof(SomethingHappened).FullName)),
                ("Projection", (Expression<Func<HubRecord, string>>)(r => r.SubscriberID)));
            Assert.Equal(new[] { "subscriber" }, await storage.RestoreSubscriberIDsForEventTypeAsync(restore));
            var stale = Parameters<StaleRecordSearchParams<HubRecord>>(("Match", (Expression<Func<HubRecord, bool>>)(r => r.IsComplete || r.ExpireOn <= DateTime.UtcNow)));
            await storage.PurgeStaleRecordsAsync(stale);
            await using var check = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, (await check.Events.SingleAsync(TestContext.Current.CancellationToken)).GetEvent<SomethingHappened>().Id);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Subscriber_Persists_Payload_And_Completes_And_Purges_Record()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using var services = new ServiceCollection()
                                       .AddDbContextFactory<SubscriberClient.DbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"))
                                       .BuildServiceProvider();
            var factory = services.GetRequiredService<IDbContextFactory<SubscriberClient.DbContext>>();
            await using (var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var storage = new SubscriberClient.SubscriberStorageProvider(factory);
            var record = new SubscriberRecord
                { SubscriberID = "subscriber", EventType = typeof(SomethingHappened).FullName!, TrackingID = Guid.NewGuid(), RetainUntil = DateTime.UtcNow.AddDays(2) };
            record.SetEvent(new SomethingHappened { Id = 42, Description = "persisted" });
            await storage.StoreEventAsync(record, TestContext.Current.CancellationToken);
            Assert.InRange(record.ExpireOn, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
            var search = Parameters<PendingRecordSearchParams<SubscriberRecord>>(
                ("Match", (Expression<Func<SubscriberRecord, bool>>)(r => !r.IsComplete && r.ExpireOn > DateTime.UtcNow)),
                ("Limit", 10));
            var fetched = (await storage.GetNextBatchAsync(search)).Single();
            Assert.Equal("persisted", fetched.GetEvent<SomethingHappened>().Description);
            Assert.Equal(record.RetainUntil, fetched.RetainUntil);
            await storage.MarkEventAsCompleteAsync(fetched, TestContext.Current.CancellationToken);
            Assert.Empty(await storage.GetNextBatchAsync(search));
            var stale = Parameters<StaleRecordSearchParams<SubscriberRecord>>(
                ("Match",
                 (Expression<Func<SubscriberRecord, bool>>)(r => (r.IsComplete || r.ExpireOn <= DateTime.UtcNow) && (r.RetainUntil == null || r.RetainUntil <= DateTime.UtcNow))));
            await storage.PurgeStaleRecordsAsync(stale);
            await using var check = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            Assert.Single(await check.Events.ToListAsync(TestContext.Current.CancellationToken));
            var duplicate = new SubscriberRecord { SubscriberID = "subscriber", EventType = record.EventType, TrackingID = record.TrackingID };
            duplicate.SetEvent(new SomethingHappened { Id = 99, Description = "duplicate" });
            await Assert.ThrowsAsync<DuplicateEventDeliveryException>(() => storage.StoreEventAsync(duplicate, TestContext.Current.CancellationToken).AsTask());
            var retained = await check.Events.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            Assert.True(retained.IsComplete);
            Assert.Equal("persisted", retained.GetEvent<SomethingHappened>().Description);
            Assert.Equal(record.RetainUntil, retained.RetainUntil);
            await check.Events.ExecuteUpdateAsync(s => s.SetProperty(r => r.RetainUntil, DateTime.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);
            await storage.PurgeStaleRecordsAsync(stale);
            Assert.Empty(await check.Events.ToListAsync(TestContext.Current.CancellationToken));
        }
        finally { File.Delete(path); }
    }

    // FastEndpoints supplies these parameters through internal setters.
    static T Parameters<T>(params (string Name, object Value)[] values) where T : struct
    {
        object result = new T();
        typeof(T).GetProperty("CancellationToken")!.SetValue(result, TestContext.Current.CancellationToken);
        foreach (var (name, value) in values)
            typeof(T).GetProperty(name)!.SetValue(result, value);

        return (T)result;
    }
}