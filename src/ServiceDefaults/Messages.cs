namespace AspireOtelTestHarness.Messages;

public record ProcessItemCommand(Guid ItemId, string ItemName);

public record ItemProcessedEvent(Guid ItemId, string ItemName, DateTime ProcessedAt);

/// <summary>
/// Command that always fails — used to test exception visibility in OTel traces and logs.
/// </summary>
public record ProcessItemFailCommand(Guid ItemId, string ItemName);

/// <summary>
/// Enqueues a ProcessItemCommand for deferred dispatch by the DeferredDispatcher.
/// Demonstrates a common trace-context mixing pattern: enqueue happens under one
/// trace (e.g. an HTTP request), dispatch happens later under a background
/// timer's own trace, and naive instrumentation loses the correlation.
/// </summary>
public record ScheduleItemCommand(Guid ItemId, string ItemName);
