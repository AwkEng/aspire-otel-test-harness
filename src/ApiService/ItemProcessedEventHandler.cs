using AspireOtelTestHarness.Messages;

namespace ApiService;

public static class ItemProcessedEventHandler
{
    public static void Handle(
        ItemProcessedEvent @event,
        ProcessingResultStore store,
        ILogger logger)
    {
        logger.LogInformation("Received processing result for item {ItemId}: {ItemName}",
            @event.ItemId, @event.ItemName);

        store.Store(@event);
    }
}
