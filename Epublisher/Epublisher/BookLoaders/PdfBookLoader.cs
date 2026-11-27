using CommunityToolkit.Mvvm.DependencyInjection;
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
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;
using static System.Net.Mime.MediaTypeNames;

namespace Epublisher.BookLoaders
{
    public class PdfBookLoader : IBookLoader
    {
        private sealed record ParsedParagraph(string PlainText, string HtmlText);
        private sealed class PageLayoutElement
        {
            public double FontSize { get; init; }
            public double TopY { get; init; }
            public double BottomY { get; init; }
            public double LeftX { get; init; }
            public double RightX { get; init; }
            public string Html { get; set; } = string.Empty;
            public bool IsHeading { get; init; }
            public bool IsFullPageImage { get; init; }
            public string? ChapterTitleCandidate { get; init; }
            public string? ImageFileName { get; set; }
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
        private sealed class PdfConversionState
        {
            public string? CoverExt { get; set; } = ".png";
            public byte[]? CoverBytes { get; set; }
            public Dictionary<string, byte[]> ExtraImages { get; } = new();
            public List<EpubChapterDraft> Chapters { get; } = new();
            public StringBuilder CurrentChapterSb { get; } = new();
            public string CurrentChapterTitle { get; set; }
            public bool HasContentInCurrentChapter { get; set; }
            public bool CurrentChapterHasHeading { get; set; }
            public int LastHeadingPageNum { get; set; } = -1;
            public bool IsCollectingChapterTitle { get; set; }
            public List<(int PageNum, string ImgFileName)> PendingFullPageImages { get; } = new();

            public PdfConversionState(string initialTitle)
            {
                CurrentChapterTitle = initialTitle;
            }

            public void FlushCurrentChapter()
            {
                if (HasContentInCurrentChapter && CurrentChapterSb.Length > 0)
                {
                    Chapters.Add(new EpubChapterDraft(CurrentChapterTitle, CurrentChapterSb.ToString()));
                    CurrentChapterSb.Clear();
                    HasContentInCurrentChapter = false;
                    CurrentChapterHasHeading = false;
                }
            }

