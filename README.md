# Acknowledged Event Queues With EFCore+SQLite Demo

Requires the .NET 10 SDK. Persistence uses EF Core and SQLite, with separate databases for the publisher and subscriber. FastEndpoints packages are pinned to `8.4.0-beta.19`.

## Run

Start the publisher, then the subscriber in separate terminals:

```sh
dotnet run --project Source/Server
dotnet run --project Source/Client
```

Open `http://localhost:5001/event/demo` to publish ten events. The subscriber logs each received event.

The apps create `PublisherEventStore.db` and `SubscriberEventStore.db` in their working directories on first startup. Override the location through `ConnectionStrings__EventStore`, for example:

```sh
ConnectionStrings__EventStore="Data Source=/path/to/publisher.db" dotnet run --project Source/Server
```

Event payloads are stored as JSON. Delivery acknowledgements are enabled on both providers. The hub marks an event complete after the subscriber stores it durably, and the subscriber executes its handler independently. A unique subscriber `TrackingID` index makes replayed deliveries safe.

Pending events are read in insertion order. Subscriber records expire for handler execution after 24 hours. Cleanup retains subscriber records, including completed ones, through `RetainUntil`, which the library sets from the hub event expiry plus its default five-minute clock-skew allowance.

The demo initializes its schema using `EnsureCreatedAsync`. Existing subscriber databases need an EF migration or recreation to add the nullable `RetainUntil` column and unique `TrackingID` index before running this version. For a disposable demo database, stop the subscriber and delete `SubscriberEventStore.db` so it is recreated at startup. Preserve production data with a migration and backfill retention to cover the hub replay window plus the clock-skew allowance before enabling cleanup. Future schema changes also require EF migrations or recreation of the demo database.

## Tests

```sh
dotnet test EventQueuesDemo.sln
```

Tests use isolated temporary SQLite databases and cover publishing, delivery, payload persistence, batch ordering, subscriber restoration, completion, and cleanup. `global.json` enables the Microsoft Testing Platform runner used by xUnit v4 on .NET 10.