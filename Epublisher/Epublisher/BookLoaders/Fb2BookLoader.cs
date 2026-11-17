using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Epublisher.BookLoaders
{
    public class Fb2BookLoader : IBookLoader
    {
        public bool CanHandle(string extension) =>
            extension.Equals(".fb2", StringComparison.OrdinalIgnoreCase);

        public async Task<RecentFile> LoadToUnpackedEpubAsync(
            Stream sourceStream, string originalFileName, string targetBookFolder, string bookId)
        {
            XDocument doc = await Task.Run(() => XDocument.Load(sourceStream));
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            XNamespace xlinkNs = "http://w3.org";

            var titleInfo = doc.Descendants(ns + "title-info").FirstOrDefault();

            string title = titleInfo?.Element(ns + "book-title")?.Value.Trim()
                ?? Path.GetFileNameWithoutExtension(originalFileName);

            var authors = titleInfo?.Elements(ns + "author")
                .Select(a => $"{a.Element(ns + "first-name")?.Value} {a.Element(ns + "last-name")?.Value}".Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            string author = authors != null && authors.Count > 0 ? string.Join(", ", authors) : "Unknown Author";
            string lang = titleInfo?.Element(ns + "lang")?.Value.Trim() ?? "ru";

            // 1. Извлекаем все встроенные картинки (<binary id="cover.jpg">)
            var binaries = doc.Descendants(ns + "binary")
                .Where(b => b.Attribute("id") != null)
                .ToDictionary(
                    b => b.Attribute("id")!.Value.TrimStart('#'),
                    b => Convert.FromBase64String(b.Value.Trim())
                );

            string? coverId = titleInfo?.Element(ns + "coverpage")?.Element(ns + "image")
                ?.Attribute(xlinkNs + "href")?.Value.TrimStart('#');

            byte[]? coverBytes = null;
            string coverExt = ".jpg";
            if (!string.IsNullOrEmpty(coverId) && binaries.TryGetValue(coverId, out var cBytes))
            {
                coverBytes = cBytes;
                coverExt = Path.GetExtension(coverId);
                if (string.IsNullOrEmpty(coverExt)) coverExt = ".jpg";
                binaries.Remove(coverId);
            }

            // 2. Разбираем секции <body> -> <section> в главы
            var chapters = new List<EpubChapterDraft>();
            var mainBody = doc.Descendants(ns + "body").FirstOrDefault();

            if (mainBody != null)
            {
                var topSections = mainBody.Elements(ns + "section").ToList();
                if (topSections.Count == 0)
                {
                    chapters.Add(new EpubChapterDraft(title, ConvertFb2ElementsToHtml(mainBody.Elements(), ns, xlinkNs)));
                }
                else
                {
                    int chNum = 1;
                    foreach (var sec in topSections)
                    {
                        string secTitle = sec.Element(ns + "title")?.Value.Trim() ?? $"Глава {chNum}";
                        // Убираем пробелы/переносы внутри заголовка FB2
                        secTitle = string.Join(" ", secTitle.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
                        string htmlBody = ConvertFb2ElementsToHtml(sec.Elements().Where(e => e.Name != ns + "title"), ns, xlinkNs);
                        chapters.Add(new EpubChapterDraft(secTitle, htmlBody));
                        chNum++;
                    }
                }
            }

            string? savedCoverPath = await EpubStructureWriter.WriteUnpackedEpubAsync(
                targetBookFolder, bookId, title, author, lang, chapters, coverBytes, coverExt, binaries);

            return new RecentFile
            {
                Id = bookId,
                InternalEpubPath = targetBookFolder,
                Title = title,
                Author = author,
                Language = lang,
                CoverThumbnailPath = savedCoverPath,
                SourceType = BookSourceType.ImportedConverted,
                OriginalFormat = ".fb2"
            };
        }

        private static string ConvertFb2ElementsToHtml(IEnumerable<XElement> elements, XNamespace ns, XNamespace xlinkNs)
        {
            var sb = new StringBuilder();
            foreach (var el in elements)
            {
                string localName = el.Name.LocalName;
                switch (localName)
                {
                    case "p":
                        sb.AppendLine($"<p>{ConvertInlineFb2(el, ns)}</p>");
                        break;
                    case "subtitle":
                        sb.AppendLine($"<h3>{WebUtility.HtmlEncode(el.Value)}</h3>");
                        break;
                    case "empty-line":
                        sb.AppendLine("<br/>");
                        break;
                    case "epigraph":
                    case "cite":
                        sb.AppendLine($"<blockquote>{ConvertFb2ElementsToHtml(el.Elements(), ns, xlinkNs)}</blockquote>");
                        break;
                    case "poem":
                    case "stanza":
                        sb.AppendLine($"<div class=\"poem\">{ConvertFb2ElementsToHtml(el.Elements(), ns, xlinkNs)}</div>");
                        break;
                    case "v":
                        sb.AppendLine($"<p style=\"text-indent:0;\">{ConvertInlineFb2(el, ns)}</p>");
                        break;
                    case "image":
                        string? href = el.Attribute(xlinkNs + "href")?.Value.TrimStart('#');
                        if (!string.IsNullOrEmpty(href))
                            sb.AppendLine($"<img src=\"../Images/{href}\" alt=\"\"/>");
                        break;
                    case "section":
                        var subTitle = el.Element(ns + "title")?.Value.Trim();
                        if (!string.IsNullOrEmpty(subTitle))
                            sb.AppendLine($"<h3>{WebUtility.HtmlEncode(subTitle)}</h3>");
                        sb.AppendLine(ConvertFb2ElementsToHtml(el.Elements().Where(e => e.Name != ns + "title"), ns, xlinkNs));
                        break;
                }
            }
            return sb.ToString();
        }

        private static string ConvertInlineFb2(XElement pElement, XNamespace ns)
        {
            var sb = new StringBuilder();
            foreach (var node in pElement.Nodes())
            {
                if (node is XText text)
                {
                    sb.Append(WebUtility.HtmlEncode(text.Value));
                }
                else if (node is XElement child)
                {
                    string inner = ConvertInlineFb2(child, ns);
                    sb.Append(child.Name.LocalName switch
                    {
                        "emphasis" => $"<em>{inner}</em>",
                        "strong" => $"<strong>{inner}</strong>",
                        "strikethrough" => $"<del>{inner}</del>",
                        "sup" => $"<sup>{inner}</sup>",
                        "sub" => $"<sub>{inner}</sub>",
                        _ => inner
                    });
                }
            }
            return sb.ToString();
        }
    }
}
