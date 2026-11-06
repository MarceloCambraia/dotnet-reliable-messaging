using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
