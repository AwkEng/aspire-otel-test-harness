using AppHost.Grafana;

var builder = DistributedApplication.CreateBuilder(args);

var alloy = builder.AddGrafanaAlloy();

var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithUrlForEndpoint("management", url => url.DisplayText = "RabbitMQ")
    .WithUrlForEndpoint("tcp", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);

var workerService = builder.AddProject<Projects.WorkerService>("workerservice")
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitFor(alloy);

var apiService = builder.AddProject<Projects.ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitFor(alloy)
    .WaitFor(workerService)
    .WithUrlForEndpoint("https", url => url.DisplayText = "API")
    .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);

builder.Build().Run();
