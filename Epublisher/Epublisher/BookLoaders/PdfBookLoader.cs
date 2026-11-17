using Epublisher.DataEntities;
using Epublisher.Exceptions;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Epublisher.BookLoaders
{
    public class PdfBookLoader : IBookLoader
    {
        private sealed class PageLayoutElement
        {
            public double FontSize { get; init; }
            public double TopY { get; init; }
            public double BottomY { get; init; }
            public string Html { get; init; } = string.Empty;
            public bool IsHeading { get; init; }
            public bool IsFullPageImage { get; init; }
            public string? ChapterTitleCandidate { get; init; }
        }
        private sealed class ExtractedPdfImage
        {
            public byte[] Bytes { get; init; } = Array.Empty<byte>();
            public string Extension { get; init; } = ".jpg";
            public double TopY { get; init; }
            public double BottomY { get; init; }
            public double Width { get; init; }
            public double Height { get; init; }
        }
        public bool CanHandle(string extension) =>
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public async Task<RecentFile> LoadToUnpackedEpubAsync(
    Stream sourceStream, string originalFileName, string targetBookFolder, string bookId)
        {
            return await Task.Run(async () =>
            {
                using var document = PdfDocument.Open(sourceStream);

                string title = !string.IsNullOrWhiteSpace(document.Information.Title)
                    ? document.Information.Title
                    : Path.GetFileNameWithoutExtension(originalFileName);

                string author = !string.IsNullOrWhiteSpace(document.Information.Author)
                    ? document.Information.Author
                    : "Unknown Author";

                string detectedLang = DetectDocumentLanguage(document);
                double bodyFontSize = DetectDominantFontSize(document);
                HashSet<string> tocTitles = ExtractTocTitlesFromDocument(document);

                byte[]? coverBytes = null;
                string coverExt = ".png";
                var extraImages = new Dictionary<string, byte[]>();

                var chapters = new List<EpubChapterDraft>();
                var currentChapterSb = new StringBuilder();
                string currentChapterTitle = title;
                bool hasContentInCurrentChapter = false;
                bool currentChapterHasHeading = false;

                var tocLineRegex = new Regex(@"^(.+?)\s*[\.·…_\-]{3,}\s*(\d+)\s*$");
                int lastHeadingPageNum = -1;

                for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
                {
                    Page page = document.GetPage(pageNum);
                    double pageWidth = page.Width;
                    double pageHeight = page.Height;

                    double topMarginY = pageHeight * 0.945;
                    double bottomMarginY = pageHeight * 0.055;

                    var pageElements = new List<PageLayoutElement>();

                    int imgCounter = 1;
                    var extractedImages = ExtractAllImagesFromPage(page, document);

                    foreach (var img in extractedImages)
                    {
                        if (img.Width < 15 || img.Height < 15)
                            continue;

                        bool isFullPageSize = img.Width >= pageWidth * 0.55 || img.Height >= pageHeight * 0.55;

                        if (pageNum <= 2 && coverBytes == null && isFullPageSize)
                        {
                            coverBytes = img.Bytes;
                            coverExt = img.Extension;
                            continue;
                        }

                        string imgFileName = $"img_p{pageNum}_{imgCounter++}{img.Extension}";
                        extraImages[imgFileName] = img.Bytes;

                        string imgHtml = isFullPageSize
                            ? $"<div class=\"fullpage-image\"><img src=\"../Images/{imgFileName}\" alt=\"\"/></div>"
                            : $"<div class=\"pdf-image\"><img src=\"../Images/{imgFileName}\" alt=\"\"/></div>";

                        pageElements.Add(new PageLayoutElement
                        {
                            TopY = img.TopY,
                            BottomY = img.BottomY,
                            Html = imgHtml,
                            IsFullPageImage = isFullPageSize
                        });
                    }

                    var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
                    var blocks = RecursiveXYCut.Instance.GetBlocks(words);
                    var orderedBlocks = UnsupervisedReadingOrderDetector.Instance.Get(blocks);

                    bool hasFullPageImage = pageElements.Any(e => e.IsFullPageImage);
                    int totalWordsOnPage = words.Count();

                    if (hasFullPageImage && totalWordsOnPage < 15)
                    {
                        orderedBlocks = Array.Empty<TextBlock>();
                    }

                    foreach (var block in orderedBlocks)
                    {
                        if (block.BoundingBox.Bottom > topMarginY || block.BoundingBox.Top < bottomMarginY)
                            continue;

                        var blockLetters = block.TextLines.SelectMany(l => l.Words).SelectMany(w => w.Letters).ToList();
                        if (blockLetters.Count == 0) continue;

                        double avgFontSize = blockLetters.Average(l => l.PointSize);
                        bool isBold = blockLetters.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true)
                                      > blockLetters.Count * 0.6;

                        double blockCenter = (block.BoundingBox.Left + block.BoundingBox.Right) / 2.0;
                        double pageCenter = pageWidth / 2.0;
                        bool isCentered = Math.Abs(blockCenter - pageCenter) < pageWidth * 0.08 &&
                                          block.BoundingBox.Width < pageWidth * 0.85;

                        List<string> paragraphs = SplitBlockIntoParagraphs(block.TextLines, avgFontSize);

                        for (int pIdx = 0; pIdx < paragraphs.Count; pIdx++)
                        {
                            string rawPara = paragraphs.ElementAt(pIdx);
                            if (string.IsNullOrWhiteSpace(rawPara)) continue;

                            if (pIdx + 1 < paragraphs.Count && !tocLineRegex.IsMatch(rawPara))
                            {
                                var nextTocMatch = tocLineRegex.Match(paragraphs.ElementAt(pIdx + 1));
                                if (nextTocMatch.Success && rawPara.Length < 90)
                                {
                                    rawPara = $"{rawPara} {paragraphs.ElementAt(pIdx + 1)}";
                                    pIdx++; 
                                }
                            }

                            var tocMatch = tocLineRegex.Match(rawPara);
                            if (tocMatch.Success)
                            {
                                string cleanTocTitle = tocMatch.Groups[1].Value.Trim();
                                string encodedToc = WebUtility.HtmlEncode(cleanTocTitle);
                                pageElements.Add(new PageLayoutElement
                                {
                                    FontSize = avgFontSize,
                                    TopY = block.BoundingBox.Top,
                                    BottomY = block.BoundingBox.Bottom,
                                    Html = $"<p class=\"pdf-toc-entry\" data-toc-target=\"{encodedToc}\">{encodedToc}</p>"
                                });
                                continue;
                            }

                            bool matchesToc = tocTitles.Any(t =>
                                t.Equals(rawPara, StringComparison.OrdinalIgnoreCase) ||
                                (rawPara.Length >= 5 && t.StartsWith(rawPara, StringComparison.OrdinalIgnoreCase)));

                            bool isLargeHeading = avgFontSize >= bodyFontSize * 1.25;
                            bool isCenteredBoldHeading = isCentered && isBold && rawPara.Length <= 100 && !rawPara.EndsWith('.');

                            bool isHeading = matchesToc || isLargeHeading || isCenteredBoldHeading;

                            string encodedText = WebUtility.HtmlEncode(rawPara);
                            string alignStyle = isCentered ? " style=\"text-align: center; text-indent: 0;\"" : string.Empty;

                            string elementHtml;
                            if (avgFontSize >= bodyFontSize * 1.45)
                                elementHtml = $"<h1{alignStyle}>{encodedText}</h1>";
                            else if (isHeading)
                                elementHtml = $"<h2{alignStyle}>{encodedText}</h2>";
                            else
                                elementHtml = $"<p{alignStyle}>{encodedText}</p>";

                            pageElements.Add(new PageLayoutElement
                            {
                                FontSize = avgFontSize,
                                TopY = block.BoundingBox.Top,
                                BottomY = block.BoundingBox.Bottom,
                                Html = elementHtml,
                                IsHeading = isHeading,
                                ChapterTitleCandidate = isHeading ? rawPara : null
                            });
                        }
                    }

                    var sortedElements = pageElements.OrderByDescending(e => e.TopY).ToList();
                    bool chapterStartedOnThisPage = false;

                    for (int i = 0; i < sortedElements.Count; i++)
                    {
                        var el = sortedElements.ElementAt(i);
                        if (el.IsFullPageImage)
                        {
                            if (hasContentInCurrentChapter && currentChapterSb.Length > 0)
                            {
                                chapters.Add(new EpubChapterDraft(currentChapterTitle, currentChapterSb.ToString()));
                                currentChapterSb.Clear();
                                hasContentInCurrentChapter = false;
                            }

                            chapters.Add(new EpubChapterDraft($"Иллюстрация (стр. {pageNum})", el.Html));
                            chapterStartedOnThisPage = true;
                            continue;
                        }

                        if (i + 1 < sortedElements.Count &&
                            !el.Html.Contains("pdf-toc-entry") &&
                            !el.Html.Contains("image") &&
                            sortedElements.ElementAt(i + 1).Html.Contains("pdf-toc-entry"))
                        {
                            string firstPart = Regex.Replace(el.Html, "<[^>]+>", string.Empty).Trim();
                            var nextEl = sortedElements.ElementAt(i + 1);

                            var nextMatch = Regex.Match(nextEl.Html, @"data-toc-target=""([^""]+)"">(.*?)</p>");
                            if (nextMatch.Success && firstPart.Length < 100)
                            {
                                string combinedTitle = $"{firstPart} {WebUtility.HtmlDecode(nextMatch.Groups[1].Value)}";
                                string encodedCombined = WebUtility.HtmlEncode(combinedTitle);
                                el = new PageLayoutElement
                                {
                                    TopY = el.TopY,
                                    BottomY = nextEl.BottomY,
                                    FontSize = el.FontSize,
                                    Html = $"<p class=\"pdf-toc-entry\" data-toc-target=\"{encodedCombined}\">{encodedCombined}</p>"
                                };
                                i++;
                            }
                        }

                        while (i + 1 < sortedElements.Count &&
                               el.IsHeading &&
                               sortedElements.ElementAt(i + 1).IsHeading)
                        {
                            var nextHeading = sortedElements.ElementAt(i + 1);
                            string currentClean = Regex.Replace(el.Html, "<[^>]+>", string.Empty).Trim();
                            string nextClean = Regex.Replace(nextHeading.Html, "<[^>]+>", string.Empty).Trim();

                            bool sameFontSize = Math.Abs(el.FontSize - nextHeading.FontSize) < 0.5;
                            bool closeVertically = (el.BottomY - nextHeading.TopY) < el.FontSize * 1.8;
                            bool startsWithUpper = nextClean.Length > 0 && char.IsUpper(nextClean.First());

                            if (currentClean.EndsWith('.') || !sameFontSize || !closeVertically || startsWithUpper)
                                break;

                            string mergedHeading = $"{currentClean} {nextClean}";
                            string encodedMerged = WebUtility.HtmlEncode(mergedHeading);

                            string tag = el.FontSize >= bodyFontSize * 1.45 ? "h1" : "h2";
                            el = new PageLayoutElement
                            {
                                TopY = el.TopY,
                                BottomY = nextHeading.BottomY,
                                FontSize = el.FontSize,
                                Html = $"<{tag} style=\"text-align: center; text-indent: 0;\">{encodedMerged}</{tag}>",
                                IsHeading = true,
                                ChapterTitleCandidate = mergedHeading
                            };
                            i++;
                        }
                        bool isDifferentPageFromLastHeading = pageNum != lastHeadingPageNum;
                        if (el.IsHeading)
                        {
                            if (!chapterStartedOnThisPage && hasContentInCurrentChapter && currentChapterHasHeading && isDifferentPageFromLastHeading)
                            {
                                chapters.Add(new EpubChapterDraft(currentChapterTitle, currentChapterSb.ToString()));
                                currentChapterSb.Clear();
                                currentChapterTitle = el.ChapterTitleCandidate ?? $"Section {chapters.Count + 1}";
                                hasContentInCurrentChapter = false;
                                currentChapterHasHeading = false;
                            }

                            lastHeadingPageNum = pageNum;
                            chapterStartedOnThisPage = true;
                            currentChapterHasHeading = true;

                            if (!hasContentInCurrentChapter)
                            {
                                currentChapterTitle = el.ChapterTitleCandidate ?? currentChapterTitle;
                            }
                        }
                        currentChapterSb.AppendLine(el.Html);
                        hasContentInCurrentChapter = true;
                    }
                }

                if (hasContentInCurrentChapter && currentChapterSb.Length > 0)
                {
                    chapters.Add(new EpubChapterDraft(currentChapterTitle, currentChapterSb.ToString()));
                }
                LinkTableOfContentsChapters(chapters);
                string? savedCoverPath = await EpubStructureWriter.WriteUnpackedEpubAsync(
                targetBookFolder, bookId, title, author, detectedLang, chapters, coverBytes, coverExt, extraImages);
                return new RecentFile
                {
                    Id = bookId,
                    InternalEpubPath = targetBookFolder,
                    Title = title,
                    Author = author,
                    Language = detectedLang,
                    CoverThumbnailPath = savedCoverPath,
                    SourceType = BookSourceType.ImportedConverted,
                    OriginalFormat = ".pdf"
                };
            });
        }
        private static List<string> SplitBlockIntoParagraphs(IReadOnlyList<TextLine> lines, double fontSize)
        {
            var result = new List<string>();
            if (lines.Count == 0) return result;

            var orderedLines = lines.OrderByDescending(l => l.BoundingBox.Top).ToList();
            double baseLeft = orderedLines.Min(l => l.BoundingBox.Left);

            var currentPara = new StringBuilder();
            double? prevBottom = null;

            foreach (var line in orderedLines)
            {
                string lineText = string.Join(" ", line.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
                if (string.IsNullOrEmpty(lineText)) continue;

                if (currentPara.Length == 0)
                {
                    currentPara.Append(lineText);
                    prevBottom = line.BoundingBox.Bottom;
                    continue;
                }

                bool endsWithHyphen = currentPara[^1] == '-' || currentPara[^1] == '‐' || currentPara[^1] == '‑';
                bool startsWithLower = char.IsLower(lineText.First());

                if (endsWithHyphen && startsWithLower)
                {
                    currentPara.Length--; 
                    currentPara.Append(lineText); 
                    prevBottom = line.BoundingBox.Bottom;
                    continue;
                }

                bool isNewParagraph = false;
                if (prevBottom.HasValue)
                {
                    double verticalGap = prevBottom.Value - line.BoundingBox.Top;
                    bool hasIndent = (line.BoundingBox.Left - baseLeft) > fontSize * 1.1;

                    if (verticalGap > fontSize * 0.85 || hasIndent)
                    {
                        isNewParagraph = true;
                    }
                }

                if (isNewParagraph)
                {
                    result.Add(currentPara.ToString().Trim());
                    currentPara.Clear();
                    currentPara.Append(lineText);
                }
                else
                {
                    currentPara.Append(' ');
                    currentPara.Append(lineText);
                }

                prevBottom = line.BoundingBox.Bottom;
            }

            if (currentPara.Length > 0)
            {
                result.Add(currentPara.ToString().Trim());
            }

            return result;
        }
        private static string DetectDocumentLanguage(PdfDocument doc)
        {
            if (doc.Structure?.Catalog?.CatalogDictionary != null &&
                doc.Structure.Catalog.CatalogDictionary.TryGet(NameToken.Create("Lang"), out var langToken))
            {
                string rawLang = langToken switch
                {
                    StringToken str => str.Data,
                    HexToken hex => hex.Data,
                    NameToken name => name.Data,
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(rawLang))
                {
                    string primaryTag = rawLang.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

                    if (primaryTag.Length is 2 or 3)
                        return primaryTag;
                }
            }

            int cyrillicCount = 0;
            int latinCount = 0;
            int cjkCount = 0;

            int pagesToScan = Math.Min(doc.NumberOfPages, 5);
            for (int i = 1; i <= pagesToScan; i++)
            {
                foreach (var letter in doc.GetPage(i).Letters)
                {
                    if (string.IsNullOrEmpty(letter.Value)) continue;
                    char ch = letter.Value.First();

                    if (ch is >= '\u0400' and <= '\u04FF')
                        cyrillicCount++;
                    else if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))
                        latinCount++;
                    else if (ch is (>= '\u3040' and <= '\u30FF') or (>= '\u4E00' and <= '\u9FFF'))
                        cjkCount++;
                }

                if (cyrillicCount + latinCount + cjkCount > 500)
                    break;
            }

            if (cyrillicCount > latinCount && cyrillicCount > cjkCount) return "ru";
            if (cjkCount > latinCount && cjkCount > cyrillicCount) return "ja";
            if (latinCount > 0) return "en";

            return "und";
        }
        private static double DetectDominantFontSize(PdfDocument doc)
        {
            var sizes = new Dictionary<double, int>();
            int samplePages = Math.Min(doc.NumberOfPages, 15);

            for (int i = 1; i <= samplePages; i++)
            {
                var page = doc.GetPage(i);
                foreach (var letter in page.Letters)
                {
                    double rounded = Math.Round(letter.PointSize, 1);
                    if (rounded < 6 || rounded > 40) continue;
                    sizes[rounded] = sizes.GetValueOrDefault(rounded) + 1;
                }
            }

            return sizes.Count > 0
                ? sizes.OrderByDescending(kv => kv.Value).First().Key
                : 11.0;
        }
        private static HashSet<string> ExtractTocTitlesFromDocument(PdfDocument doc)
        {
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tocLineRegex = new Regex(@"^(.+?)\s*[\.·…_\-]{3,}\s*(\d+)\s*$");

            int pagesToCheck = Math.Min(doc.NumberOfPages, 20);
            for (int i = 1; i <= pagesToCheck; i++)
            {
                var page = doc.GetPage(i);
                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
                var blocks = RecursiveXYCut.Instance.GetBlocks(words);

                var orderedLines = blocks
                    .SelectMany(b => b.TextLines)
                    .OrderByDescending(l => l.BoundingBox.Top)
                    .Select(l => string.Join(" ", l.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                for (int lineIdx = 0; lineIdx < orderedLines.Count; lineIdx++)
                {
                    string currentLine = orderedLines.ElementAt(lineIdx);
                    var match = tocLineRegex.Match(currentLine);

                    if (match.Success)
                    {
                        string title = match.Groups[1].Value.Trim();
                        if (lineIdx > 0)
                        {
                            string prevLine = orderedLines.ElementAt(lineIdx - 1);
                            bool prevIsNotToc = !tocLineRegex.IsMatch(prevLine);
                            bool prevIsShortEnough = prevLine.Length is >= 3 and < 100;
                            bool prevIsNotHeader = !prevLine.Equals("Оглавление", StringComparison.OrdinalIgnoreCase) &&
                                                   !prevLine.Equals("Contents", StringComparison.OrdinalIgnoreCase);

                            if (prevIsNotToc && prevIsShortEnough && prevIsNotHeader)
                            {
                                title = $"{prevLine} {title}";
                            }
                        }

                        if (title.Length >= 2)
                        {
                            titles.Add(title);
                        }
                    }
                }
            }
            return titles;
        }

        private static void LinkTableOfContentsChapters(List<EpubChapterDraft> chapters)
        {
            static string NormalizeForMatch(string s) =>
                Regex.Replace(s, @"[\p{P}\s]+", string.Empty).Trim();

            for (int tocIdx = 0; tocIdx < chapters.Count; tocIdx++)
            {
                if (!chapters.ElementAt(tocIdx).HtmlBodyContent.Contains("pdf-toc-entry"))
                    continue;

                string updatedHtml = Regex.Replace(
                    chapters.ElementAt(tocIdx).HtmlBodyContent,
                    @"<p class=""pdf-toc-entry"" data-toc-target=""([^""]+)"">(.*?)</p>",
                    m =>
                    {
                        string rawTarget = WebUtility.HtmlDecode(m.Groups[1].Value);
                        string displayHtml = m.Groups[2].Value;
                        string normTarget = NormalizeForMatch(rawTarget);
                        int foundChapterIndex = chapters.FindIndex(c =>
                        {
                            string normChapter = NormalizeForMatch(c.Title);
                            return normChapter.Equals(normTarget, StringComparison.OrdinalIgnoreCase) ||
                                   (normTarget.Length >= 5 && normChapter.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase)) ||
                                   (normChapter.Length >= 5 && normTarget.StartsWith(normChapter, StringComparison.OrdinalIgnoreCase));
                        });
                        if (foundChapterIndex >= 0)
                        {
                            string chapterFile = $"ch{foundChapterIndex + 1:D3}.xhtml";
                            return $"<p style=\"text-indent: 0; margin: 6px 0;\"><a href=\"{chapterFile}\">{displayHtml}</a></p>";
                        }
                        return $"<p style=\"text-indent: 0; margin: 6px 0;\">{displayHtml}</p>";
                    });

                chapters[tocIdx] = chapters.ElementAt(tocIdx) with { HtmlBodyContent = updatedHtml };
            }
        }


        private static List<ExtractedPdfImage> ExtractAllImagesFromPage(Page page, PdfDocument doc)
        {
            var results = new List<ExtractedPdfImage>();

            foreach (IPdfImage pdfImg in page.GetImages())
            {
                if (TryExtractAnyImage(pdfImg, out byte[]? data, out string ext) && data != null)
                {
                    results.Add(new ExtractedPdfImage
                    {
                        Bytes = data,
                        Extension = ext,
                        TopY = pdfImg.BoundingBox.Top,
                        BottomY = pdfImg.BoundingBox.Bottom,
                        Width = Math.Abs(pdfImg.BoundingBox.Width) > 1 ? Math.Abs(pdfImg.BoundingBox.Width) : pdfImg.WidthInSamples,
                        Height = Math.Abs(pdfImg.BoundingBox.Height) > 1 ? Math.Abs(pdfImg.BoundingBox.Height) : pdfImg.HeightInSamples
                    });
                }
            }

            if (results.Count == 0 && page.Dictionary != null &&
                page.Dictionary.TryGet(NameToken.Resources, doc.Structure.TokenScanner, out DictionaryToken? resDict) &&
                resDict != null)
            {
                var visitedRefs = new HashSet<IndirectReference>();
                ExtractImagesFromResourceDict(resDict, doc, page.Height, results, visitedRefs);
            }

            return results;
        }

        private static void ExtractImagesFromResourceDict(
            DictionaryToken resDict,
            PdfDocument doc,
            double pageHeight,
            List<ExtractedPdfImage> results,
            HashSet<IndirectReference> visitedRefs)
        {
            if (!resDict.TryGet(NameToken.Xobject, doc.Structure.TokenScanner, out DictionaryToken? xObjDict) || xObjDict == null)
                return;

            foreach (var kvp in xObjDict.Data)
            {
                IToken token = kvp.Value;
                if (token is IndirectReferenceToken refToken)
                {
                    if (!visitedRefs.Add(refToken.Data)) continue;
                    token = DirectObjectFinder.Get<IToken>(refToken, doc.Structure.TokenScanner) ?? token;
                }

                if (token is not StreamToken streamToken) continue;

                var dict = streamToken.StreamDictionary;
                if (!dict.TryGet(NameToken.Subtype, out NameToken subtype)) continue;

                if (subtype == NameToken.Form)
                {
                    if (dict.TryGet(NameToken.Resources, doc.Structure.TokenScanner, out DictionaryToken? innerRes) && innerRes != null)
                    {
                        ExtractImagesFromResourceDict(innerRes, doc, pageHeight, results, visitedRefs);
                    }
                    continue;
                }

                if (subtype == NameToken.Image)
                {
                    if (TryDecodeStreamTokenImage(streamToken, doc, out byte[]? imgBytes, out string ext) && imgBytes != null)
                    {
                        int w = dict.TryGet(NameToken.Width, out NumericToken wTok) ? wTok.Int : 500;
                        int h = dict.TryGet(NameToken.Height, out NumericToken hTok) ? hTok.Int : 700;

                        results.Add(new ExtractedPdfImage
                        {
                            Bytes = imgBytes,
                            Extension = ext,
                            TopY = pageHeight - results.Count * 10,
                            BottomY = 0,
                            Width = w,
                            Height = h
                        });
                    }
                }
            }
        }
        private static bool TryDecodeStreamTokenImage(
    StreamToken streamToken, PdfDocument doc, out byte[]? bytes, out string extension)
        {
            bytes = null;
            extension = ".jpg";

            try
            {
                var dict = streamToken.StreamDictionary;
                string filterStr = dict.TryGet(NameToken.Filter, out var fTok) ? (fTok.ToString()??string.Empty) : string.Empty;

                if (filterStr.Contains("DCTDecode", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = streamToken.Data.ToArray();
                    extension = ".jpg";
                    return bytes.Length > 0;
                }

                if (filterStr.Contains("JPXDecode", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = streamToken.Data.ToArray();
                    extension = ".jp2";
                    return bytes.Length > 0;
                }

                int width = dict.TryGet(NameToken.Width, out NumericToken wTok) ? wTok.Int : 0;
                int height = dict.TryGet(NameToken.Height, out NumericToken hTok) ? hTok.Int : 0;
                if (width <= 0 || height <= 0) return false;
                var decoded = streamToken.Decode(doc.Structure.FilterProvider, doc.Structure.TokenScanner);
                byte[] raw = decoded.ToArray();

                string colorSpace = dict.TryGet(NameToken.ColorSpace, doc.Structure.TokenScanner, out IToken csTok)
                    ? csTok.ToString() ?? string.Empty
                    : string.Empty;

                // RGB (3 канала)
                if (raw.Length == width * height * 3)
                {
                    bytes = WrapRgb24AsBmp(raw, width, height);
                    extension = ".bmp";
                    return true;
                }

                // CMYK (4 канала) -> конвертируем в RGB24
                if (raw.Length == width * height * 4 && colorSpace.Contains("CMYK", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] rgb = ConvertCmykToRgb24(raw, width, height);
                    bytes = WrapRgb24AsBmp(rgb, width, height);
                    extension = ".bmp";
                    return true;
                }

                // RGBA / RGB+Alpha (4 канала)
                if (raw.Length == width * height * 4)
                {
                    byte[] rgb = new byte[width * height * 3];
                    for (int i = 0; i < width * height; i++)
                    {
                        rgb[i * 3] = raw.ElementAt(i * 4);
                        rgb[i * 3 + 1] = raw.ElementAt(i * 4 + 1);
                        rgb[i * 3 + 2] = raw.ElementAt(i * 4 + 2);
                    }
                    bytes = WrapRgb24AsBmp(rgb, width, height);
                    extension = ".bmp";
                    return true;
                }

                // Grayscale (1 канал)
                if (raw.Length == width * height)
                {
                    byte[] rgb = new byte[width * height * 3];
                    for (int i = 0; i < raw.Length; i++)
                    {
                        byte g = raw.ElementAt(i);
                        rgb[i * 3] = g;
                        rgb[i * 3 + 1] = g;
                        rgb[i * 3 + 2] = g;
                    }
                    bytes = WrapRgb24AsBmp(rgb, width, height);
                    extension = ".bmp";
                    return true;
                }
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }

            return false;
        }
        private static bool TryExtractAnyImage(IPdfImage pdfImg, out byte[]? bytes, out string extension)
        {
            bytes = null;
            extension = ".png";

            try
            {
                string filterStr = string.Empty;
                if (pdfImg.ImageDictionary != null &&
                    pdfImg.ImageDictionary.TryGet(NameToken.Filter, out var filterToken))
                {
                    filterStr = filterToken.ToString() ?? string.Empty;
                }

                if (filterStr.Contains("DCTDecode", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] jpegBytes = pdfImg.RawBytes.ToArray();
                    if (jpegBytes.Length > 0)
                    {
                        bytes = jpegBytes;
                        extension = ".jpg";
                        return true;
                    }
                }

                if (pdfImg.TryGetPng(out byte[]? pngBytes) && pngBytes is { Length: > 0 })
                {
                    bytes = pngBytes;
                    extension = ".png";
                    return true;
                }

                if (pdfImg.TryGetBytesAsMemory(out var decodedMem) && decodedMem.Length > 0)
                {
                    int width = pdfImg.WidthInSamples;
                    int height = pdfImg.HeightInSamples;
                    byte[] raw = decodedMem.ToArray();
                    int pixelCount = width * height;

                    if (width > 0 && height > 0 && raw.Length >= pixelCount)
                    {
                        int bytesPerPixel = raw.Length / pixelCount;
                        string colorSpace = pdfImg.ImageDictionary != null &&
                                            pdfImg.ImageDictionary.TryGet(NameToken.ColorSpace, out var csTok)
                            ? csTok.ToString() ?? string.Empty
                            : string.Empty;

                        if (bytesPerPixel == 3)
                        {
                            bytes = WrapRgb24AsBmp(raw, width, height);
                            extension = ".bmp";
                            return true;
                        }
                        if (bytesPerPixel == 4)
                        {
                            byte[] rgb24 = colorSpace.Contains("CMYK", StringComparison.OrdinalIgnoreCase)
                                ? ConvertCmykToRgb24(raw, width, height)
                                : ExtractRgbFromRgba(raw, pixelCount);

                            bytes = WrapRgb24AsBmp(rgb24, width, height);
                            extension = ".bmp";
                            return true;
                        }
                        if (bytesPerPixel == 1)
                        {
                            byte[] rgb24 = new byte[pixelCount * 3];
                            for (int i = 0; i < pixelCount; i++)
                            {
                                byte g = raw.ElementAt(i);
                                rgb24[i * 3] = g;
                                rgb24[i * 3 + 1] = g;
                                rgb24[i * 3 + 2] = g;
                            }
                            bytes = WrapRgb24AsBmp(rgb24, width, height);
                            extension = ".bmp";
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            return false;
        }


        private static byte[] ExtractRgbFromRgba(byte[] rgba, int pixelCount)
        {
            byte[] rgb = new byte[pixelCount * 3];
            for (int i = 0; i < pixelCount; i++)
            {
                rgb[i * 3] = rgba.ElementAt(i * 4);
                rgb[i * 3 + 1] = rgba.ElementAt(i * 4 + 1);
                rgb[i * 3 + 2] = rgba.ElementAt(i * 4 + 2);
            }
            return rgb;
        }

        private static byte[] ConvertCmykToRgb24(byte[] cmyk, int width, int height)
        {
            int pixelCount = width * height;
            byte[] rgb = new byte[pixelCount * 3];

            for (int i = 0; i < pixelCount; i++)
            {
                double c = cmyk.ElementAt(i * 4) / 255.0;
                double m = cmyk.ElementAt(i * 4 + 1) / 255.0;
                double y = cmyk.ElementAt(i * 4 + 2) / 255.0;
                double k = cmyk.ElementAt(i * 4 + 3) / 255.0;

                rgb[i * 3] = (byte)Math.Round(255.0 * (1.0 - c) * (1.0 - k));
                rgb[i * 3 + 1] = (byte)Math.Round(255.0 * (1.0 - m) * (1.0 - k));
                rgb[i * 3 + 2] = (byte)Math.Round(255.0 * (1.0 - y) * (1.0 - k));
            }

            return rgb;
        }
        private static byte[] WrapRgb24AsBmp(byte[] rgb24, int width, int height)
        {
            int rowStride = (width * 3 + 3) & ~3;
            int pixelDataSize = rowStride * height;
            int fileSize = 54 + pixelDataSize;

            byte[] bmp = new byte[fileSize];
            Encoding.ASCII.GetBytes("BM").CopyTo(bmp, 0);
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(54).CopyTo(bmp, 10);
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(width).CopyTo(bmp, 18);
            BitConverter.GetBytes(-height).CopyTo(bmp, 22);
            BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
            BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
            BitConverter.GetBytes(pixelDataSize).CopyTo(bmp, 34);

            for (int y = 0; y < height; y++)
            {
                int srcRowOffset = y * width * 3;
                int dstRowOffset = 54 + y * rowStride;
                for (int x = 0; x < width; x++)
                {
                    bmp[dstRowOffset + x * 3] = rgb24[srcRowOffset + x * 3 + 2];
                    bmp[dstRowOffset + x * 3 + 1] = rgb24[srcRowOffset + x * 3 + 1];
                    bmp[dstRowOffset + x * 3 + 2] = rgb24[srcRowOffset + x * 3];
                }
            }

            return bmp;
        }
    }
}
