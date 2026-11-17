using Avalonia.Platform.Storage;
using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class EpubExportService
    {
        private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase){ "reader_full.html", "cover_thumb.jpg" };

        /// <summary>
        /// Упаковывает книгу
        /// </summary>
        public async Task ExportToStorageFileAsync(RecentFile book, IStorageFile targetFile)
        {
            await using var memoryStream = new MemoryStream();
            await ExportToStreamAsync(book.InternalEpubPath, memoryStream);

            memoryStream.Position = 0;

            await using Stream outputStream = await targetFile.OpenWriteAsync();
            outputStream.SetLength(0);
            await memoryStream.CopyToAsync(outputStream);
            await outputStream.FlushAsync();

            book.HasUnsavedChanges = false;
            book.ExternalUriOrPath = targetFile.Path?.ToString();
            await App.Services.RecentFiles.AddOrUpdateAsync(book);
        }

        /// <summary>
        /// Упаковывает разархивированную папку книги в валидный EPUB-поток
        /// </summary>
        public async Task ExportToStreamAsync(string unpackedBookFolder, Stream destinationStream)
        {
            if (!Directory.Exists(unpackedBookFolder))
                throw new DirectoryNotFoundException($"Папка книги не найдена: {unpackedBookFolder}");

            await Task.Run(() =>
            {
                using var archive = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);

                var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using (var entryStream = mimeEntry.Open())
                {
                    byte[] mimeBytes = Encoding.ASCII.GetBytes("application/epub+zip");
                    entryStream.Write(mimeBytes, 0, mimeBytes.Length);
                }

                var allFiles = Directory.GetFiles(unpackedBookFolder, "*", SearchOption.AllDirectories);

                foreach (string fullFilePath in allFiles)
                {
                    string fileName = Path.GetFileName(fullFilePath);

                    if (fileName.Equals("mimetype", StringComparison.OrdinalIgnoreCase) ||
                        ExcludedFileNames.Contains(fileName))
                    {
                        continue;
                    }

                    string relativePath = Path.GetRelativePath(unpackedBookFolder, fullFilePath)
                        .Replace('\\', '/');

                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    using var fileStream = File.OpenRead(fullFilePath);
                    using var entryStream = entry.Open();
                    fileStream.CopyTo(entryStream);
                }
            });
        }
    }
}
