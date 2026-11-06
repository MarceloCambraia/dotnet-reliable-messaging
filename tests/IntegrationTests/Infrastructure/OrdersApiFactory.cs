using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api.Endpoints;
using Orders.Infrastructure.Outbox;
using Orders.Infrastructure.Persistence;

namespace IntegrationTests.Infrastructure;

/// <summary>Hosts the real Orders API in memory against the test containers.</summary>
public sealed class OrdersApiFactory(
    string ordersConnectionString,
    string rabbitMqUri,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public async Task<T> QueryAsync<T>(Func<OrdersDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<OrdersDbContext>());
    }

    public Task<List<OutboxMessage>> GetOutboxAsync() =>
        QueryAsync(db => db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync());

    public async Task<Guid> CreateOrderAsync(HttpClient client, string sku = "SKU-1", int quantity = 1)
    {
        var response = await client.PostAsJsonAsync("/orders", new CreateOrderRequest("customer-1", [new CreateOrderItemRequest(sku, quantity, 10m)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!.OrderId;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Orders", ordersConnectionString);
        builder.UseSetting("RabbitMq:Uri", rabbitMqUri);
        builder.UseSetting("RabbitMq:OperationTimeout", "00:00:03");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.200");
        builder.UseSetting("Outbox:MaxBrokerBackoff", "00:00:01");
        builder.UseSetting("Outbox:BaseRetryDelay", "00:00:00.200");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
