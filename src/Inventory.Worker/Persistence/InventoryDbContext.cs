using Inventory.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Sku);
            product.Property(p => p.Sku).HasMaxLength(64);
            product.Property(p => p.Name).HasMaxLength(200);
            product.ToTable(t => t.HasCheckConstraint("ck_products_available_not_negative", "available_quantity >= 0"));
            product.HasData(
                Product.Create("SKU-1", "Mechanical keyboard", 100),
                Product.Create("SKU-2", "Wireless mouse", 100),
                Product.Create("SKU-3", "USB-C hub", 50),
                Product.Create("SKU-4", "4K monitor", 10),
                Product.Create("SKU-5", "Laptop stand", 0));
        });

        modelBuilder.Entity<StockReservation>(reservation =>
        {
            reservation.ToTable("stock_reservations");
            reservation.HasKey(r => r.OrderId);
            reservation.Property(r => r.OrderId).ValueGeneratedNever();
            reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            reservation.Property(r => r.RejectionReason).HasMaxLength(500);
            reservation.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("stock_reservation_lines");
                line.WithOwner().HasForeignKey("order_id");
                line.Property<int>("id");
                line.HasKey("id");
                line.Property(l => l.Sku).HasMaxLength(64);
            });
            reservation.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_messages");
            inbox.HasKey(m => m.MessageId);
            inbox.Property(m => m.MessageId).ValueGeneratedNever();
            inbox.Property(m => m.Type).HasMaxLength(200);
        });
    }
}
