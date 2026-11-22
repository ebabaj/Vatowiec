namespace Vatowiec.Operations;

// Pętla usługi: harmonogram kopii co kilka minut i przejmowanie kolejnych zleceń z bazy System.
// Osobny zakres DI na każde zlecenie.
public sealed class OperationsWorker(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock,
    ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Operations:PollSeconds", 5), 1, 300));
        var scheduleEvery = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Operations:ScheduleMinutes", 5), 1, 60));
        var nextSchedule = DateTimeOffset.MinValue;
        logger.LogInformation("VATowiec Operations Worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                if (clock.GetUtcNow() >= nextSchedule)
                {
                    await using var scheduleScope = scopes.CreateAsyncScope();
                    await scheduleScope.ServiceProvider.GetRequiredService<BackupScheduler>().EnqueueDue(stoppingToken);
                    await scheduleScope.ServiceProvider.GetRequiredService<BackupImportScanner>().Scan(stoppingToken);
                    nextSchedule = clock.GetUtcNow() + scheduleEvery;
                }
                await using var scope = scopes.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<CompanyOperationExecutor>().RunNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException
                or Vatowiec.Core.Infrastructure.Operations.CompanyOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
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
