using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Orders.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> at design time (creating migrations needs no live database).</summary>
internal sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=orders;Username=postgres;Password=postgres");
        return new OrdersDbContext(options.Options);
    }
}
