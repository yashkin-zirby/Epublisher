using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Epublisher.Android;

[Activity(
    Label = "Epublisher.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public static Activity? CurrentActivity { get; private set; }

    protected override void OnResume()
    {
        base.OnResume();
        CurrentActivity = this;
    }

    protected override void OnDestroy()
    {
        if (CurrentActivity == this)
        {
            CurrentActivity = null;
        }
        base.OnDestroy();
    }
}
