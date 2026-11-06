using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orders.Domain;
using Orders.Infrastructure.Outbox;

namespace Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(ConfigureOrder);
        modelBuilder.Entity<OutboxMessage>(ConfigureOutbox);
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> order)
    {
        order.ToTable("orders");
        order.HasKey(o => o.Id);
        order.Property(o => o.Id).ValueGeneratedNever();
        order.Property(o => o.CustomerId).HasMaxLength(100).IsRequired();
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        order.Ignore(o => o.Total);

        order.OwnsMany(o => o.Items, item =>
        {
            item.ToTable("order_items");
            item.WithOwner().HasForeignKey("order_id");
            item.Property<int>("id");
            item.HasKey("id");
            item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
            item.Property(i => i.UnitPrice).HasPrecision(18, 2);
            item.Ignore(i => i.Subtotal);
        });
        order.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureOutbox(EntityTypeBuilder<OutboxMessage> outbox)
    {
        outbox.ToTable("outbox_messages");
        outbox.HasKey(m => m.Id);
        outbox.Property(m => m.Id).ValueGeneratedNever();
        outbox.Property(m => m.Type).HasMaxLength(200).IsRequired();
        outbox.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(m => m.CorrelationId).HasMaxLength(100).IsRequired();
        outbox.Property(m => m.TraceParent).HasMaxLength(100);
        outbox.Property(m => m.LastError).HasMaxLength(2000);

        // The publisher only ever scans pending rows; a partial index keeps that scan small no
        // matter how many processed rows accumulate before cleanup.
        outbox.HasIndex(m => m.NextAttemptAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL AND failed_at IS NULL");
    }
}
