using Epublisher.DataEntities;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Services
{
    public class CustomHtmlRulesManager
    {
        private readonly IHtmlRulesRepository _repository;
        private List<CustomHtmlRule> _rules = new();

        public IReadOnlyList<CustomHtmlRule> Rules => _rules;
        public event Action? RulesChanged;

        public CustomHtmlRulesManager(IHtmlRulesRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task InitializeAsync()
        {
            _rules = await _repository.GetAllAsync();
            RulesChanged?.Invoke();
        }

        public async Task AddOrUpdateAsync(CustomHtmlRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.OpenTag))
                return;

            int idx = _rules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0)
                _rules[idx] = rule;
            else
                _rules.Add(rule);

            await _repository.SaveAllAsync(_rules);
            RulesChanged?.Invoke();
        }

        public async Task DeleteAsync(string id)
        {
            if (_rules.RemoveAll(r => r.Id == id) > 0)
            {
                await _repository.DeleteAsync(id);
                RulesChanged?.Invoke();
            }
        }
    }
}
