using System.Diagnostics;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Diagnostics;
using BuildingBlocks.Messaging.RabbitMq;
using Inventory.Worker.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

    /// <summary>Traces: consumer spans (continuing the producer's trace) and the SQL they run.</summary>
    public static IServiceCollection AddInventoryTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(configuration["OTEL_SERVICE_NAME"] ?? "inventory-worker"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(MessagingTelemetry.ActivitySourceName)
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .EnableRetryOnFailure()
                .ConfigureDataSource(dataSource => dataSource.ConfigureTracing(tracing =>
                    tracing.ConfigureCommandFilter(_ => Activity.Current is not null))))
            .UseSnakeCaseNamingConvention();
}
