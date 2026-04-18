using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

public class OtlpForwardingTests(OtlpTestFixture fixture) : IClassFixture<OtlpTestFixture>, IAsyncLifetime
{
    private string? _traceId;

    public ValueTask InitializeAsync()
    {
        _traceId = Activity.Current?.TraceId.ToHexString();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_traceId is null) return;

        // Wait until no new trace-correlated data arrives (stable for 500ms, max 5s)
        var lastCount = 0;
        var stableAt = DateTime.UtcNow;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow - stableAt < TimeSpan.FromMilliseconds(500)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            var count = fixture.Receiver.GetSpans(s => s.TraceId == _traceId).Count
                      + fixture.Receiver.GetLogRecords(l => l.TraceId == _traceId).Count;
            if (count != lastCount)
            {
                lastCount = count;
                stableAt = DateTime.UtcNow;
            }
        }

        // Primary trace chain for the test's own trace
        var sb = new StringBuilder();
        sb.AppendLine(fixture.Receiver.FormatTraceChain(_traceId));

        // Include any downstream traces that link back to this trace — e.g. the
        // dispatch trace produced by DeferredDispatcher under Option D, which
        // lives on its own trace id but carries an ActivityLink to _traceId.
        var linkedTraceIds = fixture.Receiver
            .GetSpans(s => s.Links.Any(l => l.TraceId == _traceId))
            .Select(s => s.TraceId!)
            .Where(id => id != _traceId)
            .Distinct()
            .ToList();

        foreach (var linkedTraceId in linkedTraceIds)
        {
            sb.AppendLine();
            sb.AppendLine($"-- linked downstream trace --");
            sb.AppendLine(fixture.Receiver.FormatTraceChain(linkedTraceId));
        }

        var content = sb.ToString();

        // Write to VS Test Explorer output
        TestContext.Current.TestOutputHelper?.WriteLine(content);

        // Write to _Logs/{methodName}.testlog on disk
        TestLogWriter.Write(TestContext.Current, GetType(), content);
    }

    [Fact]
    public async Task WorkerLogs_AreForwarded()
    {
        var logs = await fixture.Receiver.WaitForLogsAsync(
            "workerservice",
            minCount: 3,
            timeout: TimeSpan.FromSeconds(30));

        Assert.True(logs.Count >= 3,
            $"Expected at least 3 worker logs, got {logs.Count}");

        var hasHeartbeat = logs.Any(l =>
            l.Body?.Contains("Worker heartbeat") == true);
        Assert.True(hasHeartbeat, "Expected worker heartbeat log messages");

        foreach (var log in logs.Take(5))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"[workerservice] {log.SeverityText}: {log.Body}");
        }
    }

    [Fact]
    public async Task ApiLogs_AreForwarded_OnHttpRequest()
    {
        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var response = await httpClient.GetAsync("/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logs = await fixture.Receiver.WaitForLogsAsync(
            "apiservice",
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task Logs_CanBeFiltered_ByResourceName()
    {
        await fixture.Receiver.WaitForLogsAsync("workerservice", minCount: 1, timeout: TimeSpan.FromSeconds(15));

        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        await httpClient.GetAsync("/weatherforecast");
        await fixture.Receiver.WaitForLogsAsync("apiservice", minCount: 1, timeout: TimeSpan.FromSeconds(15));

        var workerLogs = fixture.Receiver.GetLogRecords("workerservice");
        var apiLogs = fixture.Receiver.GetLogRecords("apiservice");

        Assert.NotEmpty(workerLogs);
        Assert.NotEmpty(apiLogs);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Worker logs: {workerLogs.Count}, API logs: {apiLogs.Count}");
    }

    [Fact]
    public async Task Logs_CanBeFiltered_ByPredicate()
    {
        var warnings = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "workerservice" && l.SeverityText == "Warning",
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(warnings);
        Assert.All(warnings, l => Assert.Contains("periodic warning", l.Body));

        var heartbeats = fixture.Receiver.GetLogRecords(
            l => l.Body?.Contains("heartbeat") == true);
        Assert.NotEmpty(heartbeats);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Warnings: {warnings.Count}, Heartbeats: {heartbeats.Count}");
    }

    [Fact]
    public async Task Traces_AreCorrelated_AcrossServices()
    {
        Assert.NotNull(_traceId);

        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var response = await httpClient.GetAsync("/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var correlatedSpans = await fixture.Receiver.WaitForSpansAsync(
            s => s.ResourceName == "apiservice" && s.TraceId == _traceId,
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(correlatedSpans);
    }

    [Fact]
    public async Task Metrics_AreForwarded()
    {
        var metrics = await fixture.Receiver.WaitForMetricsAsync(
            "apiservice",
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(metrics);

        var metricNames = metrics.Select(m => m.Name).Distinct().OrderBy(n => n).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"API metrics received: {metricNames.Count} unique names");
        foreach (var name in metricNames.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"  {name}");
        }
    }

    [Fact]
    public async Task ConsoleLogs_AreCaptured()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && !fixture.ConsoleLogs.ContainsKey("workerservice"))
            await Task.Delay(500);

        Assert.True(fixture.ConsoleLogs.ContainsKey("workerservice"),
            $"No console logs captured for workerservice. Resources with logs: [{string.Join(", ", fixture.ConsoleLogs.Keys)}]");

        var workerConsole = fixture.ConsoleLogs["workerservice"];
        Assert.NotEmpty(workerConsole);

        var hasHeartbeat = workerConsole.Any(l => l.Content.Contains("Worker heartbeat"));
        Assert.True(hasHeartbeat, "Expected worker heartbeat in console output");

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Console log lines captured: {workerConsole.Count}");
        foreach (var line in workerConsole.Take(5))
        {
            var prefix = line.IsError ? "ERR" : "OUT";
            TestContext.Current.TestOutputHelper?.WriteLine($"  [{prefix}] {line.Content}");
        }
    }

    [Fact]
    public async Task DebugLogs_AreFilteredByAlloy()
    {
        var infoLogs = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "workerservice"
                 && l.Body?.Contains("Worker heartbeat") == true,
            minCount: 3,
            timeout: TimeSpan.FromSeconds(30));
        Assert.True(infoLogs.Count >= 3);

        var debugLogs = fixture.Receiver.GetLogRecords(
            l => l.ResourceName == "workerservice"
                 && l.Body?.Contains("Worker debug tick") == true);
        Assert.Empty(debugLogs);

        var belowInfoLogs = fixture.Receiver.GetLogRecords(
            l => l.ResourceName == "workerservice"
                 && l.SeverityNumber > 0 && l.SeverityNumber < 9);
        Assert.Empty(belowInfoLogs);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Info+ logs: {infoLogs.Count}, Debug logs (should be 0): {debugLogs.Count}");
    }

    [Fact]
    public async Task MessageChain_IsTraceable()
    {
        Assert.NotNull(_traceId);

        // 1. POST to trigger the message chain
        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var itemName = $"test-{Guid.NewGuid():N}";
        var response = await httpClient.PostAsJsonAsync("/process", new { itemName });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = body.GetProperty("itemId").GetGuid();

        // 2. Poll GET /process/{id} until the result arrives (round-trip through RabbitMQ)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        HttpResponseMessage? resultResponse = null;
        while (DateTime.UtcNow < deadline)
        {
            resultResponse = await httpClient.GetAsync($"/process/{itemId}");
            if (resultResponse.StatusCode == HttpStatusCode.OK)
                break;
            await Task.Delay(500);
        }

        Assert.NotNull(resultResponse);
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

        // 3. Verify worker processing log arrived via OTel
        var processingLogs = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "workerservice"
                 && l.Body?.Contains(itemName) == true,
            minCount: 1,
            timeout: TimeSpan.FromSeconds(15));
        Assert.NotEmpty(processingLogs);

        // 4. Verify API received the result back via RabbitMQ
        var resultLogs = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "apiservice"
                 && l.Body?.Contains($"Received processing result for item {itemId}") == true,
            minCount: 1,
            timeout: TimeSpan.FromSeconds(15));
        Assert.NotEmpty(resultLogs);

        // 5. Verify trace correlation across the full round-trip
        Assert.Contains(processingLogs, l => l.TraceId == _traceId);
        Assert.Contains(resultLogs, l => l.TraceId == _traceId);
    }

    /// <summary>
    /// Verifies SpectrumPlanner issue #155 is fixed by Option D: when work is
    /// enqueued under trace A and dispatched by a background tick, the dispatched
    /// Producer span starts a new trace and carries an ActivityLink back to the
    /// enqueuer. The handler runs under the Producer's new trace (as its child
    /// via Wolverine's traceparent propagation), and the Producer span's link
    /// is discoverable by traversing the handler's trace.
    /// </summary>
    [Fact]
    public async Task DeferredDispatch_CorrelatesToEnqueueTraceViaActivityLink()
    {
        Assert.NotNull(_traceId);

        // 1. Enqueue under the test's HTTP trace (trace A)
        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var itemName = $"deferred-{Guid.NewGuid():N}";
        var response = await httpClient.PostAsJsonAsync("/schedule", new { itemName },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // 2. Wait for the handler's processing log — its TraceId tells us which
        //    trace the handler ran under.
        var handlerLogs = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "workerservice"
                 && l.Body?.Contains(itemName) == true
                 && l.Body?.Contains("Processing item") == true,
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(handlerLogs);
        var handlerLog = handlerLogs[0];
        Assert.NotNull(handlerLog.TraceId);

        var handlerTraceId = handlerLog.TraceId;

        // 3. Option D assertions:
        //    (a) handler trace is NOT the enqueue trace (new trace created at dispatch)
        //    (b) the handler's trace contains a span with an ActivityLink back to
        //        the enqueue trace — this is the Producer span on the dispatcher side
        Assert.NotEqual(_traceId, handlerTraceId);

        // Wait for the DeferredDispatcher.Publish span to arrive with its link
        var spansWithLink = await fixture.Receiver.WaitForSpansAsync(
            s => s.TraceId == handlerTraceId
                 && s.Links.Any(l => l.TraceId == _traceId),
            minCount: 1,
            timeout: TimeSpan.FromSeconds(15));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"enqueue trace={_traceId}");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"handler trace={handlerTraceId}");
        TestContext.Current.TestOutputHelper?.WriteLine(
            fixture.Receiver.FormatTraceChain(handlerTraceId!));

        Assert.NotEmpty(spansWithLink);

        // Sanity: the Producer span we found should be the DeferredDispatcher.Publish span
        Assert.Contains(spansWithLink, s => s.Name == "DeferredDispatcher.Publish");
    }

    /// <summary>
    /// Option F: correlation-attribute filtering. Verifies the dispatcher stamps
    /// a stable correlation attribute so test assertions can find the dispatched
    /// work WITHOUT depending on which trace-structure choice the production code
    /// made (Option C same-trace vs Option D new-trace-with-link). If SP later
    /// changes the strategy, tests filtering by attribute keep working.
    /// </summary>
    [Fact]
    public async Task DeferredDispatch_FindableByEnqueueTraceIdAttribute()
    {
        Assert.NotNull(_traceId);

        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var itemName = $"deferred-attr-{Guid.NewGuid():N}";
        var response = await httpClient.PostAsJsonAsync("/schedule", new { itemName },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Filter by attribute alone — no trace-id assumptions.
        var publishSpans = await fixture.Receiver.WaitForSpansByAttributeAsync(
            "enqueue.trace_id", _traceId!,
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotEmpty(publishSpans);
        Assert.All(publishSpans, s => Assert.Equal("DeferredDispatcher.Publish", s.Name));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Found {publishSpans.Count} dispatch span(s) by attribute enqueue.trace_id={_traceId}");
        foreach (var span in publishSpans)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"  trace={span.TraceId} span={span.SpanId} item_id={span.Attributes.GetValueOrDefault("deferred_work.item_id")}");
        }
    }

    /// <summary>
    /// Verifies that handler exceptions and retries are visible through OTel:
    /// 1. Error spans (per handler execution) — Wolverine sets status=ERROR with exception type
    /// 2. Error logs (per attempt) — Wolverine's built-in "Failed to process message" logging
    /// The error policy is: retry twice (100ms, 500ms) then move to error queue.
    /// This produces 3 handler executions but only 2 error spans — the final
    /// MoveToErrorQueue action doesn't re-execute the handler.
    /// </summary>
    [Fact]
    public async Task HandlerException_RetriesAreVisible_InTracesAndLogs()
    {
        Assert.NotNull(_traceId);

        // 1. POST to trigger the failing handler
        var httpClient = fixture.Application.CreateHttpClient("apiservice");
        var itemName = $"fail-{Guid.NewGuid():N}";
        var response = await httpClient.PostAsJsonAsync("/process-fail", new { itemName },
            TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);

        // 2. Wait for Wolverine's per-attempt error logs — fires on EVERY failure (3 total)
        var errorLogs = await fixture.Receiver.WaitForLogsAsync(
            l => l.ResourceName == "workerservice"
                 && l.SeverityText == "Error"
                 && l.Body?.Contains("Failed to process message") == true
                 && l.Body?.Contains("ProcessItemFailCommand") == true,
            minCount: 3,
            timeout: TimeSpan.FromSeconds(30));

        Assert.True(errorLogs.Count >= 3,
            $"Expected at least 3 error logs (one per attempt), got {errorLogs.Count}");

        // 3. Wait for error spans — 2 from RetryWithCooldown handler re-executions
        var errorSpans = await fixture.Receiver.WaitForSpansAsync(
            s => s.ResourceName == "workerservice"
                 && s.IsError
                 && s.Name?.Contains("ProcessItemFailCommand") == true,
            minCount: 2,
            timeout: TimeSpan.FromSeconds(15));

        Assert.True(errorSpans.Count >= 2,
            $"Expected at least 2 error spans (retries), got {errorSpans.Count}");

        // 4. Verify trace chain shows ERROR markers with exception type
        var traceChain = fixture.Receiver.FormatTraceChain(errorSpans.First().TraceId!);
        Assert.Contains("ERROR", traceChain);
        Assert.Contains("InvalidOperationException", traceChain);
    }
}
