using Epublisher.DataEntities;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class RecentFilesManager
    {
        private readonly IRecentFilesRepository _repository;
        private readonly int _maxItems;
        private List<RecentFile> _items = new();

        public IReadOnlyList<RecentFile> Items => _items;

        public RecentFilesManager(IRecentFilesRepository repository, int maxItems = 30)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _maxItems = maxItems;
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
        }

        public async Task AddOrUpdateAsync(RecentFile file)
        {
            file.LastAccessedUtc = DateTime.UtcNow;

            _items.RemoveAll(x => x.Id == file.Id ||
                (!string.IsNullOrEmpty(file.InternalEpubPath) && x.InternalEpubPath == file.InternalEpubPath));

            _items.Insert(0, file);

            var pinned = _items.Where(x => x.IsPinned).ToList();
            var unpinned = _items.Where(x => !x.IsPinned).Take(Math.Max(0, _maxItems - pinned.Count));

            _items = pinned.Concat(unpinned)
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.LastAccessedUtc)
                .ToList();

            await _repository.SaveAllAsync(_items);
        }

        public async Task RemoveAsync(string id, bool deleteInternalCopy = false)
        {
            var target = _items.FirstOrDefault(x => x.Id == id);
            if (target == null) return;

            if (deleteInternalCopy)
            {
                if (Directory.Exists(target.InternalEpubPath))
                    Directory.Delete(target.InternalEpubPath, true);

                if (!string.IsNullOrEmpty(target.CoverThumbnailPath) && File.Exists(target.CoverThumbnailPath))
                    File.Delete(target.CoverThumbnailPath);
            }

            _items.Remove(target);
            await _repository.DeleteAsync(id);
        }
    }
}
