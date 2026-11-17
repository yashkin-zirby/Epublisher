using Avalonia;
using Epublisher.Interfaces;
using Epublisher.Services;
using System;
using System.IO;

namespace Epublisher.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        string desktopPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Epublisher");
        App.InitServices(desktopPath, new DesktopServices.DesktopMessageAlerter());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
