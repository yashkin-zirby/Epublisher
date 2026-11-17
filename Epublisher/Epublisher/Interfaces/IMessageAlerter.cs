using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.Interfaces
{
    public interface IMessageAlerter
    {
        Task DisplayMessageAsync(string message);
        Task DisplayWarningAsync(string message);
        Task DisplayErrorAsync(string message);
        Task<bool> DisplayQuestionAsync(string question);
    }
}
