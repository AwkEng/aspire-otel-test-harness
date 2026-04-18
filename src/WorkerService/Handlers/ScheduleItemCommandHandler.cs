using System.Diagnostics;
using AspireOtelTestHarness.Messages;

namespace WorkerService.Handlers;

/// <summary>
/// Receives a ScheduleItemCommand and stores a ProcessItemCommand in the DeferredWorkStore
/// for later dispatch. Captures Activity.Current.Id as the enqueue-time traceparent so
/// the dispatcher can correlate back to the originating request.
/// </summary>
public static class ScheduleItemCommandHandler
{
    public static void Handle(
        ScheduleItemCommand command,
        DeferredWorkStore store,
        ILogger logger)
    {
        var enqueueTraceParent = Activity.Current?.Id;

        logger.LogInformation("Scheduling item {ItemId} for deferred dispatch (traceparent={TraceParent})",
            command.ItemId, enqueueTraceParent);

        store.Enqueue(
            new ProcessItemCommand(command.ItemId, command.ItemName),
            enqueueTraceParent);
    }
}