            public void FlushPendingImagesAsStandaloneChapters()
            {
                foreach (var img in PendingFullPageImages)
                {
                    string fullPageHtml = $"<div class=\"fullpage-image\"><img src=\"../Images/{img.ImgFileName}\" alt=\"\"/></div>";
                    Chapters.Add(new EpubChapterDraft($"Иллюстрация (стр. {img.PageNum})", fullPageHtml));
                }
                PendingFullPageImages.Clear();
            }
        }
        private static bool IsInvisibleGlyph(Letter letter)
        {
            if (letter.RenderingMode == TextRenderingMode.Neither)
                return true;

            if (letter.Value == "." && letter.Color != null)
            {
                var rgb = letter.Color.ToRGBValues();
                if (rgb.r > 0.96 && rgb.g > 0.96 && rgb.b > 0.96)
                    return true;
            }

            return false;
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
                var state = new PdfConversionState(title);

                for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
                {
                    Page page = document.GetPage(pageNum);
                    var rawElements = ExtractPageElements(page, document, pageNum, bodyFontSize, state);
                    var mergedElements = MergeAdjacentElements(rawElements, bodyFontSize);
                    AppendPageElementsToChapters(mergedElements, pageNum, state);
                }

                state.FlushCurrentChapter();
                state.FlushPendingImagesAsStandaloneChapters();
                LinkTableOfContentsChapters(state.Chapters);

                var additionStyles = """
            .pdf-toc-entry {
                text-indent: 0;
                margin: 0.3em 0;
            }

            """+ CssHelper.FootnoteCss;

                string? savedCoverPath = await EpubStructureWriter.WriteUnpackedEpubAsync(
                    targetBookFolder, bookId, title, author, detectedLang,
                    state.Chapters, state.CoverBytes, state.CoverExt ?? ".png", state.ExtraImages, additionStyles);

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
        private static List<PageLayoutElement> ExtractPageElements(
    Page page, PdfDocument document, int pageNum, double bodyFontSize, PdfConversionState state)
        {
            var elements = ExtractImagesFromPage(page, document, pageNum, state);

            var lines = GroupWordsIntoTextLines(page, bodyFontSize, elements.Any(e => e.IsFullPageImage));
            if (lines.Count == 0)
                return elements.OrderByDescending(e => e.TopY).ToList();

            var textElements = BuildTextElementsFromLines(lines, page.Width, bodyFontSize, out bool isTocPage);
            if (isTocPage)
                elements.RemoveAll(e => e.IsFullPageImage);

            elements.AddRange(textElements);

            var sortedElements = elements.OrderByDescending(e => e.TopY).ToList();
            LinkPageFootnotes(sortedElements, pageNum, page.Height);
            return sortedElements;
        }
        private static List<PageLayoutElement> ExtractImagesFromPage(
    Page page, PdfDocument document, int pageNum, PdfConversionState state)
        {
            var elements = new List<PageLayoutElement>();
            int imgCounter = 1;

            foreach (var img in ExtractAllImagesFromPage(page, document))
            {
                if (img.Width < 15 || img.Height < 15) continue;

                bool isFullPageSize = img.Width >= page.Width * 0.55 || img.Height >= page.Height * 0.55;
                if (pageNum <= 2 && state.CoverBytes == null && isFullPageSize)
                {
                    state.CoverBytes = img.Bytes;
                    state.CoverExt = img.Extension;
                    continue;
                }

                string imgFileName = $"img_p{pageNum}_{imgCounter++}{img.Extension}";
                state.ExtraImages[imgFileName] = img.Bytes;

                string imgHtml = isFullPageSize
                    ? $"<div class=\"fullpage-image\"><img src=\"../Images/{imgFileName}\" alt=\"\"/></div>"
                    : $"<div class=\"pdf-image\"><img src=\"../Images/{imgFileName}\" alt=\"\"/></div>";

                elements.Add(new PageLayoutElement
                {
                    TopY = img.TopY,
                    BottomY = img.BottomY,
                    Html = imgHtml,
                    IsFullPageImage = isFullPageSize,
                    ImageFileName = imgFileName
                });
            }

            return elements;
        }
        private static List<TextLine> GroupWordsIntoTextLines(Page page, double bodyFontSize, bool hasFullPageImage)
        {
            double topMarginY = page.Height * 0.955;
            double bottomMarginY = page.Height * 0.045;
            double runningHeaderZoneY = page.Height * 0.935;

            var visibleLetters = page.Letters.Where(l => !IsInvisibleGlyph(l)).ToList();
            var words = NearestNeighbourWordExtractor.Instance.GetWords(visibleLetters)
                .Where(w => w.BoundingBox.Bottom <= topMarginY && w.BoundingBox.Top >= bottomMarginY)
                .OrderByDescending(w => w.Letters.First().StartBaseLine.Y)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            if (hasFullPageImage && words.Count < 15)
                return new List<TextLine>();

            var buckets = new List<(double BaselineY, List<Word> Words)>();

            foreach (var word in words)
            {
                double wordBaselineY = word.Letters.Average(l => l.StartBaseLine.Y);
                double wordAvgSize = word.Letters.Average(l => l.PointSize);

                int matchIdx = -1;
                bool updateBaselineToWord = false;

                for (int i = 0; i < buckets.Count; i++)
                {
                    double bucketAvgSize = buckets[i].Words.SelectMany(w => w.Letters).Average(l => l.PointSize);
                    double diffY = wordBaselineY - buckets[i].BaselineY;

                    bool isSameBaseline = Math.Abs(diffY) <= bodyFontSize * 0.25;

                    // Case A: bucket is normal text, incoming word is a raised/lowered superscript/subscript
                    bool wordIsSupOrSub = wordAvgSize < bodyFontSize * 0.90 &&
                                          diffY >= -bodyFontSize * 0.35 &&
                                          diffY <= bodyFontSize * 0.75;

                    // Case B: bucket was created first by a raised superscript (like '*'), incoming word is the normal line below it
                    bool bucketIsSupOrSub = buckets[i].Words.Count == 1 &&
                                            bucketAvgSize < bodyFontSize * 0.90 &&
                                            diffY <= 0 &&
                                            diffY >= -bodyFontSize * 0.75;

                    if (isSameBaseline || wordIsSupOrSub || bucketIsSupOrSub)
                    {
                        matchIdx = i;
                        updateBaselineToWord = bucketIsSupOrSub && wordAvgSize >= bodyFontSize * 0.90;
                        break;
                    }
                }

                if (matchIdx >= 0)
                {
                    buckets[matchIdx].Words.Add(word);
                    if (updateBaselineToWord)
                    {
                        buckets[matchIdx] = (wordBaselineY, buckets[matchIdx].Words);
                    }
                }
                else
                {
                    buckets.Add((wordBaselineY, new List<Word> { word }));
                }
            }

            return buckets
                .OrderByDescending(b => b.BaselineY)
                .Select(b => new TextLine(b.Words.OrderBy(w => w.BoundingBox.Left).ToList()))
                .Where(line =>
                {
                    var letters = line.Words.SelectMany(w => w.Letters).ToList();
                    string rawText = string.Concat(letters.Select(l => l.Value)).Trim();
                    if (line.BoundingBox.Bottom > runningHeaderZoneY && letters.Count <= 60)
                        return false;
                    if (line.BoundingBox.Top < page.Height * 0.08 && Regex.IsMatch(rawText, @"^[\-\–\—\s]*\d{1,4}[\-\–\—\s]*$"))
                        return false;
                    return true;
                })
                .ToList();
        }
        private static List<PageLayoutElement> BuildTextElementsFromLines(
    IReadOnlyList<TextLine> lines, double pageWidth, double bodyFontSize, out bool isTocPage)
        {
            var elements = new List<PageLayoutElement>();
            var tocLineRegex = new Regex(@"^(.+?)\s*[\.·…_\-]{3,}\s*(\d+)\s*$");

            double pageColumnWidth = lines.Max(l => l.BoundingBox.Width);
            double pageColumnLeft = lines.Min(l => l.BoundingBox.Left);
            double pageColumnCenter = pageColumnLeft + pageColumnWidth / 2.0;

            var currentPlain = new StringBuilder();
            var currentHtml = new StringBuilder();
            var currentLetters = new List<Letter>();
            double paraTopY = 0, paraBottomY = 0, paraLeftX = 0, paraRightX = 0;
            double? prevBottom = null;

            var rawParagraphs = new List<(ParsedParagraph Para, PageLayoutElement Meta)>();
            int tocLineCount = 0;

            void FlushParagraph()
            {
                if (currentPlain.Length == 0 || currentLetters.Count == 0) return;

                string plain = currentPlain.ToString().Trim();
                string html = currentHtml.ToString().Trim();
                double avgFontSize = currentLetters.Average(l => l.PointSize);
                bool isBold = currentLetters.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true) > currentLetters.Count * 0.6;
                double width = paraRightX - paraLeftX;
                double center = paraLeftX + width / 2.0;
                bool isCentered = paraLeftX > pageColumnLeft + pageColumnWidth * 0.15 &&
                                  Math.Abs(center - pageColumnCenter) < pageColumnWidth * 0.08 &&
                                  width < pageColumnWidth * 0.78;

                if (tocLineRegex.IsMatch(plain))
                    tocLineCount++;

                rawParagraphs.Add((
                    new ParsedParagraph(plain, html),
                    new PageLayoutElement
                    {
                        FontSize = avgFontSize,
                        TopY = paraTopY,
                        BottomY = paraBottomY,
                        LeftX = paraLeftX,
                        RightX = paraRightX,
                        IsHeading = isBold,
                        ChapterTitleCandidate = isCentered ? "centered" : null
                    }));

                currentPlain.Clear();
                currentHtml.Clear();
                currentLetters.Clear();
                prevBottom = null;
            }

            foreach (var line in lines)
            {
                var lineLetters = line.Words.SelectMany(w => w.Letters).ToList();
                if (lineLetters.Count == 0) continue;

                double lineFontSize = lineLetters.Average(l => l.PointSize);
                var parsedLine = BuildLineWithSubSup(line.Words, lineFontSize);
                if (string.IsNullOrWhiteSpace(parsedLine.PlainText)) continue;

                if (currentPlain.Length == 0)
                {
                    currentPlain.Append(parsedLine.PlainText);
                    currentHtml.Append(parsedLine.HtmlText);
                    currentLetters.AddRange(lineLetters);
                    paraTopY = line.BoundingBox.Top;
                    paraBottomY = line.BoundingBox.Bottom;
                    paraLeftX = line.BoundingBox.Left;
                    paraRightX = line.BoundingBox.Right;
                    prevBottom = line.BoundingBox.Bottom;
                    continue;
                }

                string currentSoFar = currentPlain.ToString().TrimEnd();
                bool prevEndedToc = Regex.IsMatch(currentSoFar, @"[\.·…_\-]{2,}\s*\d+$");
                bool endsWithHyphen = currentPlain[^1] == '-' || currentPlain[^1] == '‐' || currentPlain[^1] == '‑';
                bool startsWithLower = char.IsLower(parsedLine.PlainText, 0);

                if (!prevEndedToc && endsWithHyphen && startsWithLower)
                {
                    currentPlain.Length--;
                    currentPlain.Append(parsedLine.PlainText);
                    if (currentHtml.Length > 0 && (currentHtml[^1] == '-' || currentHtml[^1] == '‐' || currentHtml[^1] == '‑'))
                        currentHtml.Length--;
                    currentHtml.Append(parsedLine.HtmlText);
                    currentLetters.AddRange(lineLetters);
                    paraBottomY = line.BoundingBox.Bottom;
                    paraLeftX = Math.Min(paraLeftX, line.BoundingBox.Left);
                    paraRightX = Math.Max(paraRightX, line.BoundingBox.Right);
                    prevBottom = line.BoundingBox.Bottom;
                    continue;
                }

                double verticalGap = prevBottom!.Value - line.BoundingBox.Top;
                bool hasIndent = (line.BoundingBox.Left - pageColumnLeft) > lineFontSize * 1.1;
                bool fontSizeChanged = Math.Abs(lineFontSize - currentLetters.Average(l => l.PointSize)) > 0.6;
                bool wasCenteredNowLeft = paraLeftX > pageColumnLeft + pageColumnWidth * 0.15 &&
                                          line.BoundingBox.Left <= pageColumnLeft + lineFontSize * 0.5;
                bool isLineBold = lineLetters.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true) > lineLetters.Count * 0.6;
                bool currentIsBold = currentLetters.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true) > currentLetters.Count * 0.6;
                bool boldChanged = isLineBold != currentIsBold;

