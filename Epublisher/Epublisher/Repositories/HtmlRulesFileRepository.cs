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
    public class HtmlRulesFileRepository : IHtmlRulesRepository
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public HtmlRulesFileRepository(string appDataFolder)
        {
            Directory.CreateDirectory(appDataFolder);
            _filePath = Path.Combine(appDataFolder, "custom_html_rules.json");
        }

        public async Task<List<CustomHtmlRule>> GetAllAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                    return new List<CustomHtmlRule>();

                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<CustomHtmlRule>>(json, _jsonOptions) ?? new List<CustomHtmlRule>();
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
                return new List<CustomHtmlRule>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAllAsync(IEnumerable<CustomHtmlRule> rules)
        {
            await _lock.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(rules, _jsonOptions);
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
            if (all.RemoveAll(r => r.Id == id) > 0)
            {
                await SaveAllAsync(all);
            }
        }
    }
}
