using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Persistence;

/// <summary>
/// Applies migrations before the consumer starts (hosted services start in registration order).
/// Convenient for the demo; production would run migration bundles in the deployment pipeline.
/// </summary>
internal sealed class DatabaseMigrator(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
