using Avalonia.Threading;
using Epublisher.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher
{
    public static class CrashHandler
    {
        private static bool _isRegistered;

        public static void Register()
        {
            if (_isRegistered) return;
            _isRegistered = true;

            Dispatcher.UIThread.UnhandledException += (sender, args) =>
            {
                LogManager.Instance.Log(args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LogManager.Instance.Log(ex);
                }
                else
                {
                    LogManager.Instance.Log(new Exception($"Non-CLS fatal crash: {args.ExceptionObject}"));
                }
            };
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LogManager.Instance.Log(args.Exception);
                args.SetObserved(); 
            };
        }
    }
}
