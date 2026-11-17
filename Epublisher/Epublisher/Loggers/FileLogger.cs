using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Epublisher.Loggers
{
    internal class FileLogger : ILogger
    {
        private readonly string _file;
        private readonly object _lock = new();

        public FileLogger(string file)
        {
            _file = file;
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            string exText = exception != null ? $"{Environment.NewLine}{exception}" : string.Empty;
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {message}{exText}{Environment.NewLine}";

            lock (_lock)
            {
                File.AppendAllText(_file, line);
            }
        }
    }
}
