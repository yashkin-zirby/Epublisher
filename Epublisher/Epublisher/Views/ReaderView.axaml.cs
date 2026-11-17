using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Epublisher.Helpers;
using Epublisher.ViewModels;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Epublisher.Views
{
    public partial class ReaderView : ContentPage
    {
        private double _fontScale = 100.0;
        private bool _isPageLoaded = false;
        private DispatcherTimer? _progressSaveTimer;

        public ReaderView()
        {
            InitializeComponent();
            double savedScale = App.Services.Settings.GetProperty<double>("Reader.FontScale", 100.0);
            _fontScale = savedScale >= 70.0 && savedScale <= 200.0 ? savedScale : 100.0;
            _progressSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _progressSaveTimer.Tick += async (_, _) => await CaptureAndSaveScrollProgressAsync();
            EpubWebView.NavigationCompleted += async (_, args) =>
            {
                if (args.IsSuccess)
                {
                    _isPageLoaded = true;
                    await ApplyCurrentAppThemeAsync();
                    if (Math.Abs(_fontScale - 100.0) > 0.1)
                    {
                        await ApplyFontScaleAsync(saveToSettings: false);
                    }
                    await RestoreSavedScrollPositionAsync();
                    _progressSaveTimer?.Start();
                }
            };
            EpubWebView.AdapterCreated += (_, args) =>
            {
            #if ANDROID
            if (args.Adapter?.PlatformView is Android.Webkit.WebView androidWebView)
            {
                androidWebView.Settings.AllowFileAccess = true;
                androidWebView.Settings.AllowFileAccessFromFileURLs = true;
            }
            #endif
            };
        }
        private async void OnAppThemeChanged(object? sender, EventArgs e)
        {
            await ApplyCurrentAppThemeAsync();
        }
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged += OnAppThemeChanged;
        }

        protected override async void OnUnloaded(RoutedEventArgs e)
        {
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged -= OnAppThemeChanged;
            _progressSaveTimer?.Stop();
            if (_isPageLoaded)
            {
                await CaptureAndSaveScrollProgressAsync();
            }
            _isPageLoaded = false;
            EpubWebView.Source = null;
            base.OnUnloaded(e);
        }

        private async Task RestoreSavedScrollPositionAsync()
        {
            try
            {
                if (DataContext is not ReaderViewModel vm || vm.ReadingProgress <= 0.005)
                    return;

                await Task.Delay(400);

                string progressStr = vm.ReadingProgress.ToString(CultureInfo.InvariantCulture);
                string script = "(function() {" +
                    "  var target = document.scrollingElement || document.documentElement;" +
                    "  var maxScroll = target.scrollHeight - target.clientHeight;" +
                    "  if (maxScroll > 0) {" +
                    "    target.scrollTo({ top: maxScroll * " + progressStr + ", behavior: 'instant' });" +
                    "  }" +
                    "})();";

                await EpubWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }
        private async Task ApplyCurrentAppThemeAsync()
        {
            if (!_isPageLoaded)
                return;

            try
            {
                var themeVariant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
                string bgHex = ResolveResourceColorHex(
                    new[] { "ReaderBackgroundBrush", "SystemControlBackgroundAltHighBrush", "SystemRegionBrush" },
                    themeVariant,
                    themeVariant == ThemeVariant.Dark ? "#18181b" : "#ffffff");

                string fgHex = ResolveResourceColorHex(
                    new[] { "ReaderForegroundBrush", "SystemControlForegroundBaseHighBrush" },
                    themeVariant,
                    themeVariant == ThemeVariant.Dark ? "#e4e4e7" : "#1a1a1a");

                string linkHex = ResolveResourceColorHex(
                    new[] { "ReaderLinkBrush", "SystemControlHighlightAccentBrush", "SystemAccentColor" },
                    themeVariant,
                    themeVariant == ThemeVariant.Dark ? "#60a5fa" : "#1d4ed8");
                string script = "(function() {var root = document.documentElement;\n"+
                $"  root.style.setProperty('--reader-bg', '{bgHex}');\n"+
                $"  root.style.setProperty('--reader-fg', '{fgHex}');\n" +
                $"  root.style.setProperty('--reader-link', '{linkHex}');\n" +
                $"  root.setAttribute('data-custom-theme', 'true');\n" +
                "})();";

                await EpubWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }
        private static string ResolveResourceColorHex(string[] resourceKeys, ThemeVariant theme, string fallbackHex)
        {
            if (Application.Current == null) return fallbackHex;

            foreach (string key in resourceKeys)
            {
                if (Application.Current.TryFindResource(key, theme, out object? resource))
                {
                    if (resource is ISolidColorBrush brush)
                    {
                        return CssHelper.ColorToCssHex(brush.Color);
                    }
                    if (resource is Color color)
                    {
                        return CssHelper.ColorToCssHex(color);
                    }
                }
            }

            return fallbackHex;
        }
        private async Task CaptureAndSaveScrollProgressAsync()
        {
            try
            {
                if (!_isPageLoaded || DataContext is not ReaderViewModel vm)
                    return;

                string script = """
                (function() {
                    var maxScroll = document.documentElement.scrollHeight - window.innerHeight;
                    if (maxScroll <= 0) return "0";
                    return (window.scrollY / maxScroll).toString();
                })();
                """;

                string? result = await EpubWebView.InvokeScript(script);
                if (!string.IsNullOrWhiteSpace(result) &&
                    double.TryParse(result.Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out double progress))
                {
                    await vm.UpdateScrollProgressAsync(progress);
                }
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        private async void OnScrollToTopClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                await EpubWebView.InvokeScript("window.scrollTo({ top: 0, behavior: 'smooth' });");
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        private async void OnIncreaseFontClick(object? sender, RoutedEventArgs e)
        {
            _fontScale = Math.Min(200.0, _fontScale + 10.0);
            await ApplyFontScaleAsync(saveToSettings: true);
        }

        private async void OnDecreaseFontClick(object? sender, RoutedEventArgs e)
        {
            _fontScale = Math.Max(70.0, _fontScale - 10.0);
            await ApplyFontScaleAsync(saveToSettings: true);
        }

        private async Task ApplyFontScaleAsync(bool saveToSettings)
        {
            if (!_isPageLoaded) return;
            if (saveToSettings)
            {
                App.Services.Settings.SetProperty("Reader.FontScale", _fontScale);
            }
            try
            {
                string scaleStr = _fontScale.ToString(CultureInfo.InvariantCulture);

                string script = "(function() {\n" +
                    $"   var oldMaxScroll = document.documentElement.scrollHeight - window.innerHeight;\n" +
                    $"   var ratio = oldMaxScroll > 0 ? (window.scrollY / oldMaxScroll) : 0;\n" +
                    $"   document.body.style.fontSize = '{scaleStr}%';\n"+
                    $"   var newMaxScroll = document.documentElement.scrollHeight - window.innerHeight;\n"+
                    $"  if (newMaxScroll > 0) window.scrollTo(0, newMaxScroll * ratio);\n"+
                    "})();";

                await EpubWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

    }
}