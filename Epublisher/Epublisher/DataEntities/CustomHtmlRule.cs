using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.DataEntities
{
    public class CustomHtmlRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;

        public string OpenTag { get; set; } = string.Empty;

        public string CloseTag { get; set; } = string.Empty;

        public string AssociatedCss { get; set; } = string.Empty;
    }
}
