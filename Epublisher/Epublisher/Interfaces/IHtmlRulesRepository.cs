using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Interfaces
{
    public interface IHtmlRulesRepository
    {
        Task<List<CustomHtmlRule>> GetAllAsync();
        Task SaveAllAsync(IEnumerable<CustomHtmlRule> rules);
        Task DeleteAsync(string id);
    }
}
