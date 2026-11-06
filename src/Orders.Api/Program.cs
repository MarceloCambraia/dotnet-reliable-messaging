using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orders.Api;
using Orders.Api.Endpoints;
using Orders.Application;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services
    .AddOrdersApplication()
    .AddOrdersInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "orders-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options => options.Filter = context =>
                !context.Request.Path.StartsWithSegments("/health") &&
                !context.Request.Path.StartsWithSegments("/diagnostics"))
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddSource(MessagingTelemetry.ActivitySourceName);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }
    });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>("postgres", tags: ["ready"])
    // The API keeps accepting orders while the broker is down (that is the point of the
    // outbox), so RabbitMQ being unreachable is "Degraded", never a reason to stop traffic.
    .AddRabbitMq("rabbitmq", HealthStatus.Degraded);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.MapOrdersEndpoints();
app.MapDiagnosticsEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });

await app.RunAsync();

/// <summary>Entry point; exposed for <c>WebApplicationFactory</c> in the integration tests.</summary>
public partial class Program;
