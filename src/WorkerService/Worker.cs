namespace WorkerService;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private int _heartbeatCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _heartbeatCount++;
            logger.LogInformation("Worker heartbeat #{Count} at {Time}", _heartbeatCount, DateTimeOffset.Now);

            if (_heartbeatCount % 4 == 0)
            {
                logger.LogWarning("Worker periodic warning at heartbeat #{Count}", _heartbeatCount);
            }

            await Task.Delay(2500, stoppingToken);
        }
    }
}
