using Epublisher.BookLoaders;
using Epublisher.Exceptions;
using Epublisher.Interfaces;
using Epublisher.Loggers;
using Epublisher.Repositories;
using Epublisher.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Epublisher
{
    public sealed class AppServices
    {
        public SettingsManager Settings { get; }
        public LocalizationManager Localization { get; }
        public ThemeManager Theme { get; }
        public RecentFilesManager RecentFiles { get; }
        public IMessageAlerter MessageAlerter { get; } = null!;
        public BookImportManager BookImporter { get; }
        public EpubExportService BookExporter { get; }
        public CustomHtmlRulesManager CustomHtmlRules { get; }
        public CustomCssTemplatesManager CustomCssTemplates { get; }

        public AppServices(string appDataFolder, IMessageAlerter messageAlerter)
        {
            Directory.CreateDirectory(appDataFolder);
            ConfigureLogging(appDataFolder);
            CrashHandler.Register();
            MessageAlerter = messageAlerter;
            Settings = new SettingsManager(appDataFolder);
            Localization = new LocalizationManager(appDataFolder);
            Localization.LoadLocalizations();
            Theme = new ThemeManager(appDataFolder);
            Theme.LoadThemePlugins();

            IRecentFilesRepository recentRepo = new JsonRecentFilesRepository(appDataFolder);
            RecentFiles = new RecentFilesManager(recentRepo);

            IHtmlRulesRepository htmlRulesRepo = new HtmlRulesFileRepository(appDataFolder);
            CustomHtmlRules = new CustomHtmlRulesManager(htmlRulesRepo);

            ICssTemplatesRepository cssTemplatesRepo = new CssTemplatesFileRepository(appDataFolder);
            CustomCssTemplates = new CustomCssTemplatesManager(cssTemplatesRepo);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var loaders = new IBookLoader[]{
                new EpubBookLoader(),
                new Fb2BookLoader(),
                new TxtBookLoader(),
                new PdfBookLoader()
            };

            BookImporter = new BookImportManager(appDataFolder, loaders);
            BookExporter = new EpubExportService();
        }
        public void LoadSavedSettings()
        {
            Localization.ApplyLanguage(Localization.LoadSavedLanguage());
            Theme.ApplyTheme(Theme.LoadSavedTheme());
        }
        private void ConfigureLogging(string appDataFolder)
        {
            var logger = new FileLogger(Path.Combine(appDataFolder, "log.txt"));
            LogManager.Instance.AddRule((Exception ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Unhandled crash"); }, LogLevel.Critical, logger);
            LogManager.Instance.AddRule((FileNotFoundException ex, LogLevel level, ILogger log) => { log.Log(level, ex, $"Missing file {ex.FileName}"); }, LogLevel.Error, logger);
            LogManager.Instance.AddRule((IOException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "General Disk Exception"); }, LogLevel.Error, logger);
            LogManager.Instance.AddRule((ArgumentException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Invalid argument provided"); }, LogLevel.Error, logger);
            LogManager.Instance.AddRule((AggregateException ex, LogLevel level, ILogger log) => { foreach (var e in ex.InnerExceptions) log.Log(level, e, "Agregated exception"); }, LogLevel.Error, logger);
            LogManager.Instance.AddRule((CultureNotFoundException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Culture not found in OS"); }, LogLevel.Warning, logger);
            LogManager.Instance.AddRule((UnauthorizedAccessException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Permission for access denied by OS"); }, LogLevel.Warning, logger);
            LogManager.Instance.AddRule((JsonException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Invalid JSON"); }, LogLevel.Warning, logger);
            LogManager.Instance.AddRule((NotSupportedException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Not supported"); }, LogLevel.Warning, logger);
            LogManager.Instance.AddRule((PdfImageExtractionException ex, LogLevel level, ILogger log) => { log.Log(level, ex, "Unable to exctract image from pdf"); }, LogLevel.Debug, logger);
        }
    }
}
