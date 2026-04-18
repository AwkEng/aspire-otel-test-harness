namespace AspireOtelTestHarness.Messages;

public record ProcessItemCommand(Guid ItemId, string ItemName);

public record ItemProcessedEvent(Guid ItemId, string ItemName, DateTime ProcessedAt);

/// <summary>
/// Command that always fails — used to test exception visibility in OTel traces and logs.
/// </summary>
public record ProcessItemFailCommand(Guid ItemId, string ItemName);

/// <summary>
/// Enqueues a ProcessItemCommand for deferred dispatch by the DeferredDispatcher.
/// Used to reproduce the trace-context mixing bug (issue SpectrumPlanner#155):
/// enqueue happens under the HTTP request's trace, dispatch happens later under
/// the dispatcher tick's own trace.
/// </summary>
public record ScheduleItemCommand(Guid ItemId, string ItemName);
