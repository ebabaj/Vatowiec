namespace Vatowiec.Operations;

// Pętla usługi: przejmuje kolejne zlecenia z bazy System. Osobny zakres DI na każde zlecenie.
public sealed class OperationsWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Operations:PollSeconds", 5), 1, 300));
        logger.LogInformation("VATowiec Operations Worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<CompanyProvisioningExecutor>().RunNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                // Brak bazy System lub chwilowa awaria SQL: bez treści błędu (może zawierać szczegóły serwera).
                logger.LogWarning("Operations queue unavailable ({ErrorType}); retrying", error.GetType().Name);
            }
            if (!processed)
            {
                try { await Task.Delay(idle, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
