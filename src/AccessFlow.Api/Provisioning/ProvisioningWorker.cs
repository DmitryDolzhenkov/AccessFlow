using AccessFlow.AccessRequests;
using Microsoft.Extensions.Options;

namespace AccessFlow.Api.Provisioning;

/// <summary>
/// Runs Provisioning in the background of the same process (BR-16, ADR 0002):
/// polls the outbox in the database, so it also picks up entries left before a restart.
/// </summary>
public sealed class ProvisioningWorker(
    IServiceScopeFactory scopeFactory, IOptions<ProvisioningOptions> options, TimeProvider timeProvider, ILogger<ProvisioningWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ProvisioningService>().ProcessPendingAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // E.g. the database is unavailable; the next poll tries again.
                logger.LogError(e, "Polling the Provisioning outbox failed.");
            }

            await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }
}
