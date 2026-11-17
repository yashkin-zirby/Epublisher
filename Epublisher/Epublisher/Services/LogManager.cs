using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Epublisher.Services
{
    public class LogManager
    {
        private static LogManager? _instance;
        public static LogManager Instance { get { if (_instance == null) _instance = new LogManager(); return _instance; } }
        private readonly Dictionary<Type, Action<Exception,LogLevel>> _rules = new();
        private readonly Dictionary<Type, LogLevel> _logLevels = new();
        public LogManager AddRule<TEx>(Action<TEx, LogLevel, ILogger> handler, LogLevel logLevel, ILogger logger) where TEx : Exception
        {
            _rules[typeof(TEx)] = (ex,logLevel) => handler((TEx)ex, logLevel, logger);
            _logLevels[typeof(TEx)] = logLevel;
            return this;
        }
        public bool Log(Exception exception)
        {
            Type? currentType = exception.GetType();
            while (currentType != null && typeof(Exception).IsAssignableFrom(currentType))
            {
                LogLevel logLevel;
                if (!_logLevels.TryGetValue(currentType, out logLevel))
                {
                    logLevel = LogLevel.Critical;
                }
                if (_rules.TryGetValue(currentType, out var handler))
                {
                    handler(exception, logLevel);
                    return true;
                }
                currentType = currentType.BaseType;
            }
            return false;
        }

        public LogLevel GetExceptionLogLevel(Exception exception)
        {
            Type? currentType = exception.GetType();
            while (currentType != null && typeof(Exception).IsAssignableFrom(currentType))
            {
                if (_logLevels.TryGetValue(currentType, out var logLevel))
                {
                    return logLevel;
                }

                currentType = currentType.BaseType;
            }
            return LogLevel.Critical;
        }
    }
}
