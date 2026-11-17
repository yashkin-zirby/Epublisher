using Epublisher.DataEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Interfaces
{
    public interface IBookLoader
    {
        bool CanHandle(string extension);

        Task<RecentFile> LoadToUnpackedEpubAsync(Stream sourceStream, string originalFileName, string targetBookFolder, string bookId);
    }
}
