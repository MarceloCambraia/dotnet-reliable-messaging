using System.Diagnostics;
using Orders.Infrastructure;
using Serilog.Context;

namespace Orders.Api;

/// <summary>
/// Accepts an incoming <c>X-Correlation-Id</c> (or creates one), stores it for the outbox,
/// echoes it back and adds it to every log line of the request.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaxLength = 100;

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        var incoming = context.Request.Headers[CorrelationContext.HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(incoming) && incoming.Length <= MaxLength)
        {
            correlation.CorrelationId = incoming;
        }

        context.Response.Headers[CorrelationContext.HeaderName] = correlation.CorrelationId;
        Activity.Current?.SetTag("correlation.id", correlation.CorrelationId);

        using (LogContext.PushProperty("CorrelationId", correlation.CorrelationId))
        {
            await next(context);
        }
    }
}
