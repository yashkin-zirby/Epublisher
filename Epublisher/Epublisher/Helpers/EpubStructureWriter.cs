using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Helpers
{
    public record EpubChapterDraft(string Title, string HtmlBodyContent);

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
            Dictionary<string, byte[]>? extraImages = null)
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
            string baseCss = """
            body { font-family: serif; line-height: 1.6; margin: 1em; }
            h1, h2, h3 { text-align: center; margin-top: 1.5em; margin-bottom: 0.8em; }
            p { text-indent: 1.5em; margin: 0.4em 0; text-align: justify; }
            img { max-width: 100%; height: auto; display: block; margin: 1em auto; }
            .fullpage-image {
                width: 100%;
                min-height: 92vh;
                display: flex;
                align-items: center;
                justify-content: center;
                break-before: page;
                page-break-before: always;
                break-after: page;
                page-break-after: always;
                margin: 0;
                padding: 0;
            }
            .fullpage-image img {
                width: 100%;
                max-height: 92vh;
                object-fit: contain;
                margin: 0;
            }
            """;
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
                string chapterId = $"ch{num:D3}";
                string fileName = $"{chapterId}.xhtml";
                var currentChapter = chapters.ElementAt(i);
                string safeTitle = WebUtility.HtmlEncode(currentChapter.Title);
                bool hasOwnHeadingOrFullImage =
                    currentChapter.HtmlBodyContent.Contains("<h1", StringComparison.OrdinalIgnoreCase) ||
                    currentChapter.HtmlBodyContent.Contains("<h2", StringComparison.OrdinalIgnoreCase) ||
                    currentChapter.HtmlBodyContent.Contains("fullpage-image", StringComparison.OrdinalIgnoreCase);
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

            // 7. OEBPS/content.opf
            string safeBookTitle = WebUtility.HtmlEncode(title);
            string safeAuthor = WebUtility.HtmlEncode(author);
            string opfXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://idpf.org" unique-identifier="BookId" version="2.0">
              <metadata xmlns:dc="http://purl.org" xmlns:opf="http://idpf.org">
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
            <ncx xmlns="http://daisy.org" version="2005-1">
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
