using Microsoft.Extensions.Logging;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Extension methods for configuring the Aspire AppHost in integration tests.
/// </summary>
public static class AppHostExtensions
{
    /// <summary>
    /// Applies standard testing defaults: logging, infrastructure services, and diagnostics.
    /// </summary>
    public static IDistributedApplicationTestingBuilder WithTestingDefaults(
        this IDistributedApplicationTestingBuilder builder)
    {
        builder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Aspire.Hosting", LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None);
        });

        builder.Services.AddHostedService<FinalStateLoggerService>();

        return builder;
    }
}
