namespace AppHost.Grafana;

public static class GrafanaExtensions
{
    private const string ALLOY_RESOURCE_NAME = "grafana-alloy";
    private const int ALLOY_HTTP_PORT_NUMBER = 12345;
    private const string ALLOY_HTTP_ENDPOINT_NAME = "http";
    private const int ALLOY_OTLP_GRPC_PORT_NUMBER = 4317;
    private const string ALLOY_OTLP_GRPC_ENDPOINT_NAME = "grpc";
    private const int ALLOY_OTLP_HTTP_PORT_NUMBER = 4318;
    private const string ALLOY_OTLP_HTTP_ENDPOINT_NAME = "otlp";

    // AppHost configuration keys
    private const string CONFIG_ASPIRE_DASHBOARD_OTLP_URL = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
    private const string CONFIG_ASPIRE_DASHBOARD_OTLP_URL_DEFAULT = "http://localhost:18889";
    private const string CONFIG_ASPIRE_DASHBOARD_API_KEY = "AppHost:OtlpApiKey";
    private const string CONFIG_EXTERNAL_OTEL_ENDPOINT = "EXTERNAL_OTEL_ENDPOINT";

    // Alloy container environment variables (must match alloy-config.alloy sys.env() references)
    private const string ALLOY_ENV_ASPIRE_ENDPOINT = "ASPIRE_ENDPOINT";
    private const string ALLOY_ENV_ASPIRE_API_KEY = "ASPIRE_API_KEY";
    private const string ALLOY_ENV_EXTERNAL_OTEL_ENDPOINT = "EXTERNAL_OTEL_ENDPOINT";

    // Service environment variables
    private const string OTEL_EXPORTER_OTLP_ENDPOINT = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string OTEL_EXPORTER_OTLP_PROTOCOL = "OTEL_EXPORTER_OTLP_PROTOCOL";

    public static IResourceBuilder<GrafanaAlloyResource> AddGrafanaAlloy(
        this IDistributedApplicationBuilder builder)
    {
        var alloy = builder.AddResource(new GrafanaAlloyResource(ALLOY_RESOURCE_NAME))
            .WithImage("grafana/alloy")
            .WithImageTag("v1.14.2")
            .WithImageRegistry("registry.hub.docker.com")

            // Web UI
            .WithHttpEndpoint(
                targetPort: ALLOY_HTTP_PORT_NUMBER,
                name: ALLOY_HTTP_ENDPOINT_NAME)
            .WithUrlForEndpoint(
                ALLOY_HTTP_ENDPOINT_NAME,
                url => url.DisplayText = "Alloy")
            .WithHttpHealthCheck("/-/healthy")

            // OTLP gRPC
            .WithHttpEndpoint(
                targetPort: ALLOY_OTLP_GRPC_PORT_NUMBER,
                name: ALLOY_OTLP_GRPC_ENDPOINT_NAME)
            .WithUrlForEndpoint(
                ALLOY_OTLP_GRPC_ENDPOINT_NAME,
                url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)

            // OTLP HTTP
            .WithHttpEndpoint(
                targetPort: ALLOY_OTLP_HTTP_PORT_NUMBER,
                name: ALLOY_OTLP_HTTP_ENDPOINT_NAME)
            .WithUrlForEndpoint(
                ALLOY_OTLP_HTTP_ENDPOINT_NAME,
                url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)

            // Config
            .WithBindMount(
                "./Grafana/alloy-config.alloy",
                "/etc/alloy/config.alloy",
                isReadOnly: true)

            // Startup args
            .WithArgs(
                "run",
                $"--server.http.listen-addr=0.0.0.0:{ALLOY_HTTP_PORT_NUMBER}",
                "--storage.path=/var/lib/alloy/data",
                "--disable-reporting",
                "/etc/alloy/config.alloy")

            // Auto-wire OTLP from all project resources
            .WithAppForwarding();

        // Aspire dashboard endpoint
        var aspireDashboardUrl = builder.Configuration[CONFIG_ASPIRE_DASHBOARD_OTLP_URL]
            ?? CONFIG_ASPIRE_DASHBOARD_OTLP_URL_DEFAULT;
        alloy.WithEnvironment(ALLOY_ENV_ASPIRE_ENDPOINT, new HostUrl(aspireDashboardUrl));
        alloy.WithEnvironment(ALLOY_ENV_ASPIRE_API_KEY,
            builder.Configuration[CONFIG_ASPIRE_DASHBOARD_API_KEY]);

        // External OTEL endpoint (test harness) — empty string when not configured
        var externalOtelEndpoint = builder.Configuration[CONFIG_EXTERNAL_OTEL_ENDPOINT] ?? "";
        alloy.WithEnvironment(ALLOY_ENV_EXTERNAL_OTEL_ENDPOINT, externalOtelEndpoint);

        return alloy;
    }

    public static EndpointReference GetEndpoint(
        this IResourceBuilder<GrafanaAlloyResource> alloy, string endpointName)
    {
        return alloy.Resource.GetEndpoint(endpointName);
    }

    /// <summary>
    /// Subscribes to BeforeStartEvent and wires all resources that can have
    /// environment variables to send OTel through Alloy using OTLP/HTTP protocol.
    /// Based on the pattern from the Aspire Community Toolkit:
    /// https://github.com/CommunityToolkit/Aspire
    /// </summary>
    private static IResourceBuilder<GrafanaAlloyResource> WithAppForwarding(
        this IResourceBuilder<GrafanaAlloyResource> builder)
    {
        builder.ApplicationBuilder.Eventing.Subscribe<BeforeStartEvent>((evt, ct) =>
        {
            var otlpEndpoint = builder.Resource.GetEndpoint(ALLOY_OTLP_HTTP_ENDPOINT_NAME);

            var resources = evt.Model.Resources
                .OfType<IResourceWithEnvironment>()
                .Where(x => x.Name != ALLOY_RESOURCE_NAME);

            foreach (var resource in resources)
            {
                var resourceBuilder = builder.ApplicationBuilder.CreateResourceBuilder(resource);

                resourceBuilder.WithEnvironment(ctx =>
                {
                    if (resource is ContainerResource)
                    {
                        // Container → container: use Docker internal network
                        ctx.EnvironmentVariables[OTEL_EXPORTER_OTLP_ENDPOINT] =
                            $"http://{ALLOY_RESOURCE_NAME}:{ALLOY_OTLP_HTTP_PORT_NUMBER}";
                    }
                    else
                    {
                        // Project/executable → container: use host-mapped port
                        ctx.EnvironmentVariables[OTEL_EXPORTER_OTLP_ENDPOINT] =
                            $"http://{otlpEndpoint.Host}:{otlpEndpoint.Port}";
                    }

                    ctx.EnvironmentVariables[OTEL_EXPORTER_OTLP_PROTOCOL] = "http/protobuf";
                });
            }

            return Task.CompletedTask;
        });

        return builder;
    }
}
