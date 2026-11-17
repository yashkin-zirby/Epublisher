using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace Epublisher.Helpers
{
    internal partial class SinglePageEpubAssembler
    {
        private sealed class ManifestItem
        {
            public string Id { get; init; } = string.Empty;
            public string Href { get; init; } = string.Empty;
            public string MediaType { get; init; } = string.Empty;
            public string Properties { get; init; } = string.Empty;
        }
        private static string NormalizeRelPath(string path) => path.Replace('\\', '/').TrimStart('/');
        public async Task<string> BuildSinglePageHtmlAsync(string bookFolder)
        {
            string opfPath = Directory.GetFiles(bookFolder, "*.opf", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException("Файл .opf не найден в распакованном EPUB.");

            string opfDir = Path.GetDirectoryName(opfPath)!;
            XDocument opfDoc = XDocument.Load(opfPath);
            XNamespace opfNs = opfDoc.Root!.GetDefaultNamespace();

            var manifestItems = opfDoc.Descendants(opfNs + "item")
                .Select(x => new ManifestItem
                {
                    Id = x.Attribute("id")?.Value ?? string.Empty,
                    Href = Uri.UnescapeDataString(x.Attribute("href")?.Value ?? string.Empty),
                    MediaType = x.Attribute("media-type")?.Value ?? string.Empty,
                    Properties = x.Attribute("properties")?.Value ?? string.Empty
                })
                .Where(x => !string.IsNullOrEmpty(x.Id))
                .ToDictionary(x => x.Id);

            var spineItems = opfDoc.Descendants(opfNs + "itemref")
                .Select(x => x.Attribute("idref")?.Value)
                .Where(id => id != null && manifestItems.ContainsKey(id))
                .Select(id => manifestItems[id!])
                .ToList();

            var hrefToSectionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < spineItems.Count; i++)
            {
                string normHref = NormalizeRelPath(spineItems.ElementAt(i).Href);
                hrefToSectionId[normHref] = $"epub-sec-{i}";
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>");

            var cssItems = manifestItems.Values.Where(x => x.MediaType == "text/css");
            foreach (var css in cssItems)
            {
                string cssFullPath = Path.Combine(opfDir, css.Href.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(cssFullPath))
                {
                    string cssContent = await File.ReadAllTextAsync(cssFullPath);
                    string cssDir = GetUrlDirectory(css.Href);
                    cssContent = RewriteCssUrls(cssContent, cssDir);

                    sb.AppendLine("<style type=\"text/css\">");
                    sb.AppendLine(cssContent);
                    sb.AppendLine("</style>");
                }
            }

            sb.AppendLine("""
    <style type="text/css">
        :root {
            --reader-bg: #ffffff;
            --reader-fg: #1a1a1a;
            --reader-link: #1d4ed8;
        }
        html, body {
            background-color: var(--reader-bg) !important;
            color: var(--reader-fg) !important;
            margin: 0 auto;
            padding: 20px;
            max-width: 860px;
            box-sizing: border-box;
            line-height: 1.6;
            transition: background-color 0.2s ease, color 0.2s ease;
        }
        html[data-custom-theme="true"] p,
        html[data-custom-theme="true"] div,
        html[data-custom-theme="true"] span,
        html[data-custom-theme="true"] h1,
        html[data-custom-theme="true"] h2,
        html[data-custom-theme="true"] h3,
        html[data-custom-theme="true"] li {
            color: var(--reader-fg) !important;
            background-color: transparent !important;
        }
        a {
            color: var(--reader-link) !important;
        }
        .epub-cover-container {
            min-height: 95vh;
            display: flex;
            align-items: center;
            justify-content: center;
            text-align: center;
            margin-bottom: 48px;
            break-after: page;
            page-break-after: always;
        }
        .epub-cover-container img {
            max-width: 100%;
            max-height: 88vh;
            width: auto;
            height: auto;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
        }
        .epub-chapter-section {
            min-height: 95vh;
            break-after: page;
            page-break-after: always;
            padding-bottom: 48px;
            margin-bottom: 48px;
            border-bottom: 1px dashed rgba(128, 128, 128, 0.35);
        }
            /* Dashed divider BEFORE any element with page-break-before / break-before */
    [style*="page-break-before"]::before,
    [style*="break-before"]::before,
    .fullpage-image::before,
    .page-container::before {
        content: "";
        display: block;
        width: 100%;
        height: 1px;
        margin: 24px 0;
        border-top: 1px dashed rgba(128, 128, 128, 0.45);
        flex-shrink: 0;
    }

    /* Dashed divider AFTER any element with page-break-after / break-after */
    [style*="page-break-after"]::after,
    [style*="break-after"]::after,
    .fullpage-image::after,
    .page-container::after,
    .epub-page-break::after {
        content: "";
        display: block;
        width: 100%;
        height: 1px;
        margin: 24px 0;
        border-bottom: 1px dashed rgba(128, 128, 128, 0.45);
        flex-shrink: 0;
    }

    /* Universal safeguard for ANY flex container that also triggers a page break */
    [style*="display: flex"][style*="page-break"],
    [style*="display:flex"][style*="page-break"],
    [style*="display: flex"][style*="break-"],
    [style*="display:flex"][style*="break-"],
    .fullpage-image,
    .page-container {
        display: flex !important;
        flex-direction: column !important;
        justify-content: space-between !important;
    }
        .epub-chapter-section > :first-child::before {
        display: none !important;
    }

    .epub-chapter-section > :last-child::after {
        display: none !important;
    }
        .epub-chapter-section:last-child {
            break-after: auto;
            page-break-after: auto;
            border-bottom: none;
        }
        .epub-chapter-section img,
        .epub-chapter-section svg {
            max-width: 100%;
            max-height: 85vh;
            width: auto;
            height: auto;
            display: block;
            margin: 12px auto;
            object-fit: contain;
        }
    </style>
    """);
            sb.AppendLine("</head><body>");

            string? coverHref = FindCoverHref(opfDoc, opfNs, manifestItems);
            if (!string.IsNullOrEmpty(coverHref))
            {
                bool firstChapterIsCover = spineItems.Count > 0 &&
                    (spineItems.First().Id.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                     spineItems.First().Href.Contains("cover", StringComparison.OrdinalIgnoreCase));

                if (!firstChapterIsCover)
                {
                    sb.AppendLine("<div class=\"epub-cover-container\">");
                    sb.AppendLine($"  <img src=\"{coverHref}\" alt=\"Cover\" />");
                    sb.AppendLine("</div>");
                }
            }

            var loadedScriptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < spineItems.Count; i++)
            {
                var item = spineItems.ElementAt(i);
                string chapterFullPath = Path.Combine(opfDir, item.Href.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(chapterFullPath)) continue;

                string chapterDir = GetUrlDirectory(item.Href);
                string sectionId = hrefToSectionId[NormalizeRelPath(item.Href)];
                string rawXhtml = await File.ReadAllTextAsync(chapterFullPath);

                string inlineHeadStyles = ExtractHeadStyles(rawXhtml, chapterDir);
                if (!string.IsNullOrEmpty(inlineHeadStyles))
                    sb.AppendLine(inlineHeadStyles);

                string localHeadScripts = ExtractHeadScripts(rawXhtml, chapterDir, loadedScriptPaths);
                if (!string.IsNullOrEmpty(localHeadScripts))
                    sb.AppendLine(localHeadScripts);

                string bodyHtml = ExtractBodyInnerHtml(rawXhtml);
                bodyHtml = RewriteHtmlLinksAndSources(bodyHtml, chapterDir, sectionId, hrefToSectionId);

                sb.AppendLine($"<section id=\"{sectionId}\" class=\"epub-chapter-section\">");
                sb.AppendLine(bodyHtml);
                sb.AppendLine("</section>");
            }

            sb.AppendLine("</body></html>");

            string outputHtmlPath = Path.Combine(opfDir, "reader_full.html");
            await File.WriteAllTextAsync(outputHtmlPath, sb.ToString(), Encoding.UTF8);

            return outputHtmlPath;
        }

        private static string? FindCoverHref(XDocument opfDoc, XNamespace opfNs, Dictionary<string, ManifestItem> manifest)
        {
            var epub3Cover = manifest.Values.FirstOrDefault(x =>
                x.Properties.Split(' ').Contains("cover-image", StringComparer.OrdinalIgnoreCase));
            if (epub3Cover != null) return epub3Cover.Href;

            var metaCoverId = opfDoc.Descendants(opfNs + "meta")
                .FirstOrDefault(m => string.Equals(m.Attribute("name")?.Value, "cover", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("content")?.Value;

            if (!string.IsNullOrEmpty(metaCoverId) && manifest.TryGetValue(metaCoverId, out var coverItem))
            {
                if (coverItem.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return coverItem.Href;
            }

            var fallbackCover = manifest.Values.FirstOrDefault(x =>
                x.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                (x.Id.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileNameWithoutExtension(x.Href).Equals("cover", StringComparison.OrdinalIgnoreCase)));

            return fallbackCover?.Href;
        }

        private static void RenderNavList(XElement listElement, XNamespace ns, string baseDir,
            Dictionary<string, string> hrefToSectionId, StringBuilder sb)
        {
            sb.AppendLine("");
            foreach (var li in listElement.Elements(ns + "li"))
            {
                var a = li.Element(ns + "a");
                if (a != null)
                {
                    string rawHref = a.Attribute("href")?.Value ?? string.Empty;
                    string title = WebUtility.HtmlEncode(a.Value.Trim());
                    string anchor = ResolveToSinglePageAnchor(rawHref, baseDir, hrefToSectionId);
                    sb.AppendLine($"  <a href=\"{anchor}\">{title}");
                }
                else
                {
                    sb.AppendLine("  ");
                }
                var subList = li.Element(ns + "ol") ?? li.Element(ns + "ul");
                if (subList != null)
                {
                    RenderNavList(subList, ns, baseDir, hrefToSectionId, sb);
                }
                sb.AppendLine("  ");
            }
            sb.AppendLine("");
        }
        private static void RenderNcxPoints(IEnumerable<XElement> navPoints, XNamespace ns, string baseDir,
            Dictionary<string, string> hrefToSectionId, StringBuilder sb)
        {
            var points = navPoints.ToList();
            if (points.Count == 0) return;

            sb.AppendLine("<ul>");
            foreach (var np in points)
            {
                string label = np.Element(ns + "navLabel")?.Element(ns + "text")?.Value.Trim() ?? "Глава";
                string rawSrc = np.Element(ns + "content")?.Attribute("src")?.Value ?? string.Empty;
                string anchor = ResolveToSinglePageAnchor(rawSrc, baseDir, hrefToSectionId);

                sb.AppendLine($"  <li><a href=\"{anchor}\">{WebUtility.HtmlEncode(label)}</a>");
                RenderNcxPoints(np.Elements(ns + "navPoint"), ns, baseDir, hrefToSectionId, sb);
                sb.AppendLine("  </li>");
            }
            sb.AppendLine("</ul>");
        }
        private static string RewriteHtmlLinksAndSources(string html, string chapterDir, string currentSectionId, Dictionary<string, string> hrefToSectionId)
        {
            html = Regex.Replace(html, @"\bid=""([^""]+)""", m =>
                $@"id=""{currentSectionId}-{m.Groups[1].Value}""", RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"(src|xlink:href)=""([^""]+)""", m =>
            {
                string attr = m.Groups[1].Value;
                string url = m.Groups[2].Value;

                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }

                string resolved = ResolveRelativeUrl(chapterDir, Uri.UnescapeDataString(url));
                return $@"{attr}=""{resolved}""";
            }, RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<a\b([^>]*?)\bhref=""([^""]+)""", m =>
            {
                string beforeHref = m.Groups[1].Value;
                string href = m.Groups[2].Value;

                if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }

                if (href.StartsWith('#'))
                {
                    string localFrag = href.TrimStart('#');
                    return $@"<a{beforeHref}href=""#{currentSectionId}-{localFrag}""";
                }

                string anchor = ResolveToSinglePageAnchor(href, chapterDir, hrefToSectionId);
                return $@"<a{beforeHref}href=""{anchor}""";
            }, RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<script\b([^>]*)>(.*?)</script>", m =>
            {
                string attrs = m.Groups[1].Value;
                string inlineCode = m.Groups[2].Value;

                var srcMatch = Regex.Match(attrs, @"\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (srcMatch.Success)
                {
                    string rawSrc = srcMatch.Groups[1].Value.Trim();

                    if (rawSrc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        rawSrc.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        rawSrc.StartsWith("//", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Empty;
                    }

                    string resolved = ResolveRelativeUrl(chapterDir, Uri.UnescapeDataString(rawSrc));
                    string updatedAttrs = Regex.Replace(attrs, @"\bsrc\s*=\s*[""'][^""']+[""']", $@"src=""{resolved}""", RegexOptions.IgnoreCase);
                    return $"<script{updatedAttrs}>{inlineCode}</script>";
                }
                return m.Value;
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return html;
        }
        private static string ExtractBodyInnerHtml(string xhtml)
        {
            var match = Regex.Match(xhtml, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
            string withoutHead = Regex.Replace(xhtml, @"<head[^>]*>.*?</head>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            int bodyStart = withoutHead.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStart >= 0)
            {
                int closeBracket = withoutHead.IndexOf('>', bodyStart);
                if (closeBracket >= 0)
                    return withoutHead.Substring(closeBracket + 1);
            }

            return withoutHead;
        }
        private static string ResolveToSinglePageAnchor(string rawHref, string currentDir, Dictionary<string, string> hrefToSectionId)
        {
            if (string.IsNullOrWhiteSpace(rawHref)) return "#";

            string[] parts = rawHref.Split('#', 2);
            string filePart = Uri.UnescapeDataString(parts.First()).Trim();
            string fragment = parts.Length > 1 ? parts.ElementAt(1).Trim() : string.Empty;

            if (string.IsNullOrEmpty(filePart))
            {
                return string.IsNullOrEmpty(fragment) ? "#" : $"#{fragment}";
            }

            string resolvedPath = ResolveRelativeUrl(currentDir, filePart);
            if (hrefToSectionId.TryGetValue(resolvedPath, out string? sectionId))
            {
                return string.IsNullOrEmpty(fragment) ? $"#{sectionId}" : $"#{sectionId}-{fragment}";
            }
            string targetFileName = Path.GetFileName(filePart);
            if (!string.IsNullOrEmpty(targetFileName))
            {
                var fallbackMatch = hrefToSectionId.FirstOrDefault(kvp =>
                    Path.GetFileName(kvp.Key).Equals(targetFileName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(fallbackMatch.Value))
                {
                    return string.IsNullOrEmpty(fragment)
                        ? $"#{fallbackMatch.Value}"
                        : $"#{fallbackMatch.Value}-{fragment}";
                }
            }

            return "#";
        }

        private static string ExtractHeadScripts(string xhtml, string chapterDir, HashSet<string> loadedScriptPaths)
        {
            var headMatch = Regex.Match(xhtml, @"<head[^>]*>(.*?)</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!headMatch.Success) return string.Empty;

            var scriptMatches = Regex.Matches(headMatch.Groups[1].Value, @"<script\b([^>]*)>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (scriptMatches.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (Match m in scriptMatches)
            {
                string attrs = m.Groups[1].Value;
                string inlineCode = m.Groups[2].Value;

                var srcMatch = Regex.Match(attrs, @"\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (srcMatch.Success)
                {
                    string rawSrc = srcMatch.Groups[1].Value.Trim();

                    // Block any external web scripts
                    if (rawSrc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        rawSrc.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        rawSrc.StartsWith("//", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string resolvedPath = ResolveRelativeUrl(chapterDir, Uri.UnescapeDataString(rawSrc));
                    if (loadedScriptPaths.Add(resolvedPath))
                    {
                        sb.AppendLine($"<script src=\"{resolvedPath}\"></script>");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(inlineCode))
                {
                    sb.AppendLine($"<script>{inlineCode}</script>");
                }
            }

            return sb.ToString();
        }

        private static string RewriteCssUrls(string css, string cssDir)
        {
            return Regex.Replace(css, @"url\(\s*['""]?([^)'""]+)['""]?\s*\)", m =>
            {
                string rawUrl = m.Groups[1].Value.Trim();
                if (rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                    rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }

                string resolved = ResolveRelativeUrl(cssDir, Uri.UnescapeDataString(rawUrl));
                return $"url('{resolved}')";
            }, RegexOptions.IgnoreCase);
        }


        private static string ExtractHeadStyles(string xhtml, string chapterDir)
        {
            var headMatch = Regex.Match(xhtml, @"<head[^>]*>(.*?)</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!headMatch.Success) return string.Empty;

            var styles = Regex.Matches(headMatch.Groups[1].Value, @"<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (styles.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (Match s in styles)
            {
                string css = RewriteCssUrls(s.Groups[1].Value, chapterDir);
                sb.AppendLine("<style type=\"text/css\">");
                sb.AppendLine(css);
                sb.AppendLine("</style>");
            }
            return sb.ToString();
        }

        private static string GetUrlDirectory(string href)
        {
            int lastSlash = href.LastIndexOf('/');
            return lastSlash >= 0 ? href.Substring(0, lastSlash) : string.Empty;
        }

        private static string ResolveRelativeUrl(string baseDir, string relativeUrl)
        {
            if (string.IsNullOrEmpty(baseDir))
                return NormalizeRelPath(relativeUrl);

            var baseUri = new Uri($"http://epub.local{baseDir.Trim('/')}/");
            var resolvedUri = new Uri(baseUri, relativeUrl);
            return NormalizeRelPath(Uri.UnescapeDataString(resolvedUri.AbsolutePath.TrimStart('/')));
        }
    }
}
