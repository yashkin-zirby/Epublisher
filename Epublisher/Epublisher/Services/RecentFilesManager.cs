using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class RecentFilesManager
    {
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly IRecentFilesRepository _repository;
        private List<RecentFile> _items = new();

        public IReadOnlyList<RecentFile> Items => _items;

        public RecentFilesManager(IRecentFilesRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task InitializeAsync()
        {
            var loaded = await _repository.GetAllAsync();

            _items = loaded
                .Where(x => !string.IsNullOrEmpty(x.InternalEpubPath) && Directory.Exists(x.InternalEpubPath))
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.LastAccessedUtc)
                .ToList();

            if (_items.Count != loaded.Count)
            {
                await _repository.SaveAllAsync(_items);
            }
            await CleanupOrphanedBookFoldersAsync();
        }

        public async Task AddOrUpdateAsync(RecentFile file)
        {
            file.LastAccessedUtc = DateTime.UtcNow;

            _items.RemoveAll(x => x.Id == file.Id ||
                (!string.IsNullOrEmpty(file.InternalEpubPath) && x.InternalEpubPath == file.InternalEpubPath));

            _items.Insert(0, file);

            await _repository.SaveAllAsync(_items);
        }

        public async Task RemoveAsync(string id, bool deleteInternalCopy = true)
        {
            var target = _items.FirstOrDefault(x => x.Id == id);
            if (target == null) return;
            await _fileLock.WaitAsync();
            try
            {
                _items.Remove(target);
                await _repository.DeleteAsync(id);
                if (deleteInternalCopy)
                {

                    DeleteBookDirectorySafe(target.InternalEpubPath);

                    if (!string.IsNullOrEmpty(target.CoverThumbnailPath) && File.Exists(target.CoverThumbnailPath))
                        File.Delete(target.CoverThumbnailPath);
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }
        private static void DeleteBookDirectorySafe(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            try
            {
                Directory.Delete(folderPath, recursive: true);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }
        public async Task CleanupOrphanedBookFoldersAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                string booksRoot = Path.Combine(App.Services.Settings.AppDataFolder, "books");
                if (!Directory.Exists(booksRoot))
                    return;

                var validFolders = _items
                    .Where(b => !string.IsNullOrWhiteSpace(b.InternalEpubPath))
                    .Select(b => Path.GetFullPath(b.InternalEpubPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var validIds = _items
                    .Where(b => !string.IsNullOrWhiteSpace(b.Id))
                    .Select(b => b.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string dir in Directory.GetDirectories(booksRoot))
                {
                    string fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string dirName = Path.GetFileName(fullDir);

                    if (!validFolders.Contains(fullDir) && !validIds.Contains(dirName))
                    {
                        DeleteBookDirectorySafe(fullDir);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}
