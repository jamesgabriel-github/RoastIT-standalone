namespace RoastIT.App.Services;

/// <summary>
/// Tells Razor components that the app window came back to the front (<c>Window.Resumed</c>), so a screen
/// that shows time-dependent text can refresh straight away instead of waiting for its next tick.
/// </summary>
public sealed class AppForeground
{
    /// <summary>Raised when the window resumes (possibly off the UI thread).</summary>
    public event Action? Resumed;

    public void RaiseResumed() => Resumed?.Invoke();
}