                if (prevEndedToc || verticalGap > lineFontSize * 0.85 || hasIndent || fontSizeChanged || wasCenteredNowLeft || boldChanged)
                {
                    FlushParagraph();
                    currentPlain.Append(parsedLine.PlainText);
                    currentHtml.Append(parsedLine.HtmlText);
                    currentLetters.AddRange(lineLetters);
                    paraTopY = line.BoundingBox.Top;
                    paraBottomY = line.BoundingBox.Bottom;
                    paraLeftX = line.BoundingBox.Left;
                    paraRightX = line.BoundingBox.Right;
                }
                else
                {
                    currentPlain.Append(' ');
                    currentPlain.Append(parsedLine.PlainText);
                    currentHtml.Append(' ');
                    currentHtml.Append(parsedLine.HtmlText);
                    currentLetters.AddRange(lineLetters);
                    paraBottomY = line.BoundingBox.Bottom;
                    paraLeftX = Math.Min(paraLeftX, line.BoundingBox.Left);
                    paraRightX = Math.Max(paraRightX, line.BoundingBox.Right);
                }

                prevBottom = line.BoundingBox.Bottom;
            }

            FlushParagraph();
            isTocPage = tocLineCount >= 2;
            int firstTocIndex = rawParagraphs.FindIndex(p => tocLineRegex.IsMatch(p.Para.PlainText));

