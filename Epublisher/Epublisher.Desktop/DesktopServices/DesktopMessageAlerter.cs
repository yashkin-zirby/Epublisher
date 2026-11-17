using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Epublisher.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.DesktopServices
{
    public class DesktopMessageAlerter : IMessageAlerter
    {
        private static async Task<bool> ShowWindowAsync(string title, string message, bool isQuestion)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                bool result = false;

                var dialog = new Window
                {
                    Title = title,
                    Width = 380,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                var textBlock = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Margin = new Thickness(20, 0, 20, 15)
                };

                if (isQuestion)
                {
                    var yesBtn = new Button { Content = "Yes", Width = 75 };
                    var noBtn = new Button { Content = "No", Width = 75 };

                    yesBtn.Click += (_, _) => { result = true; dialog.Close(); };
                    noBtn.Click += (_, _) => { result = false; dialog.Close(); };

                    buttonPanel.Children.Add(yesBtn);
                    buttonPanel.Children.Add(noBtn);
                }
                else
                {
                    var okBtn = new Button { Content = "OK", Width = 75 };
                    okBtn.Click += (_, _) => { result = true; dialog.Close(); };
                    buttonPanel.Children.Add(okBtn);
                }

                var layout = new DockPanel();
                DockPanel.SetDock(buttonPanel, Dock.Bottom);
                layout.Children.Add(buttonPanel);
                layout.Children.Add(textBlock);
                dialog.Content = layout;

                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner != null)
                    await dialog.ShowDialog(owner);
                else
                    dialog.Show();

                return result;
            });
        }

        public Task DisplayMessageAsync(string message) =>
            ShowWindowAsync("Information", message, isQuestion: false);

        public Task DisplayWarningAsync(string message) =>
            ShowWindowAsync("Warning", message, isQuestion: false);

        public Task DisplayErrorAsync(string message) =>
            ShowWindowAsync("Error", message, isQuestion: false);

        public Task<bool> DisplayQuestionAsync(string question) =>
            ShowWindowAsync("Confirm", question, isQuestion: true);
    }
}
