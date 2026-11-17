using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Epublisher.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.Theme.DarkTheme
{
    public class DarkThemePlugin : IThemePlugin
    {
        public string ThemeId => "DarkerTheme";
        public bool IsDarkBase => true;

        public ColorPaletteResources GetThemePalette()
        {
            return new ColorPaletteResources
            {
                RegionColor = Color.Parse("#525258"),      // Main background
                Accent = Color.Parse("#568686"),           // Accent color
                BaseHigh = Color.Parse("#FFFFC0"),         // Primary text color
                BaseMedium = Color.Parse("#D4D49E"),       // Secondary text color
                ChromeLow = Color.Parse("#28282E"),        // Dropdowns and popups
                ChromeMedium = Color.Parse("#3A3A42")      // Control backgrounds
            };
        }
    }
}
