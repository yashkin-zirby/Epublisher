using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Epublisher;
using Epublisher.Contracts;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

public class ThemeManager
{
    private readonly string _themesFolder;

    public List<IThemePlugin> LoadedPlugins { get; } = new();
    private readonly Dictionary<string, ThemeLocalization> _pluginLocalizations = new();

    public ThemeManager(string appDataFolder)
    {
        _themesFolder = Path.Combine(appDataFolder, "Themes");
    }

    public void LoadThemePlugins()
    {
        if (!Directory.Exists(_themesFolder))
        {
            try { Directory.CreateDirectory(_themesFolder); } catch(Exception e) { ExceptionProcesser.Process(e); return; }
        }

        try
        {
            var dlls = Directory.GetFiles(_themesFolder, "*.dll");
            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    IEnumerable<Type> types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null)!;
                    }
                    var pluginTypes = types.Where(t => typeof(IThemePlugin).IsAssignableFrom(t) && !t.IsInterface);

                    foreach (var type in pluginTypes)
                    {
                        if (Activator.CreateInstance(type) is IThemePlugin plugin)
                        {
                            if (!LoadedPlugins.Any(p => p.ThemeId == plugin.ThemeId))
                            {
                                LoadedPlugins.Add(plugin);
                                LoadPluginLocalization(plugin.ThemeId);
                            }
                        }
                    }
                }
                catch(Exception e) { ExceptionProcesser.Process(e); }
            }
        }
        catch (Exception e) { ExceptionProcesser.Process(e); }
    }

    private void LoadPluginLocalization(string themeId)
    {
        string jsonPath = Path.Combine(_themesFolder, $"{themeId}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var locData = JsonSerializer.Deserialize<ThemeLocalization>(jsonContent);
                if (locData != null)
                {
                    _pluginLocalizations[themeId] = locData;
                }
            }
            catch (Exception e) { ExceptionProcesser.Process(e); }
        }
    }

    /// <summary>
    /// Генерирует актуальный словарь [ID -> Имя] на основе текущего активного языка
    /// </summary>
    public Dictionary<string, string> GetAvailableThemesDetailed()
    {
        var result = new Dictionary<string, string>();
        string currentLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        result.Add("System", GetAppResource("Theme.System", "System"));
        result.Add("Dark", GetAppResource("Theme.Dark", "Dark"));
        result.Add("Light", GetAppResource("Theme.Light", "Light"));
        foreach (var plugin in LoadedPlugins)
        {
            string displayName = plugin.ThemeId;

            if (_pluginLocalizations.TryGetValue(plugin.ThemeId, out var locData) && locData.Translations != null)
            {
                if (locData.Translations.TryGetValue(currentLang, out var localizedName))
                {
                    displayName = localizedName;
                }
                else if (locData.Translations.TryGetValue("en", out var englishName))
                {
                    displayName = englishName;
                }
                else if (locData.Translations.Count > 0)
                {
                    displayName = locData.Translations.Values.First();
                }
            }

            result.Add(plugin.ThemeId, displayName);
        }

        return result;
    }
    /// <summary>
    /// Извлекает строку из Application.Current.Resources или возвращает fallback
    /// </summary>
    private static string GetAppResource(string key, string fallback)
    {
        if (Application.Current != null &&
            Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return fallback;
    }
    public void ApplyTheme(string themeId)
    {
        if (Application.Current == null) return;

        var fluentTheme = Application.Current.Styles.OfType<FluentTheme>().FirstOrDefault();

        fluentTheme?.Palettes.Clear();

        if (themeId is "Light" or "Dark" or "System")
        {
            var targetVariant = themeId switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

            Application.Current.RequestedThemeVariant = targetVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            Application.Current.RequestedThemeVariant = targetVariant;
        }
        else
        {
            var plugin = LoadedPlugins.FirstOrDefault(p => p.ThemeId == themeId);
            if (plugin != null && fluentTheme != null)
            {
                var baseVariant = plugin.IsDarkBase ? ThemeVariant.Dark : ThemeVariant.Light;

                fluentTheme.Palettes[baseVariant] = plugin.GetThemePalette();

                Application.Current.RequestedThemeVariant = baseVariant == ThemeVariant.Dark
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = baseVariant;
            }
        }

        App.Services.Settings.SetProperty("UI.Theme", themeId);
    }

    public string LoadSavedTheme()
    {
        return App.Services.Settings.GetProperty<string>("UI.Theme", "System") ?? "System";
    }
}
