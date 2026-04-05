using Tests.Integration.Infrastructure;
using PracticalOtel.xUnit.v3.OpenTelemetry;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(OtelPipelineStartup))]

namespace Tests.Integration.Infrastructure;

/// <summary>
/// xUnit v3 pipeline startup that creates per-test trace spans
/// and exports them via OTLP to the test harness receiver.
/// </summary>
public class OtelPipelineStartup : TracedPipelineStartup;
