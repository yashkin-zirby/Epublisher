using Android.App;
using Android.Content;
using Avalonia.Threading;
using Epublisher.Android;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.NativeServices
{
    public class AndroidMessageAlerter : IMessageAlerter
    {
        private Task<bool> ShowNativeDialogAsync(string title, string message, bool isQuestion)
        {
            var tcs = new TaskCompletionSource<bool>();

            Dispatcher.UIThread.Post(() =>
            {
                var activity = MainActivity.CurrentActivity;
                if (activity == null || activity.IsFinishing || activity.IsDestroyed)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                var builder = new AlertDialog.Builder(activity)
                    .SetTitle(title)!
                    .SetMessage(message)!
                    .SetCancelable(false)!;

                if (isQuestion)
                {
                    builder.SetPositiveButton("Yes", (_, _) => tcs.TrySetResult(true));
                    builder.SetNegativeButton("No", (_, _) => tcs.TrySetResult(false));
                }
                else
                {
                    builder.SetPositiveButton("OK", (_, _) => tcs.TrySetResult(true));
                }

                builder.Show();
            });

            return tcs.Task;
        }

        public Task DisplayMessageAsync(string message) =>
            ShowNativeDialogAsync("Information", message, isQuestion: false);

        public Task DisplayWarningAsync(string message) =>
            ShowNativeDialogAsync("Warning", message, isQuestion: false);

        public Task DisplayErrorAsync(string message) =>
            ShowNativeDialogAsync("Error", message, isQuestion: false);

        public Task<bool> DisplayQuestionAsync(string question) =>
            ShowNativeDialogAsync("Confirm", question, isQuestion: true);
    }
}
