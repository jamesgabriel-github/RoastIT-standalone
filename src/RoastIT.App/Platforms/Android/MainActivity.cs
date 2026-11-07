using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace RoastIT.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // The system "save as" screen of Export → "Save to a folder…" reports back here (D33).
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidBackupFileSaver.OnActivityResult(requestCode, resultCode, data);
    }
}
