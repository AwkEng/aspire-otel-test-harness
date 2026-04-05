using AppHost.Grafana;

var builder = DistributedApplication.CreateBuilder(args);

var alloy = builder.AddGrafanaAlloy();

var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

var apiService = builder.AddProject<Projects.ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq);

builder.AddProject<Projects.WorkerService>("workerservice")
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq);

builder.AddProject<Projects.Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
