using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.DataEntities
{
    public class CustomCssTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string CssCode { get; set; } = string.Empty;
    }
}
