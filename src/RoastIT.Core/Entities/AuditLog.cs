using System.Text.Json;

namespace RoastIT.Core.Entities;

/// <summary>
/// Business history (plan §6.2). Details are small JSON objects with amounts or ids;
/// they must never contain a PIN, a hash or a recovery code.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }

    public static AuditLog Create(string action, string targetType, string targetId, DateTime createdAtUtc, object? details = null) => new()
    {
        Id = Guid.NewGuid(),
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Details = details is null ? null : JsonSerializer.Serialize(details),
        CreatedAt = createdAtUtc,
    };

    public static AuditLog PinChanged(DateTime nowUtc) =>
        Create(AuditActions.PinChanged, "app_lock", "1", nowUtc);

    public static AuditLog PinResetWithRecovery(DateTime nowUtc) =>
        Create(AuditActions.PinResetWithRecovery, "app_lock", "1", nowUtc);
}

public static class AuditActions
{
    public const string PinChanged = "pin.changed";
    public const string PinResetWithRecovery = "pin.reset_with_recovery";
    public const string ProductCreated = "product.created";
    public const string ProductUpdated = "product.updated";
    public const string ProductActiveChanged = "product.active_changed";
    public const string DiscountSet = "discount.set";
    public const string PaymentRecorded = "payment.recorded";
    public const string BalanceWaived = "order.balance_waived";
    public const string OrderCancelled = "order.cancelled";
}
