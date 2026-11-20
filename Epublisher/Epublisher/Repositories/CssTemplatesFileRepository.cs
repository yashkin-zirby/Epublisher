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
    public class CssTemplatesFileRepository : ICssTemplatesRepository
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public CssTemplatesFileRepository(string appDataFolder)
        {
            Directory.CreateDirectory(appDataFolder);
            _filePath = Path.Combine(appDataFolder, "custom_css_templates.json");
        }

        public async Task<List<CustomCssTemplate>> GetAllAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                    return new List<CustomCssTemplate>();

                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<CustomCssTemplate>>(json, _jsonOptions) ?? new List<CustomCssTemplate>();
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
                return new List<CustomCssTemplate>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAllAsync(IEnumerable<CustomCssTemplate> templates)
        {
            await _lock.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(templates, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteAsync(string id)
        {
            var all = await GetAllAsync();
            if (all.RemoveAll(t => t.Id == id) > 0)
            {
                await SaveAllAsync(all);
            }
        }
    }
}
