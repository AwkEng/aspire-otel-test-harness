using AspireOtelTestHarness.Messages;

namespace WorkerService.Handlers;

public static class ProcessItemFailCommandHandler
{
    public static void Handle(
        ProcessItemFailCommand command,
        ILogger logger)
    {
        logger.LogInformation("Processing failing item {ItemId}: {ItemName}", command.ItemId, command.ItemName);

        throw new InvalidOperationException(
            $"Simulated processing failure for item {command.ItemId} ({command.ItemName})");
    }
}
