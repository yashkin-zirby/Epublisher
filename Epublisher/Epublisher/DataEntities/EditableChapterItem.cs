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
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullFileName))]
        private string _relativeHref = string.Empty;
        public string FullFilePath { get; set; } = string.Empty;
        [ObservableProperty]
        private string _fileName = string.Empty;
        public string FullFileName => !string.IsNullOrEmpty(RelativeHref)
        ? Path.GetFileName(RelativeHref)
        : $"{FileName}.xhtml";

        [ObservableProperty]
        private string _title = "Новая глава";

        [ObservableProperty]
        private string _bodyHtml = string.Empty;
    }
}
