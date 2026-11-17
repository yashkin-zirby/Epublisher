using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Epublisher.ViewModels;

namespace Epublisher.Views;

public partial class HomeView : ContentPage
{
    public HomeView()
    {
        InitializeComponent();
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is HomeViewModel vm)
        {
            vm.RefreshList();
        }
    }
    private void SettingButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.Navigation != null)
        {
            Navigation.PushAsync(new SettingsView()
            {
                DataContext = new SettingsViewModel()
            });
        }
    }
}