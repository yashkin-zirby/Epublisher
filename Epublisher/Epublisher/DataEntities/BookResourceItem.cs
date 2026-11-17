using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Epublisher.DataEntities
{
    public partial class BookResourceItem : ObservableObject
    {
        public string ManifestId { get; set; } = string.Empty;
        public string RelativeHref { get; set; } = string.Empty;
        public string FullFilePath { get; set; } = string.Empty;
        public string Extension => !string.IsNullOrEmpty(RelativeHref)? Path.GetExtension(RelativeHref):Path.GetExtension(FullFilePath);

        [ObservableProperty]
        private string _fileName = string.Empty;

        public string FullFileName => !string.IsNullOrEmpty(RelativeHref)
            ? Path.GetFileName(RelativeHref)
            : $"{FileName}{Extension}";

        [ObservableProperty]
        private bool _isUsed;

        public string SizeDisplay { get; set; } = string.Empty;
    }
}
