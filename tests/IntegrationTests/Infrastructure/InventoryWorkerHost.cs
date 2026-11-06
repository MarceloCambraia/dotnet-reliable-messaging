using System.Globalization;
using Inventory.Worker;
using Inventory.Worker.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntegrationTests.Infrastructure;

/// <summary>Runs the real Inventory worker composition in-process against the test containers.</summary>
public sealed class InventoryWorkerHost : IAsyncDisposable
{
    private readonly IHost _host;

    private InventoryWorkerHost(IHost host, string queue)
    {
        _host = host;
        Queue = queue;
    }

    public string Queue { get; }

    public IServiceProvider Services => _host.Services;

    public static async Task<InventoryWorkerHost> StartAsync(
        ContainersFixture containers,
        string exchange,
        TimeSpan[]? retryDelays = null,
        string? failOnSku = null)
    {
        var queue = TestNames.Unique("inventory");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Inventory"] = containers.NewDatabase("inventory"),
            ["RabbitMq:Uri"] = containers.RabbitMqUri,
            ["RabbitMq:ClientName"] = "inventory-worker-tests",
            ["Inventory:Exchange"] = exchange,
            ["Inventory:Queue"] = queue,
            ["Inventory:FailOnSku"] = failOnSku,
        };
        var delays = retryDelays ?? [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(300)];
        for (var i = 0; i < delays.Length; i++)
        {
            settings[$"Inventory:RetryDelays:{i}"] = delays[i].ToString("c", CultureInfo.InvariantCulture);
        }

        // Empty builder: no appsettings.json from the test output folder, only what the test sets.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLogging();
        builder.Services.AddInventoryWorker(builder.Configuration);

        var host = builder.Build();
        await host.StartAsync();

        var worker = new InventoryWorkerHost(host, queue);
        await using (var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri))
        {
            await Eventually.GetAsync(() => probe.ConsumerCountAsync(queue), count => count > 0, because: "consumer to start");
        }

        return worker;
    }

    public async Task<T> QueryAsync<T>(Func<InventoryDbContext, Task<T>> query)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<InventoryDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
