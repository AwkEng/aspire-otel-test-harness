using System.Collections.Concurrent;
using AspireOtelTestHarness.Messages;

namespace ApiService;

/// <summary>
/// In-memory store for processed item results.
/// Populated by the ItemProcessedEventHandler when results arrive from the worker via RabbitMQ.
/// </summary>
public sealed class ProcessingResultStore
{
    private readonly ConcurrentDictionary<Guid, ItemProcessedEvent> _results = [];

    public void Store(ItemProcessedEvent result) => _results[result.ItemId] = result;

    public ItemProcessedEvent? Get(Guid itemId) => _results.GetValueOrDefault(itemId);
}
