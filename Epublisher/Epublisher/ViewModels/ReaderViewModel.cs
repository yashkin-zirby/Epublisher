using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Epublisher.ViewModels
{
    internal sealed record ReaderChapterItem(string Title, string AnchorId, string? FragmentId = null);

    internal partial class ReaderViewModel : ViewModelBase
    {
        public RecentFile Book { get; }

        [ObservableProperty]
        private string _bookTitle;

        [ObservableProperty]
        private Uri? _readerSourceUri;

        [ObservableProperty]
        private double _readingProgress;

        [ObservableProperty]
        private string _progressPercentageText = "0%";

        [ObservableProperty]
        private bool _isLoading = true;

        [ObservableProperty]
        private bool _isTocDrawerOpen;

        [ObservableProperty]
        private ReaderChapterItem? _selectedChapter;

        public ObservableCollection<ReaderChapterItem> Chapters { get; } = new();

        public event Action<RecentFile>? EditBookRequested;

        public ReaderViewModel(RecentFile book)
        {
            Book = book ?? throw new ArgumentNullException(nameof(book));
            _bookTitle = book.Title;
            _readingProgress = book.ReadingProgress;
            _progressPercentageText = $"{Math.Round(book.ReadingProgress * 100)}%";

            _ = InitializeReaderAsync();
        }

        [RelayCommand]
        private void ToggleTocDrawer() => IsTocDrawerOpen = !IsTocDrawerOpen;

        private async Task InitializeReaderAsync()
        {
            try
            {
                IsLoading = true;
                if (string.IsNullOrEmpty(Book.ReaderHtmlPath) || !File.Exists(Book.ReaderHtmlPath))
                {
                    var assembler = new SinglePageEpubAssembler();
                    await assembler.BuildSinglePageHtmlAsync(Book.InternalEpubPath);
                }

                if (!File.Exists(Book.ReaderHtmlPath))
                {
                    throw new FileNotFoundException("Не удалось найти или собрать файл для чтения.", Book.ReaderHtmlPath);
                }

                await LoadChaptersFromEpubAsync(Book.InternalEpubPath);

                ReaderSourceUri = new Uri(Book.ReaderHtmlPath, UriKind.Absolute);

                Book.LastMode = LastWorkMode.Reading;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }
        private async Task LoadChaptersFromEpubAsync(string internalEpubPath)
        {
            Chapters.Clear();
            if (string.IsNullOrWhiteSpace(internalEpubPath) || !Directory.Exists(internalEpubPath))
                return;

            string? opfPath = Directory.GetFiles(internalEpubPath, "*.opf", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(opfPath))
                return;

            string opfDir = Path.GetDirectoryName(opfPath)!;
            XDocument opfDoc = XDocument.Load(opfPath);
            XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

            var manifestItems = opfDoc.Descendants(opfNs + "item")
                .Select(x => new
                {
                    Id = x.Attribute("id")?.Value ?? string.Empty,
                    Href = Uri.UnescapeDataString(x.Attribute("href")?.Value ?? string.Empty)
                })
                .Where(x => !string.IsNullOrEmpty(x.Id))
                .ToDictionary(x => x.Id, x => x.Href);

            var spineHrefs = opfDoc.Descendants(opfNs + "itemref")
                .Select(x => x.Attribute("idref")?.Value)
                .Where(id => id != null && manifestItems.ContainsKey(id))
                .Select(id => manifestItems[id!])
                .ToList();

            // Exact same mapping as SinglePageEpubAssembler: normalized OPF-relative href -> "epub-sec-{i}"
            var hrefToSectionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < spineHrefs.Count; i++)
            {
                string normHref = spineHrefs[i].Replace('\\', '/').TrimStart('/');
                hrefToSectionId[normHref] = $"epub-sec-{i}";
            }

            string ResolveAnchorId(string baseDirRelativeToOpf, string rawHref)
            {
                string decoded = Uri.UnescapeDataString(rawHref);
                string[] parts = decoded.Split('#', 2);
                string relFile = parts.FirstOrDefault() ?? string.Empty;
                string? fragment = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts.Last()) ? parts.Last() : null;

                string combined = string.IsNullOrEmpty(baseDirRelativeToOpf)
                    ? relFile
                    : $"{baseDirRelativeToOpf}/{relFile}";

                // Resolve ".." segments cleanly
                var stack = new Stack<string>();
                foreach (var seg in combined.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (seg == ".") continue;
                    if (seg == ".." && stack.Count > 0) stack.Pop();
                    else if (seg != "..") stack.Push(seg);
                }
                string normTarget = string.Join("/", stack.Reverse());

                if (hrefToSectionId.TryGetValue(normTarget, out string? secId))
                {
                    return !string.IsNullOrEmpty(fragment) ? $"{secId}_{fragment}" : secId;
                }

                // Fallback: match by file name if path prefix differs
                var fileMatch = hrefToSectionId.FirstOrDefault(kv =>
                    Path.GetFileName(kv.Key).Equals(Path.GetFileName(normTarget), StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(fileMatch.Value))
                {
                    return !string.IsNullOrEmpty(fragment) ? $"{fileMatch.Value}_{fragment}" : fileMatch.Value;
                }

                return fragment ?? string.Empty;
            }

            XNamespace xhtml = "http://w3.org";
            XNamespace epub = "http://idpf.org";

            // 1. Read EPUB 3 nav.xhtml
            string? navPath = Directory.GetFiles(internalEpubPath, "nav.xhtml", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(navPath) && File.Exists(navPath))
            {
                string navDirRel = Path.GetRelativePath(opfDir, Path.GetDirectoryName(navPath)!).Replace('\\', '/');
                if (navDirRel == ".") navDirRel = string.Empty;

                var doc = XDocument.Parse(await File.ReadAllTextAsync(navPath));
                var tocNav = doc.Descendants(xhtml + "nav")
                    .FirstOrDefault(n => (string?)n.Attribute(epub + "type") == "toc")
                    ?? doc.Descendants(xhtml + "nav").FirstOrDefault();

                if (tocNav != null)
                {
                    foreach (var link in tocNav.Descendants(xhtml + "a"))
                    {
                        string title = Regex.Replace(link.Value, @"\s+", " ").Trim();
                        string href = ((string?)link.Attribute("href") ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(href)) continue;

                        string anchorId = ResolveAnchorId(navDirRel, href);
                        if (!string.IsNullOrEmpty(anchorId))
                            Chapters.Add(new ReaderChapterItem(title, anchorId));
                    }
                }
            }

            // 2. Fallback to EPUB 2 toc.ncx
            if (Chapters.Count == 0)
            {
                string? ncxPath = Directory.GetFiles(internalEpubPath, "*.ncx", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(ncxPath) && File.Exists(ncxPath))
                {
                    string ncxDirRel = Path.GetRelativePath(opfDir, Path.GetDirectoryName(ncxPath)!).Replace('\\', '/');
                    if (ncxDirRel == ".") ncxDirRel = string.Empty;

                    var ncxDoc = XDocument.Parse(await File.ReadAllTextAsync(ncxPath));
                    XNamespace ncx = ncxDoc.Root?.GetDefaultNamespace() ?? "http://daisy.org";

                    foreach (var navPoint in ncxDoc.Descendants(ncx + "navPoint"))
                    {
                        string title = Regex.Replace(
                            navPoint.Element(ncx + "navLabel")?.Element(ncx + "text")?.Value ?? string.Empty,
                            @"\s+", " ").Trim();
                        string src = ((string?)navPoint.Element(ncx + "content")?.Attribute("src") ?? string.Empty).Trim();

                        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(src)) continue;

                        string anchorId = ResolveAnchorId(ncxDirRel, src);
                        if (!string.IsNullOrEmpty(anchorId))
                            Chapters.Add(new ReaderChapterItem(title, anchorId));
                    }
                }
            }
        }

        private void AddChapterItemFromHref(string title, string href)
        {
            string decodedHref = Uri.UnescapeDataString(href);
            string[] parts = decodedHref.Split('#', 2);
            string filePart = parts.FirstOrDefault() ?? string.Empty;
            string? fragment = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts.Last()) ? parts.Last() : null;

            string anchorId = Path.GetFileNameWithoutExtension(filePart);
            if (!string.IsNullOrEmpty(anchorId) || !string.IsNullOrEmpty(fragment))
            {
                Chapters.Add(new ReaderChapterItem(title, anchorId, fragment));
            }
        }

        public async Task UpdateScrollProgressAsync(double progress)
        {
            try
            {
                double clamped = Math.Clamp(progress, 0.0, 1.0);
                if (Math.Abs(clamped - ReadingProgress) < 0.01) return;

                ReadingProgress = clamped;
                ProgressPercentageText = $"{Math.Round(clamped * 100)}%";

                Book.ReadingProgress = clamped;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        [RelayCommand]
        private void EditCurrentBook() => EditBookRequested?.Invoke(Book);
    }
}
