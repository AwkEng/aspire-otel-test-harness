using ApiService;
using AspireOtelTestHarness.Messages;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ProcessingResultStore>();

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

    opts.PublishMessage<ProcessItemCommand>()
        .ToRabbitQueue("process-commands");

    opts.PublishMessage<ProcessItemFailCommand>()
        .ToRabbitQueue("process-commands");

    opts.ListenToRabbitQueue("process-results");
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapPost("/process", async (ProcessItemRequest request, IMessageBus bus) =>
{
    var itemId = Guid.NewGuid();
    await bus.PublishAsync(new ProcessItemCommand(itemId, request.ItemName));
    return Results.Accepted(value: new { itemId });
});

app.MapPost("/process-fail", async (ProcessItemRequest request, IMessageBus bus) =>
{
    var itemId = Guid.NewGuid();
    await bus.PublishAsync(new ProcessItemFailCommand(itemId, request.ItemName));
    return Results.Accepted(value: new { itemId });
});

app.MapGet("/process/{itemId:guid}", (Guid itemId, ProcessingResultStore store) =>
{
    var result = store.Get(itemId);
    return result is not null
        ? Results.Ok(result)
        : Results.NotFound();
});

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record ProcessItemRequest(string ItemName);