            for (int i = 0; i < rawParagraphs.Count; i++)
            {
                var (para, meta) = rawParagraphs[i];
                double bottomY = meta.BottomY;
                bool isTocTitle = isTocPage && firstTocIndex > 0 && i < firstTocIndex;

                if (!isTocTitle && i + 1 < rawParagraphs.Count && !tocLineRegex.IsMatch(para.PlainText))
                {
                    var (nextPara, nextMeta) = rawParagraphs[i + 1];
                    if (tocLineRegex.IsMatch(nextPara.PlainText) && para.PlainText.Length < 100)
                    {
                        para = new ParsedParagraph(
                            $"{para.PlainText} {nextPara.PlainText}",
                            $"{para.HtmlText} {nextPara.HtmlText}");
                        bottomY = nextMeta.BottomY;
                        i++;
                    }
                }

                var tocMatch = tocLineRegex.Match(para.PlainText);
                if (tocMatch.Success)
                {
                    string encodedToc = WebUtility.HtmlEncode(tocMatch.Groups[1].Value.Trim());
                    elements.Add(new PageLayoutElement
                    {
                        FontSize = meta.FontSize,
                        TopY = meta.TopY,
                        BottomY = bottomY,
                        LeftX = meta.LeftX,
                        RightX = meta.RightX,
                        Html = $"<p class=\"pdf-toc-entry\" data-toc-target=\"{encodedToc}\">{encodedToc}</p>"
                    });
                    continue;
                }

                bool isBold = meta.IsHeading;
                bool isCentered = meta.ChapterTitleCandidate == "centered";
                bool isLargeH1 = meta.FontSize >= bodyFontSize * 1.45;
                bool isLargeH2 = meta.FontSize >= bodyFontSize * 1.22;
                bool isCenteredBoldHeading = isCentered && isBold && para.PlainText.Length <= 100 && !para.PlainText.EndsWith('.');
                bool isHeading = isTocPage
                    ? (isTocTitle || isLargeH1 || isLargeH2)
                    : (isLargeH1 || isLargeH2 || isCenteredBoldHeading);

                string elementHtml = isLargeH1
                    ? $"<h1>{para.HtmlText}</h1>"
                    : isHeading
                        ? $"<h2>{para.HtmlText}</h2>"
                        : $"<p{(isCentered ? " style=\"text-align: center; text-indent: 0;\"" : string.Empty)}>{para.HtmlText}</p>";

