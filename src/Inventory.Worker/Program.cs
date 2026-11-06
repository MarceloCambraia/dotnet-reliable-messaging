namespace Inventory.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        using var host = builder.Build();
        await host.RunAsync();
    }
}
