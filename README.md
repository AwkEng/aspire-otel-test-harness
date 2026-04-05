# Aspire OTel Test Harness

Capture OpenTelemetry logs, traces, and metrics from **out-of-process** Aspire resources during integration tests.

## The Problem

With `DistributedApplicationTestingBuilder`, worker service logs are invisible — they run as separate processes and their telemetry goes nowhere useful. When a message handler fails or a saga chain breaks, you're debugging blind.

## The Solution

Route all OTel through **[Grafana Alloy](https://grafana.com/oss/alloy-opentelemetry-collector/)** (an OpenTelemetry Collector distribution) and fan it out to an **in-process OTLP receiver** that the test code can query directly.

![Architecture Diagram](docs/diagram.png)

Each xUnit test gets its own trace span. Trace context propagates through HTTP calls **and** RabbitMQ messages, so the test can filter by trace ID to see only its own request chain across all services.

## Features

| Feature | Details |
|---------|---------|
| **OTel forwarding** | Logs (Info+), traces, and metrics from all resources flow through Alloy to the test receiver. Silently no-ops when `EXTERNAL_OTEL_ENDPOINT` isn't set. |
| **Trace correlation** | Per-test spans via [PracticalOtel.xUnit.v3](https://github.com/practical-otel/dotnet-xunit-otel). Full distributed trace across HTTP and message broker hops. |
| **Message chain tracing** | Full round-trip: API publishes command → RabbitMQ → Worker processes → publishes result → RabbitMQ → API receives. Every log carries the originating test's trace ID. |
| **Console log capture** | Resource stdout/stderr via `ResourceLoggerService.WatchAsync()`. Catches startup crashes before OTel initializes. |
| **Diagnostics** | `GetDiagnosticSummary()` on failure, `FormatTraceChain(traceId)` for visualization, `FinalStateLoggerService` for shutdown state. |
| **Predicate filtering** | `GetLogRecords(l => l.Body?.Contains("error") == true)` — filter by resource, severity, content, trace ID. |
| **Structured attributes** | Log record attributes parsed from OTLP JSON — filter by structured fields (e.g., `l.Attributes["ItemId"]`) instead of string-matching the body. |
| **Severity filtering** | Alloy drops Debug/Trace-level logs (`severity_number < 9`) before forwarding, keeping the receiver focused on actionable output. |

## Tests

| Test | Proves |
|------|--------|
| `WorkerLogs_AreForwarded` | Out-of-process worker logs arrive at the receiver |
| `ApiLogs_AreForwarded_OnHttpRequest` | API request logs flow through Alloy |
| `Logs_CanBeFiltered_ByResourceName` | Filter by service name |
| `Logs_CanBeFiltered_ByPredicate` | Filter by severity, message content, any field |
| `Traces_AreCorrelated_AcrossServices` | HTTP trace context propagates end-to-end |
| `Metrics_AreForwarded` | Runtime/ASP.NET metrics arrive at the receiver |
| `ConsoleLogs_AreCaptured` | Raw stdout/stderr captured per resource |
| `DebugLogs_AreFilteredByAlloy` | Debug-level logs are dropped by Alloy's severity filter |
| `MessageChain_IsTraceable` | Full round-trip (API → Worker → API) shares one trace ID |

## Project Structure

```
src/
  AppHost/                   Aspire orchestrator + Grafana Alloy config
  ApiService/                REST API + Wolverine publisher
  WorkerService/             Background service + Wolverine handlers
  ServiceDefaults/           Shared OTel config + message types
test/
  Tests.Integration/         9 integration tests + OTLP receiver infrastructure
```

## How It Works

1. `OtlpTestFixture` starts an OTLP HTTP receiver on a dynamic port
2. AppHost starts with `--EXTERNAL_OTEL_ENDPOINT=http://host.docker.internal:{port}`
3. `AddGrafanaAlloy()` detects `EXTERNAL_OTEL_ENDPOINT` and selects the external Alloy config (`alloy-config.external.alloy`) with immediate batch forwarding; without it, the default config (`alloy-config.default.alloy`) routes only to the Aspire Dashboard
4. `WithAppForwarding()` auto-sets `OTEL_EXPORTER_OTLP_ENDPOINT` on all resources → Alloy, and minimizes SDK batch delays (`OTEL_BSP_SCHEDULE_DELAY`, `OTEL_BLRP_SCHEDULE_DELAY`, `OTEL_METRIC_EXPORT_INTERVAL`) when an external endpoint is configured
5. `TracedPipelineStartup` creates a span per test; HTTP/Wolverine propagate trace context
6. Test queries receiver by resource name, predicate, or trace ID
7. `IAsyncLifetime.DisposeAsync` waits for trace-correlated data to stabilize, then dumps the full trace chain to `TestOutputHelper` (runs on pass and fail)
8. On teardown: `FinalStateLoggerService` logs resource state, `GetDiagnosticSummary()` dumps collected telemetry

## Why Not WebApplicationFactory?

`WebApplicationFactory` (WAF) runs the service in-process, which seems simpler but undermines what this harness validates:

- **OTel SDK ignores DI configuration for exporter endpoints.** The SDK reads `OTEL_EXPORTER_OTLP_ENDPOINT` from environment variables at options construction time. `ConfigureAll<OtlpExporterOptions>` and `PostConfigure` don't reliably override them, forcing fragile env var save/restore hacks.
- **Trace context doesn't propagate through TestServer.** WAF's in-memory HTTP handler doesn't propagate `Activity.Current` the same way a real HTTP client does. Per-test trace correlation — the core feature of this harness — breaks.
- **Competing consumers on shared queues.** The WAF's Wolverine instance listens on the same RabbitMQ queues as the out-of-process service, making message delivery non-deterministic.
- **Metric export timing.** A freshly started WAF hasn't flushed metrics yet (default interval ~60s), requiring inflated timeouts.
- **It tests the wrong thing.** The whole point is proving telemetry flows through the real infrastructure — Alloy, RabbitMQ, separate processes. WAF removes exactly the parts you're trying to validate.

WAF is the right tool for controller unit tests and DI integration tests. For end-to-end OTel pipeline validation, `DistributedApplicationTestingBuilder` with out-of-process resources is the correct approach.

## Quick Start

```bash
dotnet test                          # run all tests
dotnet run --project src/AppHost     # run standalone with dashboard
```

> Requires .NET 10 SDK and Docker Desktop

## Acknowledgments

Built with [Claude Code](https://claude.ai/claude-code) by Anthropic.

Inspired by:
- [Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire) — `WithAppForwarding()` pattern
- [aspire-otel-testing](https://github.com/afscrome/aspire-otel-testing) by [@afscrome](https://github.com/afscrome) — resource log streaming, `FinalStateLoggerService`
- [dotnet-xunit-otel](https://github.com/practical-otel/dotnet-xunit-otel) by [Practical OpenTelemetry](https://github.com/practical-otel) — per-test trace spans
