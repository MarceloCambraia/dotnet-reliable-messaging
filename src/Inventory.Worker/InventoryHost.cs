using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.RabbitMq;
using Inventory.Worker.Persistence;
using Microsoft.EntityFrameworkCore;
using Orders.Contracts;

namespace Inventory.Worker;

/// <summary>Service composition shared by <see cref="Program"/> and the integration tests.</summary>
public static class InventoryHost
{
    public const string ConnectionStringName = "Inventory";

    public static IServiceCollection AddInventoryWorker(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        var section = configuration.GetSection(InventoryOptions.SectionName);
        services.AddOptions<InventoryOptions>().Bind(section);
        var settings = section.Get<InventoryOptions>() ?? new InventoryOptions();

        services.AddDbContext<InventoryDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddSingleton(TimeProvider.System);

        // Registered first: hosted services start in order, so the schema exists before consuming.
        services.AddHostedService<DatabaseMigrator>();

        services.AddRabbitMqMessaging(configuration);
        services.AddRabbitMqConsumer<OrderCreatedHandler>(
            new ConsumerQueue(settings.Queue, settings.Exchange, [OrderCreated.MessageType], settings.EffectiveRetryDelays),
            settings.PrefetchCount);

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention();
}
