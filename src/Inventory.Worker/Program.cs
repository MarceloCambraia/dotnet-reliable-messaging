using Serilog;

namespace Inventory.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        builder.Services.AddInventoryWorker(builder.Configuration);

        using var host = builder.Build();
        await host.RunAsync();
    }
}
