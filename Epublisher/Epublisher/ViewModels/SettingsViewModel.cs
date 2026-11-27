using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epublisher.DataEntities;
using Epublisher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Epublisher.ViewModels;

public partial class SettingsViewModel : ViewModelBase
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
    public ObservableCollection<CustomHtmlRule> HtmlRules { get; } = new(App.Services.CustomHtmlRules.Rules);
    public ObservableCollection<CustomCssTemplate> CssTemplates { get; } = new(App.Services.CustomCssTemplates.Templates);

    [ObservableProperty] private string? _editingHtmlRuleId;
    [ObservableProperty] private string _newHtmlRuleName = string.Empty;
    [ObservableProperty] private string _newHtmlOpenTag = string.Empty;
    [ObservableProperty] private string _newHtmlCloseTag = string.Empty;
    [ObservableProperty] private string _newHtmlAssociatedCss = string.Empty;

    public bool IsEditingHtmlRule => !string.IsNullOrEmpty(EditingHtmlRuleId);
    partial void OnEditingHtmlRuleIdChanged(string? value) => OnPropertyChanged(nameof(IsEditingHtmlRule));

    [ObservableProperty] private string? _editingCssTemplateId;
    [ObservableProperty] private string _newCssTemplateName = string.Empty;
    [ObservableProperty] private string _newCssTemplateCode = string.Empty;

    public bool IsEditingCssTemplate => !string.IsNullOrEmpty(EditingCssTemplateId);
    partial void OnEditingCssTemplateIdChanged(string? value) => OnPropertyChanged(nameof(IsEditingCssTemplate));

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
    [RelayCommand]
    private void BeginEditHtmlRule(CustomHtmlRule? rule)
    {
        if (rule == null) return;
        EditingHtmlRuleId = rule.Id;
        NewHtmlRuleName = rule.Name;
        NewHtmlOpenTag = rule.OpenTag;
        NewHtmlCloseTag = rule.CloseTag;
        NewHtmlAssociatedCss = rule.AssociatedCss;
    }

    [RelayCommand]
    private void CancelEditHtmlRule()
    {
        EditingHtmlRuleId = null;
        NewHtmlRuleName = NewHtmlOpenTag = NewHtmlCloseTag = NewHtmlAssociatedCss = string.Empty;
    }

    [RelayCommand]
    private async Task SaveHtmlRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewHtmlRuleName) || string.IsNullOrWhiteSpace(NewHtmlOpenTag))
            return;

        var rule = new CustomHtmlRule
        {
            Id = EditingHtmlRuleId ?? Guid.NewGuid().ToString("N"),
            Name = NewHtmlRuleName.Trim(),
            OpenTag = NewHtmlOpenTag.Trim(),
            CloseTag = NewHtmlCloseTag.Trim(),
            AssociatedCss = NewHtmlAssociatedCss.Trim()
        };

        await App.Services.CustomHtmlRules.AddOrUpdateAsync(rule);

        int existingIdx = -1;
        for (int i = 0; i < HtmlRules.Count; i++)
        {
            if (HtmlRules[i].Id == rule.Id)
            {
                existingIdx = i;
                break;
            }
        }

        if (existingIdx >= 0)
            HtmlRules[existingIdx] = rule;
        else
            HtmlRules.Add(rule);

        if (!string.IsNullOrWhiteSpace(rule.AssociatedCss))
        {
            string className = ExtractCssClassName(rule.AssociatedCss, rule.Name);
            var existingTpl = CssTemplates.FirstOrDefault(t =>
                t.Name.Equals(className, StringComparison.OrdinalIgnoreCase));

            var cssTpl = new CustomCssTemplate
            {
                Id = existingTpl?.Id ?? Guid.NewGuid().ToString("N"),
                Name = className,
                CssCode = rule.AssociatedCss
            };

            await App.Services.CustomCssTemplates.AddOrUpdateAsync(cssTpl);

            if (existingTpl != null)
            {
                int tplIdx = CssTemplates.IndexOf(existingTpl);
                if (tplIdx >= 0) CssTemplates[tplIdx] = cssTpl;
            }
            else
            {
                CssTemplates.Add(cssTpl);
            }
        }

        CancelEditHtmlRule();
    }

    private static string ExtractCssClassName(string css, string fallbackName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(css, @"\.[a-zA-Z0-9_-]+");
        return match.Success ? match.Value : fallbackName;
    }

    [RelayCommand]
    private async Task DeleteHtmlRuleAsync(CustomHtmlRule? rule)
    {
        if (rule == null) return;
        if (EditingHtmlRuleId == rule.Id) CancelEditHtmlRule();

        await App.Services.CustomHtmlRules.DeleteAsync(rule.Id);
        HtmlRules.Remove(rule);
    }

    [RelayCommand]
    private async Task AddHtmlRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewHtmlRuleName) || string.IsNullOrWhiteSpace(NewHtmlOpenTag))
            return;

        var rule = new CustomHtmlRule
        {
            Name = NewHtmlRuleName.Trim(),
            OpenTag = NewHtmlOpenTag.Trim(),
            CloseTag = NewHtmlCloseTag.Trim(),
            AssociatedCss = NewHtmlAssociatedCss.Trim()
        };

        await App.Services.CustomHtmlRules.AddOrUpdateAsync(rule);
        HtmlRules.Add(rule);

        NewHtmlRuleName = NewHtmlOpenTag = NewHtmlCloseTag = NewHtmlAssociatedCss = string.Empty;
    }

    [RelayCommand]
    private async Task AddCssTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCssTemplateName) || string.IsNullOrWhiteSpace(NewCssTemplateCode))
            return;

        var tpl = new CustomCssTemplate
        {
            Name = NewCssTemplateName.Trim(),
            CssCode = NewCssTemplateCode.Trim()
        };

        await App.Services.CustomCssTemplates.AddOrUpdateAsync(tpl);
        CssTemplates.Add(tpl);

        NewCssTemplateName = NewCssTemplateCode = string.Empty;
    }

    [RelayCommand]
    private void BeginEditCssTemplate(CustomCssTemplate? tpl)
    {
        if (tpl == null) return;
        EditingCssTemplateId = tpl.Id;
        NewCssTemplateName = tpl.Name;
        NewCssTemplateCode = tpl.CssCode;
    }

    [RelayCommand]
    private void CancelEditCssTemplate()
    {
        EditingCssTemplateId = null;
        NewCssTemplateName = NewCssTemplateCode = string.Empty;
    }

    [RelayCommand]
    private async Task SaveCssTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCssTemplateName) || string.IsNullOrWhiteSpace(NewCssTemplateCode))
            return;

        var tpl = new CustomCssTemplate
        {
            Id = EditingCssTemplateId ?? Guid.NewGuid().ToString("N"),
            Name = NewCssTemplateName.Trim(),
            CssCode = NewCssTemplateCode.Trim()
        };

        await App.Services.CustomCssTemplates.AddOrUpdateAsync(tpl);

        int existingIdx = -1;
        for (int i = 0; i < CssTemplates.Count; i++)
        {
            if (CssTemplates[i].Id == tpl.Id)
            {
                existingIdx = i;
                break;
            }
        }

        if (existingIdx >= 0)
            CssTemplates[existingIdx] = tpl;
        else
            CssTemplates.Add(tpl);

        CancelEditCssTemplate();
    }

    [RelayCommand]
    private async Task DeleteCssTemplateAsync(CustomCssTemplate? tpl)
    {
        if (tpl == null) return;
        if (EditingCssTemplateId == tpl.Id) CancelEditCssTemplate();

        await App.Services.CustomCssTemplates.DeleteAsync(tpl.Id);
        CssTemplates.Remove(tpl);
    }
}
