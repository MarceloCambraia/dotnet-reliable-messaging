using BuildingBlocks.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName));
        services.TryAddSingleton<RabbitMqConnection>();
        services.TryAddSingleton<IRabbitMqConnection>(sp => sp.GetRequiredService<RabbitMqConnection>());
        services.TryAddSingleton<IMessagePublisher, RabbitMqPublisher>();
        return services;
    }

    public static IServiceCollection AddRabbitMqConsumer<THandler>(
        this IServiceCollection services,
        ConsumerQueue queue,
        ushort prefetchCount = 16)
        where THandler : class, IMessageHandler
    {
        services.TryAddScoped<THandler>();
        services.AddHostedService(sp => new RabbitMqConsumer<THandler>(
            sp.GetRequiredService<IRabbitMqConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            queue,
            prefetchCount,
            sp.GetRequiredService<ILogger<RabbitMqConsumer<THandler>>>()));
        return services;
    }

    public static IHealthChecksBuilder AddRabbitMq(
        this IHealthChecksBuilder builder,
        string name = "rabbitmq",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        params string[] tags) =>
        builder.AddCheck<RabbitMqHealthCheck>(name, failureStatus, tags);
}
