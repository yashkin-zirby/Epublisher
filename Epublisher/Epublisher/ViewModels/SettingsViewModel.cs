using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Epublisher.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Epublisher.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ThemeManager _themeManager;
    private readonly LocalizationManager _locManager;

    private KeyValuePair<string, string> _selectedTheme;
    private KeyValuePair<string, string> _selectedLanguage;
    private bool _allowLogDebug;
    private bool _allowLogWarning;
    private bool _allowLogError;
    private bool _allowLog;
    private bool _allowMessageBox;
    private bool _allowMessageBoxOnlyErrors;

    public List<KeyValuePair<string, string>> LanguagesList { get; }

    public ObservableCollection<KeyValuePair<string, string>> ThemesList { get; } = new();

    public SettingsViewModel()
    {
        string folder = App.Services.Settings.AppDataFolder;
        var settings = App.Services.Settings;
        _locManager = new LocalizationManager(folder);
        _locManager.LoadLocalizations();
        LanguagesList = _locManager.AvailableLanguages.ToList();

        string savedLang = _locManager.LoadSavedLanguage();
        _selectedLanguage = LanguagesList.FirstOrDefault(l => l.Key == savedLang);
        if (_selectedLanguage.Key == null) _selectedLanguage = LanguagesList.First();

        _themeManager = new ThemeManager(folder);
        _themeManager.LoadThemePlugins();

        RefreshThemesList();

        string savedTheme = _themeManager.LoadSavedTheme();
        _selectedTheme = ThemesList.FirstOrDefault(t => t.Key == savedTheme);
        if (_selectedTheme.Key == null) _selectedTheme = ThemesList.First();

        _allowLogDebug = settings.GetProperty<bool>("Logging.AllowDebug", false);
        _allowLogWarning = settings.GetProperty<bool>("Logging.AllowWarning", true);
        _allowLogError = settings.GetProperty<bool>("Logging.AllowError", true);
        _allowLog = settings.GetProperty<bool>("Logging.AllowLog", true);
        _allowMessageBox = settings.GetProperty<bool>("Logging.AllowMessageBox", true);
        _allowMessageBoxOnlyErrors = settings.GetProperty<bool>("Logging.AllowMessageBoxOnlyErrors", true);
    }
    public bool AllowLogDebug
    {
        get => _allowLogDebug;
        set
        {
            if (!_allowLogDebug.Equals(value))
            {
                _allowLogDebug = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowDebug", value);
            }
        }
    }
    public bool AllowLogWarning
    {
        get => _allowLogWarning;
        set
        {
            if (!_allowLogWarning.Equals(value))
            {
                _allowLogWarning = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowWarning", value);
            }
        }
    }

    public bool AllowLogError
    {
        get => _allowLogError;
        set
        {
            if (!_allowLogError.Equals(value))
            {
                _allowLogError = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowError", value);
            }
        }
    }

    public bool AllowLog
    {
        get => _allowLog;
        set
        {
            if (!_allowLog.Equals(value))
            {
                _allowLog = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowLog", value);
            }
        }
    }

    public bool AllowMessageBox
    {
        get => _allowMessageBox;
        set
        {
            if (!_allowMessageBox.Equals(value))
            {
                _allowMessageBox = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowMessageBox", value);
            }
        }
    }

    public bool AllowMessageBoxOnlyErrors
    {
        get => _allowMessageBoxOnlyErrors;
        set
        {
            if (!_allowMessageBoxOnlyErrors.Equals(value))
            {
                _allowMessageBoxOnlyErrors = value;
                OnPropertyChanged();
                App.Services.Settings.SetProperty("Logging.AllowMessageBoxOnlyErrors", value);
            }
        }
    }
    public KeyValuePair<string, string> SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!_selectedLanguage.Equals(value) && value.Key != null)
            {
                _selectedLanguage = value;
                OnPropertyChanged();

                _locManager.ApplyLanguage(_selectedLanguage.Key);
                RefreshThemesList();
            }
        }
    }

    public KeyValuePair<string, string> SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!_selectedTheme.Equals(value) && value.Key != null)
            {
                _selectedTheme = value;
                OnPropertyChanged();
                _themeManager.ApplyTheme(_selectedTheme.Key);
            }
        }
    }

    /// <summary>
    /// Вспомогательный метод перевода списка тем в реальном времени
    /// </summary>
    private void RefreshThemesList()
    {
        string? currentSelectedId = _selectedTheme.Key;

        ThemesList.Clear();
        var freshThemes = _themeManager.GetAvailableThemesDetailed();
        foreach (var theme in freshThemes)
        {
            ThemesList.Add(theme);
        }
        if (!string.IsNullOrEmpty(currentSelectedId))
        {
            var restoreSelection = ThemesList.FirstOrDefault(t => t.Key == currentSelectedId);
            if (restoreSelection.Key != null)
            {
                _selectedTheme = restoreSelection;
                OnPropertyChanged(nameof(SelectedTheme));
            }
        }
    }
}
