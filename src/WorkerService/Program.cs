using AspireOtelTestHarness.Messages;
using WorkerService;
using Wolverine;
using Wolverine.ErrorHandling;
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

    // Retry twice then dead-letter. Wolverine's built-in error logging fires
    // on EVERY attempt with the full exception (type, message, stacktrace).
    // These flow through OTel as error logs and error spans automatically.
    opts.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500))
        .Then
        .MoveToErrorQueue();
});

var host = builder.Build();
host.Run();
