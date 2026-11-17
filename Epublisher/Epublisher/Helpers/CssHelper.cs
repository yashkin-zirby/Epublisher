using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.Helpers
{
    internal static class CssHelper
    {
        public static string ColorToCssHex(Color c)
        {
            return c.A == 255
                ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        }
        public const string FullpageImageCss = """
    .fullpage-image {
        width: 100%;
        text-align: center;
        text-indent: 0;
        page-break-before: always;
        break-before: page;
        page-break-after: always;
        break-after: page;
        page-break-inside: avoid;
        break-inside: avoid;
        margin: 0;
        padding: 0;
    }
    .fullpage-image img {
        max-width: 100%;
        max-height: 95vh;
        width: auto;
        height: auto;
        object-fit: contain;
        display: inline-block;
        margin: 0 auto;
    }
    """;
        public const string BottomTextCss = """
    .page-container {
        min-height: 90vh;
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        page-break-inside: avoid;
        break-inside: avoid;
    }
    .bottom-text {
        margin-top: auto;
        text-align: center;
        text-indent: 0;
        padding-top: 2em;
    }
    """;

        public const string InlineImageCss = """
        .pdf-image {
            text-align: center;
            margin: 1em 0;
        }
        .pdf-image img {
            max-width: 100%;
            height: auto;
            display: inline-block;
        }
        """;

        public const string SceneDividerCss = """
        .scene-break {
            text-align: center;
            text-indent: 0;
            margin: 1.8em 0;
            letter-spacing: 0.4em;
        }
        """;

        public const string BlockquoteCss = """
        blockquote {
            margin: 1em 2em;
            padding-left: 1em;
            border-left: 3px solid #888888;
            font-style: italic;
        }
        """;

        public const string DropCapCss = """
        p.drop-cap::first-letter {
            float: left;
            font-size: 3em;
            line-height: 0.85;
            padding-right: 0.12em;
            font-weight: bold;
        }
        """;

        public const string PageBreakCss = """
        .epub-page-break {
            display: block;
            width: 100%;
            height: 1px;
            margin: 48px 0;
            break-after: page;
            page-break-after: always;
        }
        """;

        /// <summary>
        /// Вставляет выбранный CSS-фрагмент в текущую позицию курсора.
        /// </summary>
        public static TextFormatResult InsertSnippet(string currentCss, int cursorPos, string cssSnippet)
        {
            currentCss ??= string.Empty;
            int pos = Math.Clamp(cursorPos, 0, currentCss.Length);
            string insertion = $"\n{cssSnippet.Trim()}\n";

            string updated = currentCss.Insert(pos, insertion);
            int newCursor = pos + insertion.Length;

            return new TextFormatResult(updated, newCursor, newCursor);
        }
        public static string EnsureBottomTextStyleExists(string? css)
        {
            var sb = new StringBuilder(css ?? string.Empty);

            if (!sb.ToString().Contains(".page-container", StringComparison.OrdinalIgnoreCase) ||
                !sb.ToString().Contains(".bottom-text", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine(BottomTextCss);
            }

            return sb.ToString().Trim();
        }
        public static string EnsureDefaultEditorStylesExist(string? css)
        {
            var sb = new StringBuilder(css ?? string.Empty);
            var defaultStyles = """
            body { font-family: serif; line-height: 1.6; margin: 1em; }
            h1, h2, h3 { text-align: center; margin-top: 0.5em; margin-bottom: 0.4em; }
            p { text-indent: 1.5em; margin: 0.4em 0; text-align: justify; }
            img { max-width: 100%; height: auto; display: block; margin: 1em auto; }
            """;
            if (!sb.ToString().Contains(".pdf-image", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine(InlineImageCss);
            }

            if (!sb.ToString().Contains(".fullpage-image", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine(FullpageImageCss);
            }

            if (!sb.ToString().Contains(".epub-page-break", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine(PageBreakCss);
            }

            return sb.ToString().Trim();
        }
    }
}
