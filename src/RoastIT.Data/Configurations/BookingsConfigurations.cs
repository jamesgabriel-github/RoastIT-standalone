using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

// Indexes (D21, Phase 4): orders(pickup_at) for every date-scoped read, items(status) for the status chips, two covering
// indexes on items for the Dashboard totals, and payments(order_key) from AddBookingsDomain.

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_total", "total_amount >= 0");
            t.HasCheckConstraint("ck_orders_discount", "discount_amount >= 0 AND discount_amount <= total_amount");
            t.HasCheckConstraint("ck_orders_paid", "amount_paid >= 0");
        });
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasColumnName("key").ValueGeneratedNever();
        b.Property(x => x.Customer).HasColumnName("customer");
        b.Property(x => x.Phone).HasColumnName("phone");
        b.Property(x => x.OrderDate).HasColumnName("order_date");
        b.Property(x => x.PickupAt).HasColumnName("pickup_at");
        b.Property(x => x.TotalAmount).HasColumnName("total_amount");
        b.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasDefaultValue(0L);
        b.Property(x => x.AmountPaid).HasColumnName("amount_paid").HasDefaultValue(0L);
        b.Property(x => x.Notes).HasColumnName("notes");

        b.HasIndex(x => x.PickupAt).HasDatabaseName("ix_orders_pickup_at");

        b.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderKey).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.OrderKey).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.HasKey(x => x.BookingId);
        b.Property(x => x.BookingId).HasColumnName("booking_id").ValueGeneratedNever();
        b.Property(x => x.OrderKey).HasColumnName("order_key");
        b.Property(x => x.ProductId).HasColumnName("product_id");
        b.Property(x => x.Status).HasColumnName("status");               // ItemStatus as INTEGER, pipeline order
        b.Property(x => x.StartedCookingAt).HasColumnName("started_cooking_at");

        b.HasIndex(x => x.Status).HasDatabaseName("ix_items_status");
        // Covering indexes for the Dashboard's GROUP BYs over every booking (measured with ten years of data, D21). Their
        // leading column is the foreign key, so they also replace EF's IX_items_order_key and IX_items_product_id.
        b.HasIndex(x => new { x.OrderKey, x.Status, x.BookingId }).HasDatabaseName("ix_items_order_status_id");
        b.HasIndex(x => new { x.ProductId, x.Status }).HasDatabaseName("ix_items_product_status");

        // Products are deactivated, never deleted, so there is no cascade.
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amount", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.OrderKey).HasColumnName("order_key");
        b.Property(x => x.Amount).HasColumnName("amount");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
    }
}

public sealed class DailyCounterConfiguration : IEntityTypeConfiguration<DailyCounter>
{
    public void Configure(EntityTypeBuilder<DailyCounter> b)
    {
        b.ToTable("daily_counters");
        b.HasKey(x => x.Date);
        b.Property(x => x.Date).HasColumnName("date").ValueGeneratedNever();
        b.Property(x => x.LastSeq).HasColumnName("last_seq");
    }
}
