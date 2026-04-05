using System.Collections.Concurrent;
using Aspire.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Tracks resource state changes and logs the final state of all resources on shutdown.
/// Useful for diagnosing resource crashes or unhealthy exits in test failures.
/// </summary>
internal sealed class FinalStateLoggerService(
    DistributedApplicationModel appModel,
    ResourceNotificationService notifications,
    ILogger<FinalStateLoggerService> logger) : IHostedLifecycleService, IDisposable
{
    private readonly ConcurrentDictionary<string, ResourceState> _lastKnownState = [];
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private bool _hasLogged;

    public Task StartingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken ct)
    {
        _cts = new CancellationTokenSource();
        _watchTask = TrackResourceStates(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken ct)
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

        if (_watchTask is not null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
    }

    public Task StoppingAsync(CancellationToken ct)
    {
        LogFinalState();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        LogFinalState();
    }

    private async Task TrackResourceStates(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in notifications.WatchAsync(ct))
            {
                _lastKnownState[evt.Resource.Name] = new ResourceState
                {
                    ResourceType = evt.Resource.GetType().Name,
                    State = evt.Snapshot.State?.Text,
                    HealthStatus = evt.Snapshot.HealthStatus?.ToString(),
                    ExitCode = evt.Snapshot.ExitCode,
                };
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    private void LogFinalState()
    {
        if (_hasLogged) return;
        _hasLogged = true;

        logger.LogInformation("=== Final Resource State ===");

        foreach (var resource in appModel.Resources)
        {
            if (_lastKnownState.TryGetValue(resource.Name, out var state))
            {
                logger.LogInformation(
                    "Resource {Name} ({Type}): State={State}, Health={Health}, ExitCode={ExitCode}",
                    resource.Name,
                    state.ResourceType,
                    state.State ?? "unknown",
                    state.HealthStatus ?? "unknown",
                    state.ExitCode?.ToString() ?? "n/a");
            }
            else
            {
                logger.LogInformation(
                    "Resource {Name} ({Type}): no state received",
                    resource.Name,
                    resource.GetType().Name);
            }
        }
    }

    private record ResourceState
    {
        public string? ResourceType { get; init; }
        public string? State { get; init; }
        public string? HealthStatus { get; init; }
        public int? ExitCode { get; init; }
    }
}
