using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Minimal OTLP HTTP receiver that accepts JSON-encoded log, trace, and metric exports.
/// Runs on a dynamic port and collects telemetry for test assertions.
/// </summary>
public sealed class OtlpReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentBag<OtlpLogRecord> _logs = [];
    private readonly ConcurrentBag<OtlpSpan> _spans = [];
    private readonly ConcurrentBag<OtlpMetric> _metrics = [];

    public string BaseUrl { get; private set; } = default!;

    private OtlpReceiver(WebApplication app)
    {
        _app = app;
    }

    public static async Task<OtlpReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        var receiver = new OtlpReceiver(app);

        app.MapPost("/v1/logs", async (HttpContext ctx) =>
        {
            var json = await JsonDocument.ParseAsync(ctx.Request.Body);
            receiver.ParseLogs(json);
            return Results.Ok();
        });

        app.MapPost("/v1/traces", async (HttpContext ctx) =>
        {
            var json = await JsonDocument.ParseAsync(ctx.Request.Body);
            receiver.ParseTraces(json);
            return Results.Ok();
        });

        app.MapPost("/v1/metrics", async (HttpContext ctx) =>
        {
            var json = await JsonDocument.ParseAsync(ctx.Request.Body);
            receiver.ParseMetrics(json);
            return Results.Ok();
        });

        await app.StartAsync();
        receiver.BaseUrl = app.Urls.First();
        return receiver;
    }

    /// <summary>
    /// Returns the base URL with host.docker.internal instead of 127.0.0.1,
    /// for use by containers (like Alloy) that need to reach this receiver.
    /// </summary>
    public string GetDockerAccessibleUrl()
    {
        var uri = new Uri(BaseUrl);
        return $"http://host.docker.internal:{uri.Port}";
    }

    // --- Log queries ---

    /// <summary>
    /// Gets all received log records, optionally filtered by a predicate.
    /// </summary>
    public IReadOnlyList<OtlpLogRecord> GetLogRecords(Func<OtlpLogRecord, bool>? predicate = null)
    {
        return predicate is null
            ? [.. _logs]
            : _logs.Where(predicate).ToList();
    }

    /// <summary>
    /// Gets log records filtered by resource name.
    /// </summary>
    public IReadOnlyList<OtlpLogRecord> GetLogRecords(string resourceName)
    {
        return GetLogRecords(l => l.ResourceName == resourceName);
    }

    /// <summary>
    /// Waits until the expected number of log records matching the predicate arrive, or the token is cancelled.
    /// </summary>
    public async Task<IReadOnlyList<OtlpLogRecord>> WaitForLogsAsync(
        Func<OtlpLogRecord, bool> predicate, int minCount, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var logs = GetLogRecords(predicate);
            if (logs.Count >= minCount)
                return logs;
            await Task.Delay(500, ct);
        }
        return GetLogRecords(predicate);
    }

    /// <inheritdoc cref="WaitForLogsAsync(Func{OtlpLogRecord, bool}, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpLogRecord>> WaitForLogsAsync(
        Func<OtlpLogRecord, bool> predicate, int minCount, TimeSpan timeout)
    {
        return WaitForLogsAsync(predicate, minCount, new CancellationTokenSource(timeout).Token);
    }

    /// <summary>
    /// Waits until the expected number of log records from a resource arrive, or the token is cancelled.
    /// </summary>
    public Task<IReadOnlyList<OtlpLogRecord>> WaitForLogsAsync(
        string resourceName, int minCount, CancellationToken ct)
    {
        return WaitForLogsAsync(l => l.ResourceName == resourceName, minCount, ct);
    }

    /// <inheritdoc cref="WaitForLogsAsync(string, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpLogRecord>> WaitForLogsAsync(
        string resourceName, int minCount, TimeSpan timeout)
    {
        return WaitForLogsAsync(resourceName, minCount, new CancellationTokenSource(timeout).Token);
    }

    // --- Span queries ---

    /// <summary>
    /// Gets all received spans, optionally filtered by a predicate.
    /// </summary>
    public IReadOnlyList<OtlpSpan> GetSpans(Func<OtlpSpan, bool>? predicate = null)
    {
        return predicate is null
            ? [.. _spans]
            : _spans.Where(predicate).ToList();
    }

    /// <summary>
    /// Gets spans filtered by resource name.
    /// </summary>
    public IReadOnlyList<OtlpSpan> GetSpans(string resourceName)
    {
        return GetSpans(s => s.ResourceName == resourceName);
    }

    /// <summary>
    /// Waits until the expected number of spans matching the predicate arrive, or the token is cancelled.
    /// </summary>
    public async Task<IReadOnlyList<OtlpSpan>> WaitForSpansAsync(
        Func<OtlpSpan, bool> predicate, int minCount, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var spans = GetSpans(predicate);
            if (spans.Count >= minCount)
                return spans;
            await Task.Delay(500, ct);
        }
        return GetSpans(predicate);
    }

    /// <inheritdoc cref="WaitForSpansAsync(Func{OtlpSpan, bool}, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpSpan>> WaitForSpansAsync(
        Func<OtlpSpan, bool> predicate, int minCount, TimeSpan timeout)
    {
        return WaitForSpansAsync(predicate, minCount, new CancellationTokenSource(timeout).Token);
    }

    /// <summary>
    /// Waits until the expected number of spans from a resource arrive, or the token is cancelled.
    /// </summary>
    public Task<IReadOnlyList<OtlpSpan>> WaitForSpansAsync(
        string resourceName, int minCount, CancellationToken ct)
    {
        return WaitForSpansAsync(s => s.ResourceName == resourceName, minCount, ct);
    }

    /// <inheritdoc cref="WaitForSpansAsync(string, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpSpan>> WaitForSpansAsync(
        string resourceName, int minCount, TimeSpan timeout)
    {
        return WaitForSpansAsync(resourceName, minCount, new CancellationTokenSource(timeout).Token);
    }

    // --- Metric queries ---

    /// <summary>
    /// Gets all received metrics, optionally filtered by a predicate.
    /// </summary>
    public IReadOnlyList<OtlpMetric> GetMetrics(Func<OtlpMetric, bool>? predicate = null)
    {
        return predicate is null
            ? [.. _metrics]
            : _metrics.Where(predicate).ToList();
    }

    /// <summary>
    /// Gets metrics filtered by resource name.
    /// </summary>
    public IReadOnlyList<OtlpMetric> GetMetrics(string resourceName)
    {
        return GetMetrics(m => m.ResourceName == resourceName);
    }

    /// <summary>
    /// Waits until the expected number of metrics matching the predicate arrive, or the token is cancelled.
    /// </summary>
    public async Task<IReadOnlyList<OtlpMetric>> WaitForMetricsAsync(
        Func<OtlpMetric, bool> predicate, int minCount, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var metrics = GetMetrics(predicate);
            if (metrics.Count >= minCount)
                return metrics;
            await Task.Delay(500, ct);
        }
        return GetMetrics(predicate);
    }

    /// <inheritdoc cref="WaitForMetricsAsync(Func{OtlpMetric, bool}, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpMetric>> WaitForMetricsAsync(
        Func<OtlpMetric, bool> predicate, int minCount, TimeSpan timeout)
    {
        return WaitForMetricsAsync(predicate, minCount, new CancellationTokenSource(timeout).Token);
    }

    /// <summary>
    /// Waits until the expected number of metrics from a resource arrive, or the token is cancelled.
    /// </summary>
    public Task<IReadOnlyList<OtlpMetric>> WaitForMetricsAsync(
        string resourceName, int minCount, CancellationToken ct)
    {
        return WaitForMetricsAsync(m => m.ResourceName == resourceName, minCount, ct);
    }

    /// <inheritdoc cref="WaitForMetricsAsync(string, int, CancellationToken)"/>
    public Task<IReadOnlyList<OtlpMetric>> WaitForMetricsAsync(
        string resourceName, int minCount, TimeSpan timeout)
    {
        return WaitForMetricsAsync(resourceName, minCount, new CancellationTokenSource(timeout).Token);
    }

    // --- Diagnostics ---

    /// <summary>
    /// Returns a summary of all collected telemetry, grouped by resource.
    /// Useful for dumping on test failure to see what arrived and what didn't.
    /// </summary>
    public string GetDiagnosticSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== OTLP Receiver Diagnostic Summary ===");

        var logsByResource = _logs.GroupBy(l => l.ResourceName ?? "(unknown)").OrderBy(g => g.Key);
        sb.AppendLine($"Total log records: {_logs.Count}");
        foreach (var group in logsByResource)
        {
            sb.AppendLine($"  [{group.Key}] {group.Count()} logs");
            foreach (var log in group.Take(5))
                sb.AppendLine($"    {log.SeverityText}: {Truncate(log.Body, 120)}");
            if (group.Count() > 5)
                sb.AppendLine($"    ... and {group.Count() - 5} more");
        }

        var spansByResource = _spans.GroupBy(s => s.ResourceName ?? "(unknown)").OrderBy(g => g.Key);
        sb.AppendLine($"Total spans: {_spans.Count}");
        foreach (var group in spansByResource)
            sb.AppendLine($"  [{group.Key}] {group.Count()} spans");

        var metricsByResource = _metrics.GroupBy(m => m.ResourceName ?? "(unknown)").OrderBy(g => g.Key);
        sb.AppendLine($"Total metrics: {_metrics.Count}");
        foreach (var group in metricsByResource)
        {
            var names = group.Select(m => m.Name).Distinct().OrderBy(n => n).ToList();
            sb.AppendLine($"  [{group.Key}] {group.Count()} data points ({names.Count} unique metrics)");
            foreach (var name in names.Take(10))
                sb.AppendLine($"    {name}");
            if (names.Count > 10)
                sb.AppendLine($"    ... and {names.Count - 10} more");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns a chronological trace chain for a given trace ID across all resources.
    /// Shows the distributed flow of a single request/operation.
    /// </summary>
    public string FormatTraceChain(string traceId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Trace Chain: {traceId} ===");

        var traceSpans = _spans
            .Where(s => s.TraceId == traceId)
            .OrderBy(s => s.StartTimeUnixNano)
            .ToList();

        if (traceSpans.Count == 0)
        {
            sb.AppendLine("  (no spans found)");
            return sb.ToString();
        }

        foreach (var span in traceSpans)
        {
            var parent = span.ParentSpanId is not null ? $" parent={span.ParentSpanId}" : "";
            sb.AppendLine($"  [{span.ResourceName}] {span.Name} (span={span.SpanId}{parent})");
        }

        var traceLogs = _logs
            .Where(l => l.TraceId == traceId)
            .OrderBy(l => l.TimestampUnixNano)
            .ToList();

        if (traceLogs.Count > 0)
        {
            sb.AppendLine($"  --- Correlated logs ({traceLogs.Count}) ---");
            foreach (var log in traceLogs)
                sb.AppendLine($"  [{log.ResourceName}] {log.SeverityText}: {log.Body}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Clears all collected telemetry. Useful between tests if sharing a fixture.
    /// </summary>
    public void Clear()
    {
        _logs.Clear();
        _spans.Clear();
        _metrics.Clear();
    }

    // --- Parsing ---

    private void ParseLogs(JsonDocument json)
    {
        foreach (var resourceLog in json.RootElement.GetPropertyOrEmpty("resourceLogs"))
        {
            var serviceName = GetServiceName(resourceLog);
            foreach (var scopeLog in resourceLog.GetPropertyOrEmpty("scopeLogs"))
            {
                foreach (var logRecord in scopeLog.GetPropertyOrEmpty("logRecords"))
                {
                    _logs.Add(new OtlpLogRecord
                    {
                        ResourceName = serviceName,
                        SeverityText = logRecord.GetStringOrNull("severityText"),
                        SeverityNumber = logRecord.TryGetProperty("severityNumber", out var sn) ? sn.GetInt32() : 0,
                        Body = GetBodyString(logRecord),
                        TraceId = logRecord.GetStringOrNull("traceId"),
                        SpanId = logRecord.GetStringOrNull("spanId"),
                        TimestampUnixNano = logRecord.TryGetProperty("timeUnixNano", out var ts)
                            ? long.TryParse(ts.GetString(), out var v) ? v : 0
                            : 0,
                        Attributes = ParseAttributes(logRecord),
                    });
                }
            }
        }
    }

    private void ParseTraces(JsonDocument json)
    {
        foreach (var resourceSpan in json.RootElement.GetPropertyOrEmpty("resourceSpans"))
        {
            var serviceName = GetServiceName(resourceSpan);
            foreach (var scopeSpan in resourceSpan.GetPropertyOrEmpty("scopeSpans"))
            {
                foreach (var span in scopeSpan.GetPropertyOrEmpty("spans"))
                {
                    _spans.Add(new OtlpSpan
                    {
                        ResourceName = serviceName,
                        Name = span.GetStringOrNull("name"),
                        TraceId = span.GetStringOrNull("traceId"),
                        SpanId = span.GetStringOrNull("spanId"),
                        ParentSpanId = span.GetStringOrNull("parentSpanId"),
                        StartTimeUnixNano = span.TryGetProperty("startTimeUnixNano", out var st)
                            ? long.TryParse(st.GetString(), out var v) ? v : 0
                            : 0,
                        EndTimeUnixNano = span.TryGetProperty("endTimeUnixNano", out var et)
                            ? long.TryParse(et.GetString(), out var v2) ? v2 : 0
                            : 0,
                    });
                }
            }
        }
    }

    private void ParseMetrics(JsonDocument json)
    {
        foreach (var resourceMetric in json.RootElement.GetPropertyOrEmpty("resourceMetrics"))
        {
            var serviceName = GetServiceName(resourceMetric);
            foreach (var scopeMetric in resourceMetric.GetPropertyOrEmpty("scopeMetrics"))
            {
                foreach (var metric in scopeMetric.GetPropertyOrEmpty("metrics"))
                {
                    _metrics.Add(new OtlpMetric
                    {
                        ResourceName = serviceName,
                        Name = metric.GetStringOrNull("name"),
                        Description = metric.GetStringOrNull("description"),
                        Unit = metric.GetStringOrNull("unit"),
                    });
                }
            }
        }
    }

    private static string? GetServiceName(JsonElement resourceElement)
    {
        if (!resourceElement.TryGetProperty("resource", out var resource))
            return null;
        if (!resource.TryGetProperty("attributes", out var attributes))
            return null;

        foreach (var attr in attributes.EnumerateArray())
        {
            if (attr.GetStringOrNull("key") == "service.name" &&
                attr.TryGetProperty("value", out var value))
            {
                return value.GetStringOrNull("stringValue");
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string?> ParseAttributes(JsonElement element)
    {
        var result = new Dictionary<string, string?>();
        if (!element.TryGetProperty("attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var attr in attributes.EnumerateArray())
        {
            var key = attr.GetStringOrNull("key");
            if (key is null) continue;

            if (attr.TryGetProperty("value", out var value))
            {
                // OTLP attribute values are typed: stringValue, intValue, boolValue, etc.
                var strVal = value.GetStringOrNull("stringValue")
                    ?? value.GetStringOrNull("intValue")
                    ?? value.GetStringOrNull("boolValue")
                    ?? value.GetStringOrNull("doubleValue");
                result[key] = strVal;
            }
        }
        return result;
    }

    private static string? GetBodyString(JsonElement logRecord)
    {
        if (!logRecord.TryGetProperty("body", out var body))
            return null;
        return body.GetStringOrNull("stringValue");
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (value is null) return "(null)";
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public record OtlpLogRecord
{
    public string? ResourceName { get; init; }
    public string? SeverityText { get; init; }
    public int SeverityNumber { get; init; }
    public string? Body { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public long TimestampUnixNano { get; init; }
    public IReadOnlyDictionary<string, string?> Attributes { get; init; } = new Dictionary<string, string?>();
}

public record OtlpSpan
{
    public string? ResourceName { get; init; }
    public string? Name { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public long StartTimeUnixNano { get; init; }
    public long EndTimeUnixNano { get; init; }
}

public record OtlpMetric
{
    public string? ResourceName { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
}

internal static class JsonElementExtensions
{
    public static IEnumerable<JsonElement> GetPropertyOrEmpty(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop.EnumerateArray().ToArray();
        return [];
    }

    public static string? GetStringOrNull(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
