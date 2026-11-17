using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Epublisher.Helpers
{
    internal readonly record struct TextFormatResult(string UpdatedText, int NewSelectionStart, int NewSelectionEnd);

    internal static class EpubTextFormatingHelper
    {
        public static TextFormatResult ApplyBold(string text, int selStart, int selEnd) =>
            ToggleWrapSelection(text, selStart, selEnd, "<strong>", "</strong>");

        public static TextFormatResult ApplyItalic(string text, int selStart, int selEnd) =>
            ToggleWrapSelection(text, selStart, selEnd, "<em>", "</em>");

        public static TextFormatResult ApplyUnderline(string text, int selStart, int selEnd) =>
            ToggleWrapSelection(text, selStart, selEnd, "<u>", "</u>");

        public static TextFormatResult ApplyStrikethrough(string text, int selStart, int selEnd) =>
            ToggleWrapSelection(text, selStart, selEnd, "<del>", "</del>");

        public static TextFormatResult ApplyHeading(string text, int selStart, int selEnd, int level = 2)
        {
            int clampedLevel = Math.Clamp(level, 1, 3);
            return ToggleWrapSelection(text, selStart, selEnd, $"<h{clampedLevel}>", $"</h{clampedLevel}>");
        }

        public static TextFormatResult ApplyParagraph(string text, int selStart, int selEnd) =>
            ToggleWrapSelection(text, selStart, selEnd, "<p>", "</p>");

        public static TextFormatResult ApplyAlignment(string text, int selStart, int selEnd, string align)
        {
            string safeAlign = align.ToLowerInvariant() switch
            {
                "center" => "center",
                "right" => "right",
                "justify" => "justify",
                _ => "left"
            };

            string openTag = $"<p style=\"text-align: {safeAlign}; text-indent: 0;\">";
            const string closeTag = "</p>";

            return ToggleWrapSelection(text, selStart, selEnd, openTag, closeTag);
        }
        public static TextFormatResult ApplyBottomText(string text, int selStart, int selEnd)
        {
            text ??= string.Empty;
            int start = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
            int end = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

            string bottomContent;
            string workingText = text;

            if (start < end)
            {
                bottomContent = text.Substring(start, end - start).Trim();
                workingText = text.Remove(start, end - start);
                workingText = Regex.Replace(workingText, @"<p(?:\s[^>]*)?>\s*</p>", string.Empty, RegexOptions.IgnoreCase).Trim();
            }
            else
            {
                bottomContent = "Текст внизу страницы";
            }

            string bottomTag = $"<p class=\"bottom-text\">{bottomContent}</p>";

            int containerOpenIdx = workingText.IndexOf("<div class=\"page-container\"", StringComparison.OrdinalIgnoreCase);
            if (containerOpenIdx >= 0)
            {
                int lastCloseDiv = workingText.LastIndexOf("</div>", StringComparison.OrdinalIgnoreCase);
                if (lastCloseDiv > containerOpenIdx)
                {
                    string insertion = $"\n  {bottomTag}\n";
                    string updated = workingText.Insert(lastCloseDiv, insertion);
                    int newStart = lastCloseDiv + 3 + "<p class=\"bottom-text\">".Length;
                    int newEnd = newStart + bottomContent.Length;
                    return new TextFormatResult(updated, newStart, newEnd);
                }
            }

            string wrapped = string.IsNullOrWhiteSpace(workingText)
                ? $"<div class=\"page-container\">\n  {bottomTag}\n</div>"
                : $"<div class=\"page-container\">\n  {workingText}\n  {bottomTag}\n</div>";

            int selectStart = wrapped.LastIndexOf(bottomContent, StringComparison.Ordinal);
            int selectEnd = selectStart >= 0 ? selectStart + bottomContent.Length : wrapped.Length;

            return new TextFormatResult(wrapped, Math.Max(0, selectStart), selectEnd);
        }

        /// <summary>
        /// Удаляет все HTML-теги форматирования ТОЛЬКО внутри выделенного диапазона текста.
        /// Если текст не выделен — ничего не делает.
        /// </summary>
        public static TextFormatResult ClearFormatting(string text, int selStart, int selEnd)
        {
            text ??= string.Empty;
            int start = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
            int end = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

            // Если текст не выделен — ничего не меняем
            if (start == end)
                return new TextFormatResult(text, start, end);

            string selected = text.Substring(start, end - start);

            // Также проверяем, не находятся ли теги прямо снаружи выделенного текста
            // (например, если пользователь выделил текст внутри <strong>выделение</strong>)
            int outerStart = start;
            int outerEnd = end;

            var beforeSpan = text.AsSpan(0, start);
            var afterSpan = text.AsSpan(end);

            var openTagMatch = Regex.Match(beforeSpan.ToString(), @"<([a-zA-Z0-9]+)(?:\s[^>]*)?>$");
            var closeTagMatch = Regex.Match(afterSpan.ToString(), @"^</([a-zA-Z0-9]+)>");

            if (openTagMatch.Success && closeTagMatch.Success &&
                openTagMatch.Groups[1].Value.Equals(closeTagMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            {
                outerStart -= openTagMatch.Length;
                outerEnd += closeTagMatch.Length;
            }

            string targetSlice = text.Substring(outerStart, outerEnd - outerStart);
            string plainText = Regex.Replace(targetSlice, "<[^>]+>", string.Empty);

            string updated = text.Remove(outerStart, outerEnd - outerStart).Insert(outerStart, plainText);
            return new TextFormatResult(updated, outerStart, outerStart + plainText.Length);
        }

        public static TextFormatResult InsertPageBreak(string text, int cursorPos)
        {
            const string pageBreakTag = "\n<div style=\"break-after: page; page-break-after: always;\"></div>\n";
            return InsertAtCursor(text, cursorPos, pageBreakTag);
        }

        public static TextFormatResult InsertImageTag(string text, int cursorPos, string relativeImagePath, bool fullPage = false)
        {
            string imgHtml = fullPage
                ? $"\n<div class=\"fullpage-image\"><img src=\"{relativeImagePath}\" alt=\"\"/></div>\n"
                : $"\n<div class=\"pdf-image\"><img src=\"{relativeImagePath}\" alt=\"\"/></div>\n";

            return InsertAtCursor(text, cursorPos, imgHtml);
        }

        private static TextFormatResult ToggleWrapSelection(
            string text, int selStart, int selEnd, string openTag, string closeTag)
        {
            text ??= string.Empty;
            int start = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
            int end = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

            // Если текст НЕ выделен — ничего не вставляем и не меняем
            if (start == end)
                return new TextFormatResult(text, start, end);

            // 1. Если теги уже стоят прямо СНАРУЖИ выделенного текста -> снимаем их
            if (start >= openTag.Length &&
                end + closeTag.Length <= text.Length &&
                text.AsSpan(start - openTag.Length, openTag.Length).Equals(openTag, StringComparison.OrdinalIgnoreCase) &&
                text.AsSpan(end, closeTag.Length).Equals(closeTag, StringComparison.OrdinalIgnoreCase))
            {
                string unwrapped = text
                    .Remove(end, closeTag.Length)
                    .Remove(start - openTag.Length, openTag.Length);

                int newStart = start - openTag.Length;
                int newEnd = end - openTag.Length;
                return new TextFormatResult(unwrapped, newStart, newEnd);
            }

            string selected = text.Substring(start, end - start);

            // 2. Если пользователь выделил текст ВМЕСТЕ с тегами -> убираем теги
            if (selected.StartsWith(openTag, StringComparison.OrdinalIgnoreCase) &&
                selected.EndsWith(closeTag, StringComparison.OrdinalIgnoreCase) &&
                selected.Length >= openTag.Length + closeTag.Length)
            {
                string innerText = selected.Substring(openTag.Length, selected.Length - openTag.Length - closeTag.Length);
                string updated = text.Remove(start, end - start).Insert(start, innerText);
                return new TextFormatResult(updated, start, start + innerText.Length);
            }

            // 3. Обрамляем выделенный текст тегами
            string replacement = $"{openTag}{selected}{closeTag}";
            string wrapped = text.Remove(start, end - start).Insert(start, replacement);
            int wrappedStart = start + openTag.Length;
            int wrappedEnd = wrappedStart + selected.Length;

            return new TextFormatResult(wrapped, wrappedStart, wrappedEnd);
        }

        private static TextFormatResult InsertAtCursor(string text, int cursorPos, string insertion)
        {
            text ??= string.Empty;
            int pos = Math.Clamp(cursorPos, 0, text.Length);
            string updated = text.Insert(pos, insertion);
            int newCursor = pos + insertion.Length;

            return new TextFormatResult(updated, newCursor, newCursor);
        }
        public static string FormatHtml(string rawHtml)
        {
            if (string.IsNullOrWhiteSpace(rawHtml))
                return string.Empty;

            // 1. Убираем лишние пробелы и переносы между тегами
            string normalized = Regex.Replace(rawHtml.Trim(), @">\s+<", "><");

            // 2. Ставим переносы строк вокруг блочных и структурных тегов
            string blockTags = "div|p|h1|h2|h3|h4|h5|h6|ol|ul|li|section|article|header|footer|main|nav|blockquote|table|tr|td|th|hr";
            normalized = Regex.Replace(normalized, $@"(<(?:{blockTags})\b[^>]*>)", "\n$1\n", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, $@"(</(?:{blockTags})>)", "\n$1\n", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"(<br\s*/?>)", "$1\n", RegexOptions.IgnoreCase);

            string[] lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            int indentLevel = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                bool isClosingTag = Regex.IsMatch(line, $@"^</(?:{blockTags})>", RegexOptions.IgnoreCase);
                bool isOpeningTag = Regex.IsMatch(line, $@"^<(?:{blockTags})\b[^>]*>$", RegexOptions.IgnoreCase) && !line.EndsWith("/>");

                if (isClosingTag)
                {
                    indentLevel = Math.Max(0, indentLevel - 1);
                }

                sb.Append(new string(' ', indentLevel * 2));
                sb.AppendLine(line);

                if (isOpeningTag)
                {
                    indentLevel++;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
