using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orders.Infrastructure.Persistence;

namespace IntegrationTests.Infrastructure;

/// <summary>Hosts the real Orders API in memory against the test containers.</summary>
public sealed class OrdersApiFactory(
    string ordersConnectionString,
    string rabbitMqUri,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public async Task<T> QueryAsync<T>(Func<OrdersDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<OrdersDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Orders", ordersConnectionString);
        builder.UseSetting("RabbitMq:Uri", rabbitMqUri);
        builder.UseSetting("RabbitMq:OperationTimeout", "00:00:03");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
