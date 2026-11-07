namespace RoastIT.App.Components.Shared;

/// <summary>A filter chip. <paramref name="Label"/> already includes the global count, e.g. "Active 4".</summary>
public sealed record ChipItem(string Key, string Label);
