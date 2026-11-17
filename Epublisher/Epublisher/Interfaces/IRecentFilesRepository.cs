using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Interfaces
{
    public interface IRecentFilesRepository
    {
        Task<List<RecentFile>> GetAllAsync();
        Task SaveAllAsync(IEnumerable<RecentFile> files);
        Task UpsertAsync(RecentFile file);
        Task DeleteAsync(string id);
    }
}
