namespace AspireOtelTestHarness.Messages;

public record ProcessItemCommand(Guid ItemId, string ItemName);

public record ItemProcessedEvent(Guid ItemId, string ItemName, DateTime ProcessedAt);

/// <summary>
/// Command that always fails — used to test exception visibility in OTel traces and logs.
/// </summary>
public record ProcessItemFailCommand(Guid ItemId, string ItemName);
