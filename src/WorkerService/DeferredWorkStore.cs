using System.Collections.Concurrent;
using AspireOtelTestHarness.Messages;

namespace WorkerService;

/// <summary>
/// In-memory store of commands waiting to be dispatched by the DeferredDispatcher.
/// The traceparent captured at enqueue time is stored alongside the command so the
/// dispatcher can restore or link back to the enqueue-time trace context.
/// </summary>
public sealed class DeferredWorkStore
{
    private readonly ConcurrentQueue<DeferredWorkEntry> _queue = new();

    public void Enqueue(ProcessItemCommand command, string? enqueueTraceParent)
    {
        _queue.Enqueue(new DeferredWorkEntry(command, enqueueTraceParent, DateTime.UtcNow));
    }

    public bool TryDequeue(out DeferredWorkEntry entry) => _queue.TryDequeue(out entry!);

    public int Count => _queue.Count;
}

public record DeferredWorkEntry(ProcessItemCommand Command, string? EnqueueTraceParent, DateTime EnqueuedAt);
