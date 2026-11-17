using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.Contracts
{
    public interface IThemePlugin
    {
        string ThemeId{ get; }
        bool IsDarkBase { get; }
        ColorPaletteResources GetThemePalette();
    }
}
