using Epublisher.Interfaces;
using Epublisher.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.Helpers
{
    internal static class ExceptionProcesser
    {
        public static async void Process(Exception exception)
        {
            var settings = App.Services.Settings;
            var allowLogDebug = settings.GetProperty<bool>("Logging.AllowDebug",false);
            var allowLogWarning = settings.GetProperty<bool>("Logging.AllowWarning", true);
            var allowLogError = settings.GetProperty<bool>("Logging.AllowError", true);
            var allowLog = settings.GetProperty<bool>("Logging.AllowLog", true);
            var allowMessageBox = settings.GetProperty<bool>("Logging.AllowMessageBox", true);
            var allowMessageBoxOnlyErrors = settings.GetProperty<bool>("Logging.AllowMessageBoxOnlyErrors", true);
            var logLevel = LogManager.Instance.GetExceptionLogLevel(exception);
            if (allowLog)
            {
                if (logLevel == LogLevel.Debug && allowLogDebug) LogManager.Instance.Log(exception);
                if (logLevel == LogLevel.Warning && allowLogWarning) LogManager.Instance.Log(exception);
                if (logLevel == LogLevel.Error && allowLogError) LogManager.Instance.Log(exception);
                if (logLevel == LogLevel.Critical) LogManager.Instance.Log(exception);
            }
            if (allowMessageBox)
            {
                if(logLevel == LogLevel.Error || logLevel == LogLevel.Critical)
                    await App.Services.MessageAlerter.DisplayErrorAsync(exception.Message);
                else if (logLevel == LogLevel.Warning)
                    await App.Services.MessageAlerter.DisplayWarningAsync(exception.Message);
                else if(!allowMessageBoxOnlyErrors)
                    await App.Services.MessageAlerter.DisplayMessageAsync(exception.Message);
            }
        }
    }
}
