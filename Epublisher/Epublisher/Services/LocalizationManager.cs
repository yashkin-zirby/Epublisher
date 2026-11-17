using Avalonia;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Epublisher.Services
{
    public class LocalizationManager
    {
        private readonly string _locFolder;
        public Dictionary<string, string> AvailableLanguages { get; } = new();
        private readonly Dictionary<string, AppLocalization> _loadedLocalizations = new();

        public LocalizationManager(string appDataFolder)
        {
            _locFolder = Path.Combine(appDataFolder, "Localizations");
        }

        public void LoadLocalizations()
        {
            if (!Directory.Exists(_locFolder))
            {
                try { 
                    Directory.CreateDirectory(_locFolder);
                } catch(Exception e) { ExceptionProcesser.Process(e); return; }
            }
            var appDir = Path.Combine(AppContext.BaseDirectory, "Localizations");
            if (Directory.Exists(appDir))
            {
                try {
                    foreach (string filePath in Directory.GetFiles(appDir))
                    {
                        string fileName = Path.GetFileName(filePath);
                        string destFile = Path.Combine(_locFolder, fileName);

                        File.Copy(filePath, destFile, true);
                    }
                }
                catch (Exception e)
                {
                    ExceptionProcesser.Process(e);
                }
            }
            AvailableLanguages.Clear();
            _loadedLocalizations.Clear();

            try
            {
                var files = Directory.GetFiles(_locFolder, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(file);
                        var loc = JsonSerializer.Deserialize<AppLocalization>(jsonContent);
                        if (loc != null && !string.IsNullOrEmpty(loc.IsoCode) && loc.IsoCode.Length == 2)
                        {
                            _loadedLocalizations[loc.IsoCode] = loc;
                            AvailableLanguages[loc.IsoCode] = loc.LanguageName;
                        }
                    }
                    catch(Exception e) { ExceptionProcesser.Process(e); }
                }
            }
            catch (Exception e) { ExceptionProcesser.Process(e); }
        }

        public void ApplyLanguage(string isoCode)
        {
            if (Application.Current == null) return;
            isoCode = isoCode.ToLowerInvariant();

            if (!_loadedLocalizations.TryGetValue(isoCode, out var loc))
            {
                loc = _loadedLocalizations.Values.FirstOrDefault();
            }

            if (loc != null)
            {
                foreach (var kvp in loc.Dictionary)
                {
                    Application.Current.Resources[kvp.Key] = kvp.Value;
                }
                try
                {
                    var culture = new CultureInfo(isoCode);
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                }
                catch (Exception e) { ExceptionProcesser.Process(e); }
            }
            App.Services.Settings.SetProperty("UI.Language", isoCode);
        }

        public string LoadSavedLanguage()
        {
            string defaultSysIso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return App.Services.Settings.GetProperty<string>("UI.Language", defaultSysIso) ?? "en";
        }
    }
}
