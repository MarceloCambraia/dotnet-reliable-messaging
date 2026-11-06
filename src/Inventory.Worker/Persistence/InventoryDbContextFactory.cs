using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Worker.Persistence;

/// <summary>Used only by <c>dotnet ef</c> at design time.</summary>
internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryHost.ConfigureDbContext(options, "Host=localhost;Database=inventory;Username=postgres;Password=postgres");
        return new InventoryDbContext(options.Options);
    }
}
