using Contracts;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SubscriberClient;

var bld = WebApplication.CreateBuilder(args);
bld.Services
   .AddDbContextFactory<SubscriberClient.DbContext>(o => o.UseSqlite(bld.Configuration.GetConnectionString("EventStore") ?? "Data Source=SubscriberEventStore.db"))
   .AddEventSubscriberStorageProvider<EventRecord, SubscriberStorageProvider>();

//bld.Services.AddSubscriberExceptionReceiver<ErrorReceiver>();

var app = bld.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<SubscriberClient.DbContext>>().CreateDbContextAsync())
    await db.Database.EnsureCreatedAsync();

app.MapRemote(
    "http://localhost:6000",
    c =>
    {
        c.Subscribe<SomethingHappened, WhenSomethingHappens>();
    });

app.Run();