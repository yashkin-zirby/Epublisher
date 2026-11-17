using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Epublisher.DataEntities
{
    public enum BookSourceType
    {
        CreatedInApp, 
        ImportedConverted,
        OpenedEpub      
    }

    public enum LastWorkMode
    {
        Reading,
        Editing
    }

    public class RecentFile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string InternalEpubPath { get; set; } = string.Empty;
        public string? ExternalUriOrPath { get; set; }
        public string? PlatformBookmark { get; set; }
        public string Title { get; set; } = "Untitled";
        public string Author { get; set; } = "Unknown Author";
        public string? Language { get; set; }
        public string? CoverThumbnailPath { get; set; }
        public BookSourceType SourceType { get; set; } = BookSourceType.OpenedEpub;
        public string OriginalFormat { get; set; } = ".epub";// E.g., ".pdf", ".fb2", ".docx", ".txt", or ".epub"
        public string? LastChapterHref { get; set; }
        public double ReadingProgress { get; set; }
        public LastWorkMode LastMode { get; set; } = LastWorkMode.Reading;
        public string? LastEditedInternalFile { get; set; }
        public int LastEditorCursorPosition { get; set; }
        public bool HasUnsavedChanges { get; set; }
        public DateTime LastAccessedUtc { get; set; } = DateTime.UtcNow;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public bool IsPinned { get; set; }
        [JsonIgnore]
        public string ReaderHtmlPath
        {
            get
            {
                if (string.IsNullOrEmpty(InternalEpubPath) || !Directory.Exists(InternalEpubPath))
                    return string.Empty;

                return Directory.GetFiles(InternalEpubPath, "reader_full.html", SearchOption.AllDirectories)
                           .FirstOrDefault()
                       ?? Path.Combine(InternalEpubPath, "reader_full.html");
            }
        }
    }
}
