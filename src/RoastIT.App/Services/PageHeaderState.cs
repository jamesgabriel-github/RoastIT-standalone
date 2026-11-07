namespace RoastIT.App.Services;

/// <summary>
/// What the layout's top bar shows. Each page sets it through <c>&lt;PageHeading&gt;</c>;
/// <c>MainLayout</c> owns one instance and cascades it.
/// </summary>
public sealed class PageHeaderState
{
    public string Title { get; private set; } = "";
    public string Subtitle { get; private set; } = "";

    /// <summary>False on Settings (no FAB), which gives the page less bottom padding.</summary>
    public bool HasFab { get; private set; } = true;

    public event Action? Changed;

    public void Set(string title, string subtitle, bool hasFab)
    {
        if (Title == title && Subtitle == subtitle && HasFab == hasFab) return;
        Title = title;
        Subtitle = subtitle;
        HasFab = hasFab;
        Changed?.Invoke();
    }
}
