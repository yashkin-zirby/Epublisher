using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Epublisher.ViewModels
{
    internal partial class CreateEpubViewModel : ViewModelBase
    {
        private CancellationTokenSource? _debounceCts;
        private bool _isInitializing;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public RecentFile Book { get; private set; }

        [ObservableProperty]
        private string _bookTitle = "Новая книга";

        [ObservableProperty]
        private string _bookAuthor = "Unknown Author";

        [ObservableProperty]
        private string _bookLanguage = "ru";

        [ObservableProperty]
        private ObservableCollection<EditableChapterItem> _chapters = new();

        [ObservableProperty]
        private EditableChapterItem? _selectedChapter;

        [ObservableProperty]
        private string _chapterHtmlContent = string.Empty;

        [ObservableProperty]
        private string _customCssContent = string.Empty;

        // 0 = Редактор главы, 1 = Редактор CSS стилей
        [ObservableProperty]
        private int _activeEditorTabIndex;
        [ObservableProperty] 
        private ObservableCollection<BookResourceItem> _imageResources = new();

        [ObservableProperty]
        private Uri? _previewUri;

        [ObservableProperty]
        private bool _isSaving;
        [ObservableProperty]
        private bool _isSingleChapterPreview;

        // Выбранная глава для вставки ссылки в оглавление
        [ObservableProperty]
        private EditableChapterItem? _targetChapterForLink;

        // Событие для плавной прокрутки превью к выбранной главе (передает индекс главы)
        public event Action<int>? ScrollPreviewToChapterRequested;

        // События для UI (вставка картинки, перезагрузка WebView, экспорт)
        public event Func<Task<(string FileName, byte[] Bytes)?>>? PickImageRequested;
        public event Func<RecentFile, Task>? ExportBookRequested;
        public event Action? PreviewHtmlRebuilt;

        public CreateEpubViewModel(RecentFile? existingBook = null)
        {
            if (existingBook != null)
            {
                Book = existingBook;
                _ = LoadExistingBookAsync(existingBook);
            }
            else
            {
                Book = new RecentFile
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Новая книга",
                    Author = "Автор",
                    Language = "ru",
                    SourceType = BookSourceType.CreatedInApp,
                    OriginalFormat = ".epub",
                    HasUnsavedChanges = true
                };
                _ = CreateBlankBookAsync();
            }
        }

        partial void OnBookTitleChanged(string value) => ScheduleRebuildPreview();
        partial void OnBookAuthorChanged(string value) => ScheduleRebuildPreview();
        partial void OnBookLanguageChanged(string value) => ScheduleRebuildPreview();
        partial void OnIsSingleChapterPreviewChanged(bool value) => ScheduleRebuildPreview();

        partial void OnSelectedChapterChanged(EditableChapterItem? oldValue, EditableChapterItem? newValue)
        {
            if (oldValue != null)
            {
                oldValue.BodyHtml = ChapterHtmlContent;
            }

            if (newValue != null)
            {
                _isInitializing = true;
                ChapterHtmlContent = newValue.BodyHtml;
                _isInitializing = false;

                if (IsSingleChapterPreview)
                {
                    ScheduleRebuildPreview();
                }
                else
                {
                    int idx = Chapters.IndexOf(newValue);
                    if (idx >= 0)
                    {
                        ScrollPreviewToChapterRequested?.Invoke(idx);
                    }
                }
            }
        }

        partial void OnChapterHtmlContentChanged(string value)
        {
            if (_isInitializing || SelectedChapter == null) return;

            SelectedChapter.BodyHtml = value;
            ScheduleRebuildPreview();
        }

        partial void OnCustomCssContentChanged(string value)
        {
            if (_isInitializing) return;
            ScheduleRebuildPreview();
        }

        private async Task CreateBlankBookAsync()
        {
            try
            {
                _isInitializing = true;
                string booksRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Epublisher", "books", Book.Id);

                Book.InternalEpubPath = booksRoot;

                var initialChapters = new List<EpubChapterDraft>
            {
                new("Глава 1", "<p>Начните писать текст вашей книги здесь...</p>")
            };

                await EpubStructureWriter.WriteUnpackedEpubAsync(
                    Book.InternalEpubPath, Book.Id, Book.Title, Book.Author, Book.Language ?? "ru", initialChapters);

                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
                await LoadExistingBookAsync(Book);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                _isInitializing = false;
            }
        }


        private async Task LoadExistingBookAsync(RecentFile book)
        {
            try
            {
                _isInitializing = true;

                BookTitle = book.Title;
                BookAuthor = book.Author;
                BookLanguage = book.Language ?? "ru";

                string opfPath = Directory.GetFiles(book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException("Файл .opf не найден.");

                string opfDir = Path.GetDirectoryName(opfPath)!;
                XDocument opfDoc = XDocument.Load(opfPath);
                XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

                var manifest = opfDoc.Descendants(opfNs + "item")
                    .Where(x => x.Attribute("id") != null && x.Attribute("href") != null)
                    .ToDictionary(
                        x => x.Attribute("id")!.Value,
                        x => Uri.UnescapeDataString(x.Attribute("href")!.Value));

                var spineIds = opfDoc.Descendants(opfNs + "itemref")
                    .Select(x => x.Attribute("idref")?.Value)
                    .Where(id => id != null && manifest.ContainsKey(id))
                    .ToList();

                var loadedChapters = new ObservableCollection<EditableChapterItem>();
                int index = 1;

                foreach (string? id in spineIds)
                {
                    string relHref = manifest[id!];
                    string fullPath = Path.Combine(opfDir, relHref.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath)) continue;

                    string rawXhtml = await File.ReadAllTextAsync(fullPath);

                    var titleRegexMatch = Regex.Match(rawXhtml, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    string extractedTitle = titleRegexMatch.Success
                        ? titleRegexMatch.Groups[1].Value
                        : $"Глава {index}";

                    var bodyRegexMatch = Regex.Match(rawXhtml, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    string bodyMatch = bodyRegexMatch.Success
                        ? bodyRegexMatch.Groups[1].Value
                        : rawXhtml;

                    // Очищаем возможные остатки <head> или <title> внутри тела главы
                    bodyMatch = Regex.Replace(bodyMatch, @"<head[^>]*>.*?</head>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    bodyMatch = Regex.Replace(bodyMatch, @"<title[^>]*>.*?</title>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    loadedChapters.Add(new EditableChapterItem
                    {
                        Id = id!,
                        RelativeHref = relHref,
                        FullFilePath = fullPath,
                        FileName = Path.GetFileNameWithoutExtension(relHref),
                        Title = WebUtility.HtmlDecode(extractedTitle).Trim(),
                        BodyHtml = EpubTextFormatingHelper.FormatHtml(bodyMatch.Trim())
                    });
                    index++;
                }

                Chapters = loadedChapters;
                SelectedChapter = Chapters.FirstOrDefault();
                if (SelectedChapter != null)
                {
                    ChapterHtmlContent = SelectedChapter.BodyHtml;
                }

                string? cssFile = Directory.GetFiles(opfDir, "*.css", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(cssFile))
                {
                    CustomCssContent = await File.ReadAllTextAsync(cssFile);
                }

                Book.LastMode = LastWorkMode.Editing;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
                await SaveAndRebuildPreviewAsync();
                RefreshImageResources();
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void ScheduleRebuildPreview()
        {
            if (_isInitializing) return;

            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (!token.IsCancellationRequested)
                    {
                        await SaveAndRebuildPreviewAsync();
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    ExceptionProcesser.Process(ex);
                }
            }, token);
        }
        [RelayCommand]
        private void GenerateOrUpdateTocPage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"epub-toc\">");
            sb.AppendLine("  <h1 style=\"text-align: center;\">Оглавление</h1>");
            sb.AppendLine("  <ol style=\"line-height: 1.8;\">");

            foreach (var ch in Chapters)
            {
                if (ch.Id == "toc_page") continue;
                string targetFile = !string.IsNullOrEmpty(ch.FullFileName) ? ch.FullFileName : $"{ch.Id}.xhtml";
                string safeTitle = WebUtility.HtmlEncode(ch.Title);
                sb.AppendLine($"    <li><a href=\"{targetFile}\">{safeTitle}</a></li>");
            }

            sb.AppendLine("  </ol>");
            sb.AppendLine("</div>");

            var existingToc = Chapters.FirstOrDefault(c => c.Id == "toc_page");
            if (existingToc != null)
            {
                existingToc.BodyHtml = sb.ToString();
                SelectedChapter = existingToc;
                ChapterHtmlContent = existingToc.BodyHtml;
            }
            else
            {
                var tocChapter = new EditableChapterItem
                {
                    Id = "toc_page",
                    RelativeHref = "Text/toc_page.xhtml",
                    FileName = "toc_page",
                    Title = "Оглавление",
                    BodyHtml = sb.ToString()
                };
                Chapters.Insert(0, tocChapter);
                SelectedChapter = tocChapter;
            }

            ScheduleRebuildPreview();
        }
        [RelayCommand]
        public async Task SaveAndRebuildPreviewAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                IsSaving = true;

                if (SelectedChapter != null)
                {
                    SelectedChapter.BodyHtml = ChapterHtmlContent;
                }

                string opfPath = Directory.GetFiles(Book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException("Файл .opf не найден.");
                string opfDir = Path.GetDirectoryName(opfPath)!;

                string? cssFile = Directory.GetFiles(opfDir, "*.css", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(cssFile) && !string.IsNullOrEmpty(CustomCssContent))
                {
                    await WriteTextWithRetryAsync(cssFile, CustomCssContent);
                }

                await PersistBookStructureAsync(opfDir);

                string previewPath;
                if (IsSingleChapterPreview && SelectedChapter != null && File.Exists(SelectedChapter.FullFilePath))
                {
                    previewPath = SelectedChapter.FullFilePath;
                }
                else
                {
                    var assembler = new SinglePageEpubAssembler();
                    previewPath = await assembler.BuildSinglePageHtmlAsync(Book.InternalEpubPath);
                }

                Book.Title = BookTitle;
                Book.Author = BookAuthor;
                Book.Language = BookLanguage;
                Book.HasUnsavedChanges = true;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);

                PreviewUri = new Uri(previewPath, UriKind.Absolute);
                PreviewHtmlRebuilt?.Invoke();
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                IsSaving = false;
                _saveLock.Release();
            }
        }
        private static async Task WriteTextWithRetryAsync(string path, string content)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    await File.WriteAllTextAsync(path, content, Encoding.UTF8);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(60);
                }
            }
        }
        public void SelectChapterByHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return;
            string cleanFileName = Path.GetFileName(href.Split('#').First());
            if (string.IsNullOrEmpty(cleanFileName)) return;

            var target = Chapters.FirstOrDefault(c =>
                c.FullFileName.Equals(cleanFileName, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                SelectedChapter = target;
            }
        }
        private async Task PersistBookStructureAsync(string opfDir)
        {
            string textDir = Path.Combine(opfDir, "Text");
            Directory.CreateDirectory(textDir);

            string cssRelHref = "../Styles/style.css";
            string? existingCss = Directory.GetFiles(opfDir, "*.css", SearchOption.AllDirectories).FirstOrDefault();
            if (existingCss != null)
            {
                cssRelHref = "../" + Path.GetRelativePath(opfDir, existingCss).Replace('\\', '/');
            }

            for (int i = 0; i < Chapters.Count; i++)
            {
                var ch = Chapters.ElementAt(i);
                if (string.IsNullOrEmpty(ch.FullFilePath))
                {
                    string fileName = $"ch{i + 1:D3}.xhtml";
                    ch.Id = $"ch{i + 1:D3}";
                    ch.RelativeHref = $"Text/{fileName}";
                    ch.FullFilePath = Path.Combine(textDir, fileName);
                }

                string safeTitle = WebUtility.HtmlEncode(ch.Title);
                string xhtml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<!DOCTYPE html>\n" +
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n" +
                    "<head>\n" +
                    $"  <title>{safeTitle}</title>\n" +
                    $"  <link rel=\"stylesheet\" type=\"text/css\" href=\"{cssRelHref}\"/>\n" +
                    "</head>\n" +
                    "<body>\n" +
                    $"{ch.BodyHtml}\n" +
                    "</body>\n" +
                    "</html>";
                await WriteTextWithRetryAsync(ch.FullFilePath, xhtml);
            }

            string opfPath = Directory.GetFiles(opfDir, "*.opf", SearchOption.TopDirectoryOnly).First();
            XDocument opfDoc = XDocument.Load(opfPath);
            XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();
            XNamespace dcNs = "http://purl.org";

            var meta = opfDoc.Root.Element(opfNs + "metadata");
            if (meta != null)
            {
                SetOrCreateElement(meta, dcNs + "title", BookTitle);
                SetOrCreateElement(meta, dcNs + "creator", BookAuthor);
                SetOrCreateElement(meta, dcNs + "language", BookLanguage);
            }

            var manifest = opfDoc.Root.Element(opfNs + "manifest");
            var spine = opfDoc.Root.Element(opfNs + "spine");
            if (manifest != null && spine != null)
            {
                manifest.Elements(opfNs + "item")
                    .Where(x => x.Attribute("media-type")?.Value == "application/xhtml+xml")
                    .Remove();

                spine.Elements(opfNs + "itemref").Remove();

                foreach (var ch in Chapters)
                {
                    manifest.Add(new XElement(opfNs + "item",
                        new XAttribute("id", ch.Id),
                        new XAttribute("href", ch.RelativeHref),
                        new XAttribute("media-type", "application/xhtml+xml")));

                    spine.Add(new XElement(opfNs + "itemref",
                        new XAttribute("idref", ch.Id)));
                }
            }

            opfDoc.Save(opfPath);
        }

        private static void SetOrCreateElement(XElement parent, XName name, string value)
        {
            var el = parent.Element(name);
            if (el != null) el.Value = value;
            else parent.Add(new XElement(name, value));
        }
        // --- Управление главами ---
        [RelayCommand]
        private void AddChapter()
        {
            int nextNum = Chapters.Count + 1;
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 6);
            var newChapter = new EditableChapterItem
            {
                Id = $"ch_{uniqueId}",
                RelativeHref = $"Text/ch_{uniqueId}.xhtml",
                FileName = $"ch_{uniqueId}",
                Title = $"Глава {nextNum}",
                BodyHtml = $"<h2>Глава {nextNum}</h2>\n<p>Текст новой главы...</p>"
            };

            Chapters.Add(newChapter);
            SelectedChapter = newChapter;
            ScheduleRebuildPreview();
        }
        [RelayCommand]
        private async Task RenameSelectedChapterFileAsync(string? requestedNewName)
        {
            if (SelectedChapter == null || string.IsNullOrWhiteSpace(requestedNewName))
                return;

            string newNameWithoutExt = Path.GetFileNameWithoutExtension(requestedNewName.Trim());
            if (string.IsNullOrWhiteSpace(newNameWithoutExt))
                return;

            string oldFullName = Path.GetFileName(SelectedChapter.FullFilePath);
            string oldNameWithoutExt = Path.GetFileNameWithoutExtension(oldFullName);
            string oldExt = Path.GetExtension(oldFullName);
            if (string.IsNullOrEmpty(oldExt)) oldExt = ".xhtml";

            if (string.Equals(oldNameWithoutExt, newNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            {
                SelectedChapter.FileName = oldNameWithoutExt;
                return;
            }

            string newFullName = $"{newNameWithoutExt}{oldExt}";
            string dir = Path.GetDirectoryName(SelectedChapter.FullFilePath)!;
            string newFullPath = Path.Combine(dir, newFullName);

            if (File.Exists(newFullPath))
            {
                SelectedChapter.FileName = oldNameWithoutExt;
                return;
            }

            await _saveLock.WaitAsync();
            try
            {
                if (File.Exists(SelectedChapter.FullFilePath))
                {
                    File.Move(SelectedChapter.FullFilePath, newFullPath);
                }

                string relDir = Path.GetDirectoryName(SelectedChapter.RelativeHref)?.Replace('\\', '/') ?? "Text";
                SelectedChapter.FullFilePath = newFullPath;
                SelectedChapter.RelativeHref = string.IsNullOrEmpty(relDir) ? newFullName : $"{relDir}/{newFullName}";
                SelectedChapter.FileName = newNameWithoutExt;

                foreach (var ch in Chapters)
                {
                    if (ch == SelectedChapter)
                    {
                        ChapterHtmlContent = ChapterHtmlContent.Replace(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
                        ch.BodyHtml = ChapterHtmlContent;
                    }
                    else if (ch.BodyHtml.Contains(oldFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        ch.BodyHtml = ch.BodyHtml.Replace(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            finally
            {
                _saveLock.Release();
            }

            await SaveAndRebuildPreviewAsync();
        }
        public void RefreshImageResources()
        {
            try
            {
                string opfPath = Directory.GetFiles(Book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
                if (string.IsNullOrEmpty(opfPath)) return;

                string opfDir = Path.GetDirectoryName(opfPath)!;
                XDocument opfDoc = XDocument.Load(opfPath);
                XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

                // Собираем весь HTML всех глав + CSS для проверки, используется ли файл
                string allChaptersHtml = string.Join("\n", Chapters.Select(c => c.BodyHtml)) + "\n" + CustomCssContent;

                // Проверяем также id обложки в метаданных
                string? coverId = opfDoc.Descendants(opfNs + "meta")
                    .FirstOrDefault(m => m.Attribute("name")?.Value == "cover")
                    ?.Attribute("content")?.Value;

                var list = new ObservableCollection<BookResourceItem>();

                foreach (var item in opfDoc.Descendants(opfNs + "item"))
                {
                    string mediaType = item.Attribute("media-type")?.Value ?? string.Empty;
                    if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string id = item.Attribute("id")?.Value ?? string.Empty;
                    string href = Uri.UnescapeDataString(item.Attribute("href")?.Value ?? string.Empty);
                    string fullPath = Path.Combine(opfDir, href.Replace('/', Path.DirectorySeparatorChar));
                    string fileName = Path.GetFileName(href);

                    bool isCover = id == coverId || (item.Attribute("properties")?.Value?.Contains("cover-image") == true);
                    bool isReferenced = isCover || allChaptersHtml.Contains(fileName, StringComparison.OrdinalIgnoreCase);

                    long sizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
                    string sizeStr = sizeBytes >= 1024 * 1024
                        ? $"{sizeBytes / (1024.0 * 1024.0):F1} MB"
                        : $"{Math.Max(1, sizeBytes / 1024)} KB";

                    list.Add(new BookResourceItem
                    {
                        ManifestId = id,
                        RelativeHref = href,
                        FullFilePath = fullPath,
                        FileName = Path.GetFileNameWithoutExtension(href),
                        IsUsed = isReferenced,
                        SizeDisplay = sizeStr
                    });
                }

                ImageResources = list;
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        public async Task RenameImageResourceAsync(BookResourceItem resource, string newFileName)
        {
            if (string.IsNullOrWhiteSpace(newFileName)) return;

            string newNameWithoutExt = Path.GetFileNameWithoutExtension(newFileName.Trim());
            if (string.IsNullOrWhiteSpace(newNameWithoutExt)) return;

            string oldNameWithoutExt = resource.FileName;
            string ext = !string.IsNullOrEmpty(resource.Extension)
                ? resource.Extension
                : Path.GetExtension(resource.FullFilePath);

            if (string.Equals(oldNameWithoutExt, newNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            {
                resource.FileName = oldNameWithoutExt;
                return;
            }

            string oldFullName = $"{oldNameWithoutExt}{ext}";
            string newFullName = $"{newNameWithoutExt}{ext}";

            string dir = Path.GetDirectoryName(resource.FullFilePath)!;
            string newFullPath = Path.Combine(dir, newFullName);
            if (File.Exists(newFullPath))
            {
                resource.FileName = oldNameWithoutExt;
                return;
            }

            await _saveLock.WaitAsync();
            try
            {
                if (File.Exists(resource.FullFilePath))
                {
                    File.Move(resource.FullFilePath, newFullPath);
                }

                string relDir = Path.GetDirectoryName(resource.RelativeHref)?.Replace('\\', '/') ?? "Images";
                string newRelHref = string.IsNullOrEmpty(relDir) ? newFullName : $"{relDir}/{newFullName}";

                string opfPath = Directory.GetFiles(Book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).First();
                XDocument opfDoc = XDocument.Load(opfPath);
                XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

                var manifestItem = opfDoc.Descendants(opfNs + "item")
                    .FirstOrDefault(x => x.Attribute("id")?.Value == resource.ManifestId);
                if (manifestItem != null)
                {
                    manifestItem.SetAttributeValue("href", newRelHref);
                    opfDoc.Save(opfPath);
                }

                foreach (var ch in Chapters)
                {
                    if (ch == SelectedChapter)
                    {
                        ChapterHtmlContent = ChapterHtmlContent.Replace(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
                        ch.BodyHtml = ChapterHtmlContent;
                    }
                    else if (ch.BodyHtml.Contains(oldFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        ch.BodyHtml = ch.BodyHtml.Replace(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (CustomCssContent.Contains(oldFullName, StringComparison.OrdinalIgnoreCase))
                {
                    CustomCssContent = CustomCssContent.Replace(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
                }

                resource.FullFilePath = newFullPath;
                resource.RelativeHref = newRelHref;
                resource.FileName = newNameWithoutExt;
            }
            finally
            {
                _saveLock.Release();
            }

            await SaveAndRebuildPreviewAsync();
            RefreshImageResources();
        }

        [RelayCommand]
        private async Task DeleteUnusedResourcesAsync()
        {
            RefreshImageResources();

            await _saveLock.WaitAsync();
            try
            {
                string opfPath = Directory.GetFiles(Book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).First();
                string opfDir = Path.GetDirectoryName(opfPath)!;
                XDocument opfDoc = XDocument.Load(opfPath);
                XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

                var unusedImages = ImageResources.Where(r => !r.IsUsed).ToList();
                foreach (var img in unusedImages)
                {
                    if (File.Exists(img.FullFilePath))
                    {
                        File.Delete(img.FullFilePath);
                    }

                    opfDoc.Descendants(opfNs + "item")
                        .Where(x => x.Attribute("id")?.Value == img.ManifestId)
                        .Remove();
                }

                string textDir = Path.Combine(opfDir, "Text");
                if (Directory.Exists(textDir))
                {
                    var activePaths = Chapters
                        .Select(c => Path.GetFullPath(c.FullFilePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (string file in Directory.GetFiles(textDir, "*.xhtml"))
                    {
                        if (!activePaths.Contains(Path.GetFullPath(file)))
                        {
                            File.Delete(file);
                        }
                    }
                }

                opfDoc.Save(opfPath);
            }
            finally
            {
                _saveLock.Release();
            }

            RefreshImageResources();
            await SaveAndRebuildPreviewAsync();
        }
        [RelayCommand]
        private void DeleteChapter(EditableChapterItem? chapter)
        {
            if (chapter == null || Chapters.Count <= 1) return;

            int idx = Chapters.IndexOf(chapter);
            if (File.Exists(chapter.FullFilePath))
            {
                File.Delete(chapter.FullFilePath);
            }

            Chapters.Remove(chapter);
            SelectedChapter = Chapters.ElementAt(Math.Clamp(idx - 1, 0, Chapters.Count - 1));
            ScheduleRebuildPreview();
        }

        [RelayCommand]
        private void MoveChapterUp(EditableChapterItem? chapter)
        {
            if (chapter == null) return;
            int idx = Chapters.IndexOf(chapter);
            if (idx > 0)
            {
                Chapters.Move(idx, idx - 1);
                ScheduleRebuildPreview();
            }
        }

        [RelayCommand]
        private void MoveChapterDown(EditableChapterItem? chapter)
        {
            if (chapter == null) return;
            int idx = Chapters.IndexOf(chapter);
            if (idx >= 0 && idx < Chapters.Count - 1)
            {
                Chapters.Move(idx, idx + 1);
                ScheduleRebuildPreview();
            }
        }
        public async Task<string?> SaveImageAssetToBookAsync(string originalFileName, byte[] imageBytes)
        {
            try
            {
                string opfPath = Directory.GetFiles(Book.InternalEpubPath, "*.opf", SearchOption.AllDirectories).First();
                string opfDir = Path.GetDirectoryName(opfPath)!;
                string imagesDir = Path.Combine(opfDir, "Images");
                Directory.CreateDirectory(imagesDir);

                string ext = Path.GetExtension(originalFileName);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                string safeFileName = $"asset_{DateTime.UtcNow.Ticks}{ext}";
                string fullDestPath = Path.Combine(imagesDir, safeFileName);

                await File.WriteAllBytesAsync(fullDestPath, imageBytes);

                XDocument opfDoc = XDocument.Load(opfPath);
                XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();
                var manifest = opfDoc.Root.Element(opfNs + "manifest");
                if (manifest != null)
                {
                    string mime = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                    string uniqueImgId = $"img_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
                    manifest.Add(new XElement(opfNs + "item",
                        new XAttribute("id", uniqueImgId),
                        new XAttribute("href", $"Images/{safeFileName}"),
                        new XAttribute("media-type", mime)));
                    opfDoc.Save(opfPath);
                }

                return $"../Images/{safeFileName}";
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
                return null;
            }
        }

        [RelayCommand]
        private async Task ExportEpubAsync()
        {
            await SaveAndRebuildPreviewAsync();
            if (ExportBookRequested != null)
            {
                await ExportBookRequested.Invoke(Book);
            }
        }
    }
}
