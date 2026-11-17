using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.DataEntities
{
    public class ThemeLocalization
    {
        public string ThemeId { get; set; } = string.Empty;
        public Dictionary<string, string> Translations { get; set; } = new();
    }
}
