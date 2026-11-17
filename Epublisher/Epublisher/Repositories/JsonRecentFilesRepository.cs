using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Epublisher.Repositories
{
    public class JsonRecentFilesRepository : IRecentFilesRepository
    {
        private readonly string _storageFilePath;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public JsonRecentFilesRepository(string appDataFolder, string fileName = "recent_files.json")
        {
            _storageFilePath = Path.Combine(appDataFolder, fileName);
        }

        public async Task<List<RecentFile>> GetAllAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!File.Exists(_storageFilePath))
                    return new List<RecentFile>();

                string json = await File.ReadAllTextAsync(_storageFilePath);
                return JsonSerializer.Deserialize<List<RecentFile>>(json, _jsonOptions) ?? new List<RecentFile>();
            }
            catch(Exception e)
            {
                ExceptionProcesser.Process(e);
                return new List<RecentFile>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SaveAllAsync(IEnumerable<RecentFile> files)
        {
            await _semaphore.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(files, _jsonOptions);
                await File.WriteAllTextAsync(_storageFilePath, json);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpsertAsync(RecentFile file)
        {
            var items = await GetAllAsync();
            int index = items.FindIndex(x => x.Id == file.Id);

            if (index >= 0)
                items[index] = file;
            else
                items.Add(file);

            await SaveAllAsync(items);
        }

        public async Task DeleteAsync(string id)
        {
            var items = await GetAllAsync();
            if (items.RemoveAll(x => x.Id == id) > 0)
            {
                await SaveAllAsync(items);
            }
        }
    }
}
