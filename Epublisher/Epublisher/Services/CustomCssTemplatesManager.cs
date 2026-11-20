using Epublisher.DataEntities;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class CustomCssTemplatesManager
    {
        private readonly ICssTemplatesRepository _repository;
        private List<CustomCssTemplate> _templates = new();

        public IReadOnlyList<CustomCssTemplate> Templates => _templates;
        public event Action? TemplatesChanged;

        public CustomCssTemplatesManager(ICssTemplatesRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task InitializeAsync()
        {
            _templates = await _repository.GetAllAsync();
            TemplatesChanged?.Invoke();
        }

        public async Task AddOrUpdateAsync(CustomCssTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.CssCode))
                return;

            int idx = _templates.FindIndex(t => t.Id == template.Id);
            if (idx >= 0)
                _templates[idx] = template;
            else
                _templates.Add(template);

            await _repository.SaveAllAsync(_templates);
            TemplatesChanged?.Invoke();
        }

        public async Task DeleteAsync(string id)
        {
            if (_templates.RemoveAll(t => t.Id == id) > 0)
            {
                await _repository.DeleteAsync(id);
                TemplatesChanged?.Invoke();
            }
        }
    }
}
