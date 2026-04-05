using System.Collections.Concurrent;
using Aspire.Hosting;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Shared test fixture that starts the OTLP receiver, Aspire AppHost with Alloy,
/// and all application resources. Provides access to collected telemetry and console logs.
/// </summary>
public class OtlpTestFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    public DistributedApplication Application { get; private set; } = default!;
    public OtlpReceiver Receiver { get; private set; } = default!;

    /// <summary>
    /// Raw console log lines from all resources, keyed by resource name.
    /// Captures stdout/stderr — complementary to structured OTel logs.
    /// </summary>
    public ConcurrentDictionary<string, ConcurrentBag<ConsoleLogLine>> ConsoleLogs { get; } = [];

    public async ValueTask InitializeAsync()
    {
        // 1. Start the OTLP receiver on a dynamic port
        Receiver = await OtlpReceiver.StartAsync();

        // 2. Set OTEL_EXPORTER_OTLP_ENDPOINT for the test process itself
        //    (used by TracedPipelineStartup for per-test spans)
        Environment.SetEnvironmentVariable(
            "OTEL_EXPORTER_OTLP_ENDPOINT",
            Receiver.BaseUrl);

        // 3. Create AppHost with the external OTEL endpoint pointing at our receiver
        //    Alloy runs in Docker, so it needs host.docker.internal to reach us
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AppHost>(
                [$"--EXTERNAL_OTEL_ENDPOINT={Receiver.GetDockerAccessibleUrl()}"],
                (options, _) =>
                {
                    options.AllowUnsecuredTransport = true;
                });

        appHost.WithTestingDefaults();

        // 5. Build and start
        Application = await appHost.BuildAsync()
            .WaitAsync(StartupTimeout);

        await Application.StartAsync()
            .WaitAsync(StartupTimeout);

        // 6. Start streaming console logs immediately (before waiting for healthy)
        //    so we capture startup logs and crash output
        StartConsoleLogCapture();

        // 7. Wait for resources to be ready.
        //    ApiService WaitFor chain guarantees: alloy, rabbitMq, workerService are all ready.
        var ct = new CancellationTokenSource(StartupTimeout).Token;

        await Application.ResourceNotifications
            .WaitForResourceHealthyAsync("apiservice", ct);
    }

    private void StartConsoleLogCapture()
    {
        var loggerService = Application.Services.GetRequiredService<ResourceLoggerService>();
        var notificationService = Application.Services.GetRequiredService<ResourceNotificationService>();
        var cts = new CancellationTokenSource();
        _consoleLogCts = cts;

        _ = Task.Run(async () =>
        {
            var watchedIds = new HashSet<string>();
            try
            {
                await foreach (var evt in notificationService.WatchAsync(cts.Token))
                {
                    // Use ResourceId (DCP-level ID), not Resource.Name (friendly name)
                    // ResourceLoggerService.WatchAsync expects the ResourceId
                    var resourceId = evt.ResourceId;
                    var resourceName = evt.Resource.Name;

                    if (!watchedIds.Add(resourceId)) continue;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var logEvent in loggerService.WatchAsync(resourceId).WithCancellation(cts.Token))
                            {
                                foreach (var line in logEvent)
                                {
                                    // Store by friendly name for easy test lookup
                                    var bag = ConsoleLogs.GetOrAdd(resourceName, _ => []);
                                    bag.Add(new ConsoleLogLine(line.Content, line.IsErrorMessage));
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                    }, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        }, cts.Token);
    }

    private CancellationTokenSource? _consoleLogCts;

    public async ValueTask DisposeAsync()
    {
        // Dump diagnostic summary so failed tests always show what telemetry arrived
        if (Receiver is not null)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(Receiver.GetDiagnosticSummary());
        }

        try { _consoleLogCts?.Cancel(); } catch (ObjectDisposedException) { }
        _consoleLogCts?.Dispose();

        if (Application is not null)
        {
            await Application.StopAsync();
            await Application.DisposeAsync();
        }

        if (Receiver is not null)
        {
            await Receiver.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

public record ConsoleLogLine(string Content, bool IsError);
