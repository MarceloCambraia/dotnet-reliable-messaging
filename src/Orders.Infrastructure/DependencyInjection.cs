using System.Diagnostics;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Outbox;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Orders";

    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContext<OrdersDbContext>(options => ConfigureDbContext(options, connectionString));

        services.AddScoped<CorrelationContext>();
        services.AddScoped<OrderRepository>();
        services.AddScoped<IOrderRepository>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IOutbox, EfOutbox>();

        services.AddRabbitMqMessaging(configuration);

        var outbox = configuration.GetSection(OutboxOptions.SectionName);
        services.AddOptions<OutboxOptions>().Bind(outbox);
        services.AddScoped<OutboxProcessor>();
        if (outbox.GetValue(nameof(OutboxOptions.Enabled), defaultValue: true))
        {
            services.AddHostedService<OutboxPublisherService>();
        }

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .EnableRetryOnFailure()
                // Only trace SQL that runs inside an existing trace (a request, a publish). Without
                // this, every idle outbox poll would show up in Jaeger as its own root trace.
                .ConfigureDataSource(dataSource => dataSource.ConfigureTracing(tracing =>
                    tracing.ConfigureCommandFilter(_ => Activity.Current is not null))))
            .UseSnakeCaseNamingConvention();
}
