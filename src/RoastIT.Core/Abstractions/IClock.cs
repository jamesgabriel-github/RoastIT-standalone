namespace RoastIT.Core.Abstractions;

/// <summary>The only source of "now" for Core and Data. Always UTC.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
