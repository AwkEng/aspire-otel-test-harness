using AspireOtelTestHarness.Messages;

namespace WorkerService.Handlers;

public static class ProcessItemCommandHandler
{
    public static ItemProcessedEvent Handle(
        ProcessItemCommand command,
        ILogger logger)
    {
        logger.LogInformation("Processing item {ItemId}: {ItemName}", command.ItemId, command.ItemName);

        // Return value is cascaded — Wolverine publishes it via the routing config
        return new ItemProcessedEvent(command.ItemId, command.ItemName, DateTime.UtcNow);
    }
}
