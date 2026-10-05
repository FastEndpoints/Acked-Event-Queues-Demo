using FastEndpoints;

namespace SubscriberClient;

sealed class ErrorReceiver : SubscriberExceptionReceiver
{
    readonly SubscriberStorageProvider _storage;

    public ErrorReceiver(Microsoft.EntityFrameworkCore.IDbContextFactory<DbContext> factory, ILogger<ErrorReceiver> logger)
    {
        _storage = new(factory);
        logger.LogInformation("Subscriber Error Receiver Initialized!");
    }

    public override async Task OnMarkEventAsCompleteError<TEvent>(IEventStorageRecord record, int attemptCount, Exception exception, CancellationToken ct)
        => await _storage.MarkEventAsCompleteAsync((EventRecord)record, ct);
}