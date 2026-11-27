using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Epublisher.Helpers
{
    public record EpubChapterDraft(string Title, string HtmlBodyContent, string? CustomFileName = null);

    public static class EpubStructureWriter
    {
        public static async Task<string?> WriteUnpackedEpubAsync(
            string targetBookFolder,
            string bookId,
            string title,
            string author,
            string language,
            List<EpubChapterDraft> chapters,
            byte[]? coverBytes = null,
            string coverExtension = ".jpg",
            Dictionary<string, byte[]>? extraImages = null,
            string additionalCssStyles = "")
        {
            string metaInfDir = Path.Combine(targetBookFolder, "META-INF");
            string oebpsDir = Path.Combine(targetBookFolder, "OEBPS");
            string textDir = Path.Combine(oebpsDir, "Text");
            string stylesDir = Path.Combine(oebpsDir, "Styles");
            string imagesDir = Path.Combine(oebpsDir, "Images");

            Directory.CreateDirectory(metaInfDir);
            Directory.CreateDirectory(textDir);
            Directory.CreateDirectory(stylesDir);
            Directory.CreateDirectory(imagesDir);

            // 1. mimetype (обязательный файл стандарта EPUB)
            await File.WriteAllTextAsync(
                Path.Combine(targetBookFolder, "mimetype"),
                "application/epub+zip",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 2. META-INF/container.xml
            string containerXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """;
            await File.WriteAllTextAsync(Path.Combine(metaInfDir, "container.xml"), containerXml, Encoding.UTF8);

            // 3. Базовый CSS стиль
            string baseCss = CssHelper.EnsureDefaultEditorStylesExist(string.Empty);
            if (!string.IsNullOrWhiteSpace(additionalCssStyles))
            {
                baseCss = $"{baseCss}\n\n{additionalCssStyles.Trim()}";
            }
            await File.WriteAllTextAsync(Path.Combine(stylesDir, "style.css"), baseCss, Encoding.UTF8);

            var manifestSb = new StringBuilder();
            var spineSb = new StringBuilder();
            var ncxNavPointsSb = new StringBuilder();

            manifestSb.AppendLine("""    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>""");
            manifestSb.AppendLine("""    <item id="css" href="Styles/style.css" media-type="text/css"/>""");

            // 4. Обложка (если есть)
            string? savedCoverPath = null;
            string metaCoverTag = string.Empty;
            if (coverBytes != null && coverBytes.Length > 0)
            {
                string cleanExt = coverExtension.StartsWith('.') ? coverExtension : $".{coverExtension}";
                string coverFileName = $"cover{cleanExt}";
                savedCoverPath = Path.Combine(imagesDir, coverFileName);
                await File.WriteAllBytesAsync(savedCoverPath, coverBytes);

                string mime = GetImageMimeType(coverFileName);
                manifestSb.AppendLine($"""    <item id="cover-image" href="Images/{coverFileName}" media-type="{mime}" properties="cover-image"/>""");
                metaCoverTag = """    <meta name="cover" content="cover-image" />""";
            }

            // 5. Дополнительные картинки
            if (extraImages != null)
            {
                int imgIndex = 1;
                foreach (var kvp in extraImages)
                {
                    string imgFileName = kvp.Key;
                    await File.WriteAllBytesAsync(Path.Combine(imagesDir, imgFileName), kvp.Value);
                    string mime = GetImageMimeType(imgFileName);
                    manifestSb.AppendLine($"""    <item id="img-{imgIndex++}" href="Images/{imgFileName}" media-type="{mime}"/>""");
                }
            }

            // 6. Главы (XHTML)
            if (chapters.Count == 0)
            {
                chapters.Add(new EpubChapterDraft(title, "<p>Пустой документ</p>"));
            }

            for (int i = 0; i < chapters.Count; i++)
            {
                int num = i + 1;
                var currentChapter = chapters.ElementAt(i);
                string chapterId = $"ch{num:D3}";
                string fileName = !string.IsNullOrWhiteSpace(currentChapter.CustomFileName) ? currentChapter.CustomFileName : $"{chapterId}.xhtml";
                string safeTitle = WebUtility.HtmlEncode(currentChapter.Title);
                string xhtml =
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
    "<!DOCTYPE html>\n" +
    "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n" +
    "<head>\n" +
    $"  <title>{safeTitle}</title>\n" +
    "  <link rel=\"stylesheet\" type=\"text/css\" href=\"../Styles/style.css\"/>\n" +
    "</head>\n" +
    "<body>\n" +
    $"{currentChapter.HtmlBodyContent}\n" +
    "</body>\n" +
    "</html>";

                await File.WriteAllTextAsync(Path.Combine(textDir, fileName), xhtml, Encoding.UTF8);

                manifestSb.AppendLine($"""    <item id="{chapterId}" href="Text/{fileName}" media-type="application/xhtml+xml"/>""");
                spineSb.AppendLine($"""    <itemref idref="{chapterId}"/>""");

                ncxNavPointsSb.AppendLine($"""
                <navPoint id="navPoint-{num}" playOrder="{num}">
                  <navLabel><text>{safeTitle}</text></navLabel>
                  <content src="Text/{fileName}"/>
                </navPoint>
                """);
            }
            // Build EPUB 3 Navigation Document (OEBPS/nav.xhtml)
            string safeBookTitle = WebUtility.HtmlEncode(title);
            string safeAuthor = WebUtility.HtmlEncode(author);
            var navItemsSb = new StringBuilder();
            for (int i = 0; i < chapters.Count; i++)
            {
                int num = i + 1;
                string fileName = $"ch{num:D3}.xhtml";
                string safeTitle = WebUtility.HtmlEncode(chapters.ElementAt(i).Title);
                navItemsSb.AppendLine($"      <li><a href=\"Text/{fileName}\">{safeTitle}</a></li>");
            }

            string xhtmlNs = "http://" + "www.w3.org/1999/xhtml";
            string epubNs = "http://" + "www.idpf.org/2007/ops";
            string navXhtml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<!DOCTYPE html>\n" +
                $"<html xmlns=\"{xhtmlNs}\" xmlns:epub=\"{epubNs}\">\n" +
                "<head>\n" +
                $"  <title>{safeBookTitle}</title>\n" +
                "</head>\n" +
                "<body>\n" +
                "  <nav epub:type=\"toc\" id=\"toc\">\n" +
                "    <ol>\n" +
                $"{navItemsSb}" +
                "    </ol>\n" +
                "  </nav>\n" +
                "</body>\n" +
                "</html>";

            await File.WriteAllTextAsync(Path.Combine(oebpsDir, "nav.xhtml"), navXhtml, Encoding.UTF8);
            manifestSb.AppendLine("""    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>""");
            // 7. OEBPS/content.opf
            string opfXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:title>{safeBookTitle}</dc:title>
                <dc:creator>{safeAuthor}</dc:creator>
                <dc:language>{WebUtility.HtmlEncode(language)}</dc:language>
                <dc:identifier id="BookId">urn:uuid:{bookId}</dc:identifier>
            {metaCoverTag}
              </metadata>
              <manifest>
            {manifestSb}  </manifest>
              <spine toc="ncx">
            {spineSb}  </spine>
            </package>
            """;
            await File.WriteAllTextAsync(Path.Combine(oebpsDir, "content.opf"), opfXml, Encoding.UTF8);

            // 8. OEBPS/toc.ncx (оглавление)
            string ncxXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="urn:uuid:{bookId}"/>
              </head>
              <docTitle><text>{safeBookTitle}</text></docTitle>
              <navMap>
            {ncxNavPointsSb}  </navMap>
            </ncx>
            """;
            await File.WriteAllTextAsync(Path.Combine(oebpsDir, "toc.ncx"), ncxXml, Encoding.UTF8);

            return savedCoverPath;
        }

        public static async Task UpdateExistingBookStructureAsync(
    string opfDir,
    string bookId,
    string title,
    string author,
    string language,
    IReadOnlyList<EditableChapterItem> chapters)
        {
            string textDir = Path.Combine(opfDir, "Text");
            Directory.CreateDirectory(textDir);

            string cssRelHref = "../Styles/style.css";
            string? existingCss = Directory.GetFiles(opfDir, "*.css", SearchOption.AllDirectories).FirstOrDefault();
            if (existingCss != null)
            {
                cssRelHref = "../" + Path.GetRelativePath(opfDir, existingCss).Replace('\\', '/');
            }

            string xhtmlNs = "http://" + "www.w3.org/1999/xhtml";
            string epubNs = "http://" + "www.idpf.org/2007/ops";
            string opfNsUri = "http://" + "www.idpf.org/2007/opf";
            string dcNsUri = "http://" + "purl.org/dc/elements/1.1/";
            string ncxNsUri = "http://" + "www.daisy.org/z3986/2005/ncx/";

            // 1. Write all chapter XHTML files
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters.ElementAt(i);
                if (string.IsNullOrEmpty(ch.FullFilePath))
                {
                    string fileName = !string.IsNullOrEmpty(ch.FullFileName) ? ch.FullFileName : $"ch{i + 1:D3}.xhtml";
                    ch.Id = string.IsNullOrEmpty(ch.Id) ? $"ch{i + 1:D3}" : ch.Id;
                    ch.RelativeHref = $"Text/{fileName}";
                    ch.FullFilePath = Path.Combine(textDir, fileName);
                }

                string safeTitle = WebUtility.HtmlEncode(ch.Title);
                string xhtml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<!DOCTYPE html>\n" +
                    $"<html xmlns=\"{xhtmlNs}\" xmlns:epub=\"{epubNs}\">\n" +
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

            // 2. Write EPUB 3 Navigation Document (nav.xhtml)
            var navItemsSb = new StringBuilder();
            foreach (var ch in chapters)
            {
                string safeTitle = WebUtility.HtmlEncode(ch.Title);
                navItemsSb.AppendLine($"      <li><a href=\"{ch.RelativeHref}\">{safeTitle}</a></li>");
            }

            string navXhtml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<!DOCTYPE html>\n" +
                $"<html xmlns=\"{xhtmlNs}\" xmlns:epub=\"{epubNs}\">\n" +
                "<head>\n" +
                $"  <title>{WebUtility.HtmlEncode(title)}</title>\n" +
                "</head>\n" +
                "<body>\n" +
                "  <nav epub:type=\"toc\" id=\"toc\">\n" +
                "    <ol>\n" +
                $"{navItemsSb}" +
                "    </ol>\n" +
                "  </nav>\n" +
                "</body>\n" +
                "</html>";

            await WriteTextWithRetryAsync(Path.Combine(opfDir, "nav.xhtml"), navXhtml);

            // 3. Write EPUB 2 Navigation Document (toc.ncx)
            var ncxPointsSb = new StringBuilder();
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters.ElementAt(i);
                string safeTitle = WebUtility.HtmlEncode(ch.Title);
                ncxPointsSb.AppendLine(
                    $"    <navPoint id=\"navPoint-{i + 1}\" playOrder=\"{i + 1}\">\n" +
                    $"      <navLabel><text>{safeTitle}</text></navLabel>\n" +
                    $"      <content src=\"{ch.RelativeHref}\"/>\n" +
                    "    </navPoint>");
            }

            string ncxXml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                $"<ncx xmlns=\"{ncxNsUri}\" version=\"2005-1\">\n" +
                "  <head>\n" +
                $"    <meta name=\"dtb:uid\" content=\"urn:uuid:{bookId}\"/>\n" +
                "  </head>\n" +
                $"  <docTitle><text>{WebUtility.HtmlEncode(title)}</text></docTitle>\n" +
                "  <navMap>\n" +
                $"{ncxPointsSb}" +
                "  </navMap>\n" +
                "</ncx>";

            await WriteTextWithRetryAsync(Path.Combine(opfDir, "toc.ncx"), ncxXml);

            // 4. Rebuild content.opf with unified, valid OPF + Dublin Core namespaces
            string opfPath = Directory.GetFiles(opfDir, "*.opf", SearchOption.TopDirectoryOnly).First();
            XDocument oldDoc = XDocument.Load(opfPath);

            XNamespace opfNs = opfNsUri;
            XNamespace dcNs = dcNsUri;

            // Preserve existing non-chapter items (images, cover, CSS, fonts) and cover meta tag
            var preservedItems = oldDoc.Descendants()
                .Where(x => x.Name.LocalName == "item")
                .Where(x =>
                {
                    string mediaType = x.Attribute("media-type")?.Value ?? string.Empty;
                    string props = x.Attribute("properties")?.Value ?? string.Empty;
                    return mediaType != "application/xhtml+xml" &&
                           mediaType != "application/x-dtbncx+xml" &&
                           !props.Split(' ').Contains("nav");
                })
                .Select(x =>
                {
                    var el = new XElement(opfNs + "item",
                        new XAttribute("id", x.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N")),
                        new XAttribute("href", x.Attribute("href")?.Value ?? string.Empty),
                        new XAttribute("media-type", x.Attribute("media-type")?.Value ?? string.Empty));
                    if (x.Attribute("properties") != null)
                        el.Add(new XAttribute("properties", x.Attribute("properties")!.Value));
                    return el;
                })
                .ToList();

            string? existingCoverId = oldDoc.Descendants()
                .Where(x => x.Name.LocalName == "meta" && x.Attribute("name")?.Value == "cover")
                .Select(x => x.Attribute("content")?.Value)
                .FirstOrDefault();

            var metadataEl = new XElement(opfNs + "metadata",
                new XAttribute(XNamespace.Xmlns + "dc", dcNsUri),
                new XAttribute(XNamespace.Xmlns + "opf", opfNsUri),
                new XElement(dcNs + "title", title),
                new XElement(dcNs + "creator", author),
                new XElement(dcNs + "language", language),
                new XElement(dcNs + "identifier", new XAttribute("id", "BookId"), $"urn:uuid:{bookId}"));

            if (!string.IsNullOrEmpty(existingCoverId))
            {
                metadataEl.Add(new XElement(opfNs + "meta",
                    new XAttribute("name", "cover"),
                    new XAttribute("content", existingCoverId)));
            }

            var manifestEl = new XElement(opfNs + "manifest",
                new XElement(opfNs + "item",
                    new XAttribute("id", "ncx"),
                    new XAttribute("href", "toc.ncx"),
                    new XAttribute("media-type", "application/x-dtbncx+xml")),
                new XElement(opfNs + "item",
                    new XAttribute("id", "nav"),
                    new XAttribute("href", "nav.xhtml"),
                    new XAttribute("media-type", "application/xhtml+xml"),
                    new XAttribute("properties", "nav")));

            foreach (var item in preservedItems)
            {
                manifestEl.Add(item);
            }

            var spineEl = new XElement(opfNs + "spine", new XAttribute("toc", "ncx"));

            foreach (var ch in chapters)
            {
                manifestEl.Add(new XElement(opfNs + "item",
                    new XAttribute("id", ch.Id),
                    new XAttribute("href", ch.RelativeHref),
                    new XAttribute("media-type", "application/xhtml+xml")));

                spineEl.Add(new XElement(opfNs + "itemref",
                    new XAttribute("idref", ch.Id)));
            }

            var newOpfDoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(opfNs + "package",
                    new XAttribute("unique-identifier", "BookId"),
                    new XAttribute("version", "2.0"),
                    metadataEl,
                    manifestEl,
                    spineEl));

            newOpfDoc.Save(opfPath);

        }

        public static async Task WriteTextWithRetryAsync(string path, string content)
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

        private static void SetOrCreateElement(XElement parent, XName name, string value)
        {
            var el = parent.Element(name);
            if (el != null) el.Value = value;
            else parent.Add(new XElement(name, value));
        }

        private static string GetImageMimeType(string fileName) =>
            Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
    }
}
