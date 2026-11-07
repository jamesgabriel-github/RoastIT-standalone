namespace RoastIT.App.Components.Shared;

/// <param name="Icon">Optional icon name shown before the label (Settings → Theme).</param>
public sealed record SegmentedOption<TValue>(TValue Value, string Label, string? Icon = null);
