using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using System;

namespace Epublisher.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }
        public override void OnCreate()
        {
            base.OnCreate();

            AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
            {
                Services.LogManager.Instance.Log(args.Exception);
            };
        }
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            string androidPath = global::Android.App.Application.Context.FilesDir?.AbsolutePath
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            App.InitServices(androidPath, new NativeServices.AndroidMessageAlerter());
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
