using Epublisher.DataEntities;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using VersOne.Epub;

namespace Epublisher.BookLoaders
{
    internal class EpubBookLoader : IBookLoader
    {
        public bool CanHandle(string extension) =>
        extension.Equals(".epub", StringComparison.OrdinalIgnoreCase);

        public async Task<RecentFile> LoadToUnpackedEpubAsync(
            Stream sourceStream, string originalFileName, string targetBookFolder, string bookId)
        {
            using (var archive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                archive.ExtractToDirectory(targetBookFolder, overwriteFiles: true);
            }
            sourceStream.Position = 0;
            EpubBookRef bookRef = await EpubReader.OpenBookAsync(sourceStream);

            string? coverPath = null;
            byte[]? coverBytes = await bookRef.ReadCoverAsync();
            if (coverBytes != null && coverBytes.Length > 0)
            {
                coverPath = Path.Combine(targetBookFolder, "cover_thumb.jpg");
                await File.WriteAllBytesAsync(coverPath, coverBytes);
            }

            return new RecentFile
            {
                Id = bookId,
                InternalEpubPath = targetBookFolder,
                Title = !string.IsNullOrWhiteSpace(bookRef.Title)
                    ? bookRef.Title
                    : Path.GetFileNameWithoutExtension(originalFileName),
                Author = bookRef.AuthorList.Count > 0
                    ? string.Join(", ", bookRef.AuthorList)
                    : "Unknown Author",
                CoverThumbnailPath = coverPath,
                SourceType = BookSourceType.OpenedEpub,
                OriginalFormat = ".epub"
            };
        }
    }
}
