using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using Epublisher.Loggers;
using Epublisher.ViewModels;
using Epublisher.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UglyToad.PdfPig.Logging;

namespace Epublisher;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public static void InitServices(string appDataFolder, IMessageAlerter alerter)
    {
        if (Services != null) return;
        Services = new AppServices(appDataFolder, alerter);
    }
    public override void Initialize()
    {
        if (Services == null)
        {
            throw new InvalidOperationException(
                "App.InitServices() must be called in Program.Main (Desktop) or Application.OnCreate (Android) before starting Avalonia.");
        }
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }
    public override void OnFrameworkInitializationCompleted()
    {

        Services.LoadSavedSettings();
        _ = InitializeAsyncServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
                Width = Services.Settings.GetProperty<double>("UI.Width", 800),
                Height = Services.Settings.GetProperty<double>("UI.Height", 600)
            };
            desktop.MainWindow.Closing += (object? sender, WindowClosingEventArgs e) =>
            {
                Services.Settings.SetProperty("UI.Width", desktop.MainWindow.Width);
                Services.Settings.SetProperty("UI.Height", desktop.MainWindow.Height);
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new PageNavigationHost()
            {
                Page = new MainView { DataContext = new MainViewModel() }
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new PageNavigationHost()
            {
                Page = new MainView { DataContext = new MainViewModel() }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    private static async Task InitializeAsyncServices()
    {
        try
        {
            await Services.RecentFiles.InitializeAsync();
        }
        catch (Exception ex)
        {
            ExceptionProcesser.Process(ex);
        }
    }
}