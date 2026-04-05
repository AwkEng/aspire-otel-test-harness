namespace AspireOtelTestHarness.Messages;

public record ProcessItemCommand(Guid ItemId, string ItemName);

public record ItemProcessedEvent(Guid ItemId, string ItemName, DateTime ProcessedAt);
