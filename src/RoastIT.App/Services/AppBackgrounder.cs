namespace RoastIT.App.Services;

/// <summary>Sends the app to the background (Android Back on the lock screen never reveals pages).</summary>
public interface IAppBackgrounder
{
    void MoveToBackground();
}

public sealed class AppBackgrounder : IAppBackgrounder
{
    public void MoveToBackground()
    {
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
    }
}
