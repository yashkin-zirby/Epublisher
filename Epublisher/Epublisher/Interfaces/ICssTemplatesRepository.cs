using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Interfaces
{
    public interface ICssTemplatesRepository
    {
        Task<List<CustomCssTemplate>> GetAllAsync();
        Task SaveAllAsync(IEnumerable<CustomCssTemplate> templates);
        Task DeleteAsync(string id);
    }
}
