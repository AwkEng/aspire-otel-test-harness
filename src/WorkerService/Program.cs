using AspireOtelTestHarness.Messages;
using WorkerService;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

// Wolverine messaging
builder.Services.AddWolverine(opts =>
{
    opts.Policies.DisableConventionalLocalRouting();

    var rabbitMqUri = builder.Configuration.GetConnectionString("messaging");
    if (rabbitMqUri is not null)
    {
        opts.UseRabbitMq(new Uri(rabbitMqUri))
            .AutoProvision();
    }

    opts.UseSystemTextJsonForSerialization();
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    opts.ListenToRabbitQueue("process-commands");

    opts.PublishMessage<ItemProcessedEvent>()
        .ToRabbitQueue("process-results");
});

var host = builder.Build();
host.Run();