                elements.Add(new PageLayoutElement
                {
                    FontSize = meta.FontSize,
                    TopY = meta.TopY,
                    BottomY = bottomY,
                    LeftX = meta.LeftX,
                    RightX = meta.RightX,
                    Html = elementHtml,
                    IsHeading = isHeading,
                    ChapterTitleCandidate = isHeading ? para.PlainText : null
                });
            }

            return elements;
        }

        private static List<PageLayoutElement> MergeAdjacentElements(List<PageLayoutElement> sortedElements, double bodyFontSize)
        {
            var result = new List<PageLayoutElement>();

            for (int i = 0; i < sortedElements.Count; i++)
            {
                var el = sortedElements[i];

                if (i + 1 < sortedElements.Count &&
                    !el.IsHeading &&
                    !el.Html.Contains("pdf-toc-entry") &&
                    !el.Html.Contains("image") &&
                    sortedElements[i + 1].Html.Contains("pdf-toc-entry"))
                {
                    var nextEl = sortedElements[i + 1];
                    string firstPart = Regex.Replace(el.Html, "<[^>]+>", string.Empty).Trim();
                    bool sameFontSize = Math.Abs(el.FontSize - nextEl.FontSize) < 0.5;
                    bool closeVertically = (el.BottomY - nextEl.TopY) < el.FontSize * 1.5;

                    var nextMatch = Regex.Match(nextEl.Html, @"data-toc-target=""([^""]+)"">(.*?)</p>");
                    if (sameFontSize && closeVertically && nextMatch.Success && firstPart.Length < 120)
                    {
                        string encodedCombined = WebUtility.HtmlEncode($"{firstPart} {WebUtility.HtmlDecode(nextMatch.Groups[1].Value)}");
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

                result.Add(el);
            }

            return result;
        }

        private static void AppendPageElementsToChapters(
    List<PageLayoutElement> elements, int pageNum, PdfConversionState state)
        {
            bool chapterStartedOnThisPage = false;

            foreach (var el in elements)
            {
                if (el.IsFullPageImage && !string.IsNullOrEmpty(el.ImageFileName))
                {
                    state.PendingFullPageImages.Add((pageNum, el.ImageFileName));
                    continue;
                }

                bool isDifferentPageFromLastHeading = pageNum != state.LastHeadingPageNum;

                if (el.IsHeading)
                {
                    if (!chapterStartedOnThisPage && state.HasContentInCurrentChapter && state.CurrentChapterHasHeading && isDifferentPageFromLastHeading)
                    {
                        state.FlushCurrentChapter();
                        state.CurrentChapterTitle = el.ChapterTitleCandidate ?? $"Section {state.Chapters.Count + 1}";
                        state.IsCollectingChapterTitle = true;
                    }

                    if (state.PendingFullPageImages.Count > 0)
                    {
                        state.FlushCurrentChapter();
                        state.FlushPendingImagesAsStandaloneChapters();
                    }

                    if (!state.HasContentInCurrentChapter)
                    {
                        state.CurrentChapterTitle = el.ChapterTitleCandidate ?? state.CurrentChapterTitle;
                        state.IsCollectingChapterTitle = true;
                    }
                    else if (state.IsCollectingChapterTitle && !string.IsNullOrWhiteSpace(el.ChapterTitleCandidate))
                    {
                        state.CurrentChapterTitle = $"{state.CurrentChapterTitle} {el.ChapterTitleCandidate}";
                    }

                    state.LastHeadingPageNum = pageNum;
                    chapterStartedOnThisPage = true;
                    state.CurrentChapterHasHeading = true;
                }
                else
                {
                    state.IsCollectingChapterTitle = false;

                    if (state.PendingFullPageImages.Count > 0)
                    {
                        if (!state.HasContentInCurrentChapter)
                        {
                            state.FlushPendingImagesAsStandaloneChapters();
                        }
                        else
                        {
                            foreach (var pendingImg in state.PendingFullPageImages)
                            {
                                // Keep fullpage-image class when the image shares the same page as title/front-matter text;
                                // use pdf-image when a dedicated illustration page sits mid-chapter
                                string imgDivClass = pendingImg.PageNum == pageNum ? "fullpage-image" : "pdf-image";
                                state.CurrentChapterSb.AppendLine($"<div class=\"{imgDivClass}\"><img src=\"../Images/{pendingImg.ImgFileName}\" alt=\"\"/></div>");
                            }
                            state.PendingFullPageImages.Clear();
                        }
                    }

                    // Join hyphenated or incomplete paragraphs split across page boundaries
                    if (!el.IsFullPageImage &&
                        !el.Html.Contains("pdf-toc-entry") &&
                        !el.Html.Contains("class=\"footnote\"") &&
                        el.Html.StartsWith("<p>", StringComparison.OrdinalIgnoreCase))
                    {
                        string currentChapterText = state.CurrentChapterSb.ToString().TrimEnd();
                        var lastParaMatch = Regex.Match(currentChapterText, @"<p>([\s\S]*?)</p>\s*$");
                        var newParaMatch = Regex.Match(el.Html.Trim(), @"^<p>([\s\S]*?)</p>$");

                        if (lastParaMatch.Success && newParaMatch.Success)
                        {
                            string prevInner = lastParaMatch.Groups[1].Value.TrimEnd();
                            string nextInner = newParaMatch.Groups[1].Value.TrimStart();
                            string nextPlain = Regex.Replace(nextInner, "<[^>]+>", string.Empty).TrimStart();

                            bool startsLower = nextPlain.Length > 0 && char.IsLower(nextPlain, 0);
                            bool endsHyphen = prevInner.EndsWith('-') || prevInner.EndsWith('‐') || prevInner.EndsWith('‑');
                            bool endsNoTerminalPunct = prevInner.Length > 0 && !Regex.IsMatch(prevInner, @"[.!?:;»”\…\]]$");

                            if (startsLower && (endsHyphen || endsNoTerminalPunct))
                            {
                                string joinedInner = endsHyphen
                                    ? $"{prevInner[..^1]}{nextInner}"
                                    : $"{prevInner} {nextInner}";

                                string updatedPrev = currentChapterText[..lastParaMatch.Index] + $"<p>{joinedInner}</p>";
                                state.CurrentChapterSb.Clear();
                                state.CurrentChapterSb.AppendLine(updatedPrev);
                                state.HasContentInCurrentChapter = true;
                                continue;
                            }
                        }
                    }
                }

                state.CurrentChapterSb.AppendLine(el.Html);
                state.HasContentInCurrentChapter = true;
            }
        }

        private static void LinkPageFootnotes(List<PageLayoutElement> sortedElements, int pageNum, double pageHeight)
        {
            double footnoteZoneTopY = pageHeight * 0.30;
            int footnoteCounter = 1;
            var footnoteDefs = new List<(PageLayoutElement Element, string Symbol)>();

            foreach (var el in sortedElements)
            {
                if (el.IsFullPageImage || el.IsHeading || el.BottomY > footnoteZoneTopY)
                    continue;

                var startMarkerMatch = Regex.Match(
                    el.Html,
                    @"^<p[^>]*>\s*(?:<sup>([^<]{1,6})</sup>|([\*†‡§]+|\d{1,3}))\s+",
                    RegexOptions.IgnoreCase);

                if (startMarkerMatch.Success)
                {
                    string rawSymbol = startMarkerMatch.Groups[1].Success
                        ? startMarkerMatch.Groups[1].Value
                        : startMarkerMatch.Groups[2].Value;
                    string symbol = WebUtility.HtmlDecode(rawSymbol).Trim();

                    if (!string.IsNullOrEmpty(symbol))
                    {
                        footnoteDefs.Add((el, symbol));
                    }
                }
            }

            if (footnoteDefs.Count == 0) return;

            foreach (var (defEl, symbol) in footnoteDefs)
            {
                string encodedSymbol = WebUtility.HtmlEncode(symbol);
                string targetPattern = $@"<sup>{Regex.Escape(encodedSymbol)}</sup>";

                PageLayoutElement? refEl = sortedElements.FirstOrDefault(e =>
                    e != defEl &&
                    !e.IsFullPageImage &&
                    e.BottomY > defEl.TopY &&
                    Regex.IsMatch(e.Html, targetPattern));

                string fnId = $"fn-p{pageNum}-{footnoteCounter}";
                string refId = $"fnref-p{pageNum}-{footnoteCounter}";
                footnoteCounter++;

                string bodyInside = Regex.Replace(defEl.Html, @"^<p[^>]*>|</p>$", string.Empty, RegexOptions.IgnoreCase).Trim();
                bodyInside = Regex.Replace(
                    bodyInside,
                    @"^(?:<sup>[^<]+</sup>|[\*†‡§]+|\d{1,3})\s+",
                    $"<sup>{encodedSymbol}</sup> ");

                if (refEl != null)
                {
                    var regex = new Regex(targetPattern);
                    refEl.Html = regex.Replace(
                        refEl.Html,
                        $"<sup><a id=\"{refId}\" href=\"#{fnId}\">{encodedSymbol}</a></sup>",
                        count: 1);

                    defEl.Html = $"<p id=\"{fnId}\" class=\"footnote\">{bodyInside} <a href=\"#{refId}\">&#8617;</a></p>";
                }
                else
                {
                    defEl.Html = $"<p id=\"{fnId}\" class=\"footnote\">{bodyInside}</p>";
                }
            }

            var lastFootnote = footnoteDefs[footnoteDefs.Count - 1].Element;
            lastFootnote.Html += "\n<div class=\"epub-page-break\"></div>";
        }

        private static ParsedParagraph BuildLineWithSubSup(IReadOnlyList<Word> words, double fallbackFontSize)
        {
            var sortedLetters = words
                .SelectMany(w => w.Letters)
                .Where(l => !IsInvisibleGlyph(l))
                .OrderBy(l => l.StartBaseLine.X)
                .ToList();

            if (sortedLetters.Count == 0)
                return new ParsedParagraph(string.Empty, string.Empty);

            var orderedSizes = sortedLetters.Select(l => l.PointSize).OrderBy(s => s).ToList();
            double normalSize = orderedSizes.Count > 0 ? orderedSizes[orderedSizes.Count / 2] : fallbackFontSize;

            var normalLetters = sortedLetters
                .Where(l => Math.Abs(l.PointSize - normalSize) <= normalSize * 0.12)
                .ToList();

            double normalBaseline = normalLetters.Count > 0
                ? normalLetters.Select(l => l.StartBaseLine.Y).OrderBy(y => y).ElementAt(normalLetters.Count / 2)
                : sortedLetters.First().StartBaseLine.Y;

            var plainSb = new StringBuilder();
            var htmlSb = new StringBuilder();
            string activeTag = string.Empty;
            Letter? prevLetter = null;

            foreach (var letter in sortedLetters)
            {
                bool isPunctuation = letter.Value is "." or "," or "!" or "?" or ":" or ";" or "»" or ")" or "]";

                if (prevLetter != null && !isPunctuation)
                {
                    double gap = letter.StartBaseLine.X - prevLetter.EndBaseLine.X;
                    if (gap > normalSize * 0.08)
                    {
                        if (!string.IsNullOrEmpty(activeTag))
                        {
                            htmlSb.Append($"</{activeTag}>");
                            activeTag = string.Empty;
                        }
                        plainSb.Append(' ');
                        htmlSb.Append(' ');
                    }
                }

                double deltaY = letter.StartBaseLine.Y - normalBaseline;
                bool isSmallerFont = letter.PointSize < normalSize * 0.88;

                string targetTag = string.Empty;
                if (isSmallerFont && deltaY > normalSize * 0.14)
                    targetTag = "sup";
                else if (isSmallerFont && deltaY < -normalSize * 0.10)
                    targetTag = "sub";

                if (targetTag != activeTag)
                {
                    if (!string.IsNullOrEmpty(activeTag))
                        htmlSb.Append($"</{activeTag}>");
                    if (!string.IsNullOrEmpty(targetTag))
                        htmlSb.Append($"<{targetTag}>");

                    activeTag = targetTag;
                }

                string cleanChar = WebUtility.HtmlDecode(letter.Value);
                plainSb.Append(cleanChar);
                htmlSb.Append(WebUtility.HtmlEncode(cleanChar));
                prevLetter = letter;
            }

            if (!string.IsNullOrEmpty(activeTag))
            {
                htmlSb.Append($"</{activeTag}>");
            }

            string cleanPlain = string.Join(" ", plainSb.ToString().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
            string cleanHtml = string.Join(" ", htmlSb.ToString().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

            return new ParsedParagraph(cleanPlain.Trim(), cleanHtml.Trim());
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
        private static void LinkTableOfContentsChapters(List<EpubChapterDraft> chapters)
        {
            var tocEntryRegex = new Regex(
                @"<p\s+class=""pdf-toc-entry""\s+data-toc-target=""([^""]+)"">([\s\S]*?)</p>",
                RegexOptions.IgnoreCase);

            static string NormalizeAlphaNumeric(string text)
            {
                string noTags = Regex.Replace(text, "<[^>]+>", " ");
                string decoded = WebUtility.HtmlDecode(noTags);
                return Regex.Replace(decoded, @"[^\p{L}\p{N}]", string.Empty).ToLowerInvariant();
            }

            for (int cIdx = 0; cIdx < chapters.Count; cIdx++)
            {
                var chapter = chapters[cIdx];
                if (!chapter.HtmlBodyContent.Contains("pdf-toc-entry", StringComparison.OrdinalIgnoreCase))
                    continue;

                var matches = tocEntryRegex.Matches(chapter.HtmlBodyContent);
                if (matches.Count == 0) continue;

                var listItemsSb = new StringBuilder();
                listItemsSb.AppendLine("<nav epub:type=\"toc\">");
                listItemsSb.AppendLine("  <ol style=\"list-style: none;\">");

                foreach (Match match in matches)
                {
                    string rawTarget = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                    string cleanDisplay = Regex.Replace(match.Groups[2].Value, "<[^>]+>", string.Empty).Trim();
                    string normTarget = NormalizeAlphaNumeric(rawTarget);

                    int matchedChapterIdx = -1;
                    if (!string.IsNullOrEmpty(normTarget))
                    {
                        for (int i = 0; i < chapters.Count; i++)
                        {
                            if (i == cIdx) continue;
                            string normChapTitle = NormalizeAlphaNumeric(chapters[i].Title);
                            if (string.IsNullOrEmpty(normChapTitle)) continue;

                            bool exact = normChapTitle == normTarget;
                            bool prefixMatch = false;

                            if (!exact && Math.Min(normChapTitle.Length, normTarget.Length) >= 4)
                            {
                                if (normChapTitle.StartsWith(normTarget, StringComparison.Ordinal))
                                {
                                    // Ensure the next character isn't a digit (so "глава1" doesn't match "глава10")
                                    char nextChar = normChapTitle[normTarget.Length];
                                    prefixMatch = !char.IsDigit(nextChar);
                                }
                                else if (normTarget.StartsWith(normChapTitle, StringComparison.Ordinal))
                                {
                                    char nextChar = normTarget[normChapTitle.Length];
                                    prefixMatch = !char.IsDigit(nextChar);
                                }
                            }

                            if (exact || prefixMatch)
                            {
                                matchedChapterIdx = i;
                                break;
                            }
                        }
                    }

                    string encodedDisplay = WebUtility.HtmlEncode(cleanDisplay);
                    if (matchedChapterIdx >= 0)
                    {
                        string targetFile = !string.IsNullOrWhiteSpace(chapters[matchedChapterIdx].CustomFileName)
                            ? chapters[matchedChapterIdx].CustomFileName!
                            : $"ch{matchedChapterIdx + 1:D3}.xhtml";

                        listItemsSb.AppendLine($"    <li style=\"margin: 0.35em 0;\"><a href=\"{targetFile}\">{encodedDisplay}</a></li>");
                    }
                    else
                    {
                        listItemsSb.AppendLine($"    <li style=\"margin: 0.35em 0;\">{encodedDisplay}</li>");
                    }
                }

                listItemsSb.AppendLine("  </ol>");
                listItemsSb.AppendLine("</nav>");

                string withoutOldEntries = tocEntryRegex.Replace(chapter.HtmlBodyContent, string.Empty).Trim();
                chapters[cIdx] = new EpubChapterDraft(chapter.Title, $"{withoutOldEntries}\n{listItemsSb}".Trim(), "toc_page.xhtml");
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
