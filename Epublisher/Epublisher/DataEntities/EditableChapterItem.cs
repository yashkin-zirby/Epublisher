using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Epublisher.DataEntities
{
    public partial class EditableChapterItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string RelativeHref { get; set; } = string.Empty;
        public string FullFilePath { get; set; } = string.Empty;
        public string FileName => string.IsNullOrEmpty(RelativeHref)
        ? string.Empty
        : Path.GetFileName(RelativeHref);

        [ObservableProperty]
        private string _title = "Новая глава";

        [ObservableProperty]
        private string _bodyHtml = string.Empty;
    }
}
