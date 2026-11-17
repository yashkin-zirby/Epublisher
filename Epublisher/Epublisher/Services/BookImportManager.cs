using Avalonia.Platform.Storage;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class BookImportManager
    {
        private readonly string _booksRootFolder;
        private readonly List<IBookLoader> _loaders;
        private readonly SinglePageEpubAssembler _assembler;

        public BookImportManager(string appDataFolder, IEnumerable<IBookLoader> loaders)
        {
            _booksRootFolder = Path.Combine(appDataFolder, "books");
            Directory.CreateDirectory(_booksRootFolder);
            _loaders = loaders.ToList();
            _assembler = new SinglePageEpubAssembler();
        }
        public async Task<RecentFile> ImportFromStorageFileAsync(IStorageFile storageFile)
        {
            await using Stream stream = await storageFile.OpenReadAsync();
            return await ImportAsync(
                fileStream: stream,
                fileName: storageFile.Name,
                externalUri: storageFile.Path?.ToString()
            );
        }
        public async Task<RecentFile> ImportAsync(Stream fileStream, string fileName, string? externalUri = null)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            var loader = _loaders.FirstOrDefault(l => l.CanHandle(ext))
                ?? throw new NotSupportedException($"Формат файла {ext} не поддерживается.");

            string bookId = Guid.NewGuid().ToString("N");
            string bookFolder = Path.Combine(_booksRootFolder, bookId);
            Directory.CreateDirectory(bookFolder);
            Stream seekableStream = fileStream;
            MemoryStream? buffer = null;
            if (!fileStream.CanSeek)
            {
                buffer = new MemoryStream();
                await fileStream.CopyToAsync(buffer);
                buffer.Position = 0;
                seekableStream = buffer;
            }
            try
            {
                RecentFile recentFile = await loader.LoadToUnpackedEpubAsync(
                    seekableStream, fileName, bookFolder, bookId);

                recentFile.InternalEpubPath = bookFolder;
                recentFile.ExternalUriOrPath = externalUri;
                recentFile.OriginalFormat = ext;

                await _assembler.BuildSinglePageHtmlAsync(bookFolder);
                await App.Services.RecentFiles.AddOrUpdateAsync(recentFile);

                return recentFile;
            }
            finally
            {
                if (buffer != null)
                    await buffer.DisposeAsync();
            }
        }
    }
}
