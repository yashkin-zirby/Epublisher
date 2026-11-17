using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.BookLoaders
{
    public class TxtBookLoader : IBookLoader
    {
        public bool CanHandle(string extension) =>
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

        public async Task<RecentFile> LoadToUnpackedEpubAsync(
            Stream sourceStream, string originalFileName, string targetBookFolder, string bookId)
        {
            using var reader = new StreamReader(sourceStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            string fullText = await reader.ReadToEndAsync();

            string title = Path.GetFileNameWithoutExtension(originalFileName);
            string[] paragraphs = fullText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var chapters = new List<EpubChapterDraft>();
            var currentSb = new StringBuilder();
            int chapterIndex = 1;
            const int maxCharsPerChapter = 15000;

            foreach (string rawLine in paragraphs)
            {
                string trimmed = rawLine.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                currentSb.AppendLine($"<p>{WebUtility.HtmlEncode(trimmed)}</p>");

                if (currentSb.Length >= maxCharsPerChapter)
                {
                    chapters.Add(new EpubChapterDraft($"Часть {chapterIndex++}", currentSb.ToString()));
                    currentSb.Clear();
                }
            }

            if (currentSb.Length > 0)
            {
                chapters.Add(new EpubChapterDraft($"Часть {chapterIndex}", currentSb.ToString()));
            }

            await EpubStructureWriter.WriteUnpackedEpubAsync(
                targetBookFolder, bookId, title, "Unknown Author", "ru", chapters);

            return new RecentFile
            {
                Id = bookId,
                InternalEpubPath = targetBookFolder,
                Title = title,
                Author = "Unknown Author",
                SourceType = BookSourceType.ImportedConverted,
                OriginalFormat = ".txt"
            };
        }
    }
}
