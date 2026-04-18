using System.Diagnostics;
using Wolverine;

namespace WorkerService;

/// <summary>
/// Background service that drains the DeferredWorkStore every tick and publishes the
/// stored commands through the message bus. Each tick runs under its own Activity from
/// the "DeferredDispatcher" source, creating a trace distinct from whatever enqueued
/// the entry — this is the shape that reproduces the trace-context mixing bug.
/// </summary>
public sealed class DeferredDispatcher(
    DeferredWorkStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<DeferredDispatcher> logger) : BackgroundService
{
    public static readonly ActivitySource ActivitySource = new("DeferredDispatcher");

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await TickAsync(stoppingToken);
            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (store.Count == 0)
            return;

        // IMessageBus is scoped — resolve inside a per-tick scope.
        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        while (store.TryDequeue(out var entry))
        {
            await PublishWithLink(bus, entry);
        }
    }

    /// <summary>
    /// Publish under a fresh Producer span that starts a new trace and carries an
    /// ActivityLink back to the enqueue-time trace. Wolverine captures this
    /// Producer span's traceparent on the outgoing envelope, so the receiving
    /// handler inherits the Producer's new trace (not whatever happened to be
    /// Activity.Current at dispatch time) and the link provides correlation back
    /// to whoever originally enqueued the work. Matches the OTel messaging
    /// semantic conventions: use span links (not parent-child) for producer↔
    /// consumer correlation in async/batch scenarios.
    /// </summary>
    private async Task PublishWithLink(IMessageBus bus, DeferredWorkEntry entry)
    {
        ActivityLink[] links = [];
        string? enqueueTraceId = null;
        if (entry.EnqueueTraceParent is not null
            && ActivityContext.TryParse(entry.EnqueueTraceParent, traceState: null, out var enqueuerContext))
        {
            links = [new ActivityLink(enqueuerContext)];
            enqueueTraceId = enqueuerContext.TraceId.ToHexString();
        }

        using var publishActivity = ActivitySource.StartActivity(
            "DeferredDispatcher.Publish",
            ActivityKind.Producer,
            default(ActivityContext),
            tags: null,
            links: links);

        // Correlation attributes: let tests locate the dispatched work by attribute
        // rather than trace id, so assertions stay stable if the correlation
        // strategy here ever changes.
        publishActivity?.SetTag("enqueue.trace_id", enqueueTraceId);
        publishActivity?.SetTag("deferred_work.item_id", entry.Command.ItemId.ToString());

        logger.LogInformation("Dispatching item {ItemId} (enqueueTraceParent={TraceParent}, newTrace={NewTrace})",
            entry.Command.ItemId, entry.EnqueueTraceParent, publishActivity?.TraceId.ToHexString());

        await bus.PublishAsync(entry.Command);
    }
}
