using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Epublisher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isMenuOpen;

    public HomeViewModel Home { get; } = new();

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;
}
