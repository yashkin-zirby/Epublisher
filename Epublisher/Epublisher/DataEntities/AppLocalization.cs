using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.DataEntities
{
    public class AppLocalization
    {
        public string IsoCode { get; set; } = string.Empty; //  "ru"
        public string LanguageName { get; set; } = string.Empty; //  "Русский"
        public Dictionary<string, string> Dictionary { get; set; } = new();
    }
}
