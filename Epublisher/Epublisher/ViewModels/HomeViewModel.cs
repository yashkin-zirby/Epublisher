using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epublisher.DataEntities;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Epublisher.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<RecentFile> _recentBooks = new();
    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    public event Action<RecentFile>? OpenBookForReadRequested;
    public event Action<RecentFile>? OpenBookForEditRequested;
    public event Func<RecentFile, Task>? ExportBookRequested;
    public event Func<Task>? ImportNewBookRequested;

    public HomeViewModel()
    {
        RefreshList();
    }

    partial void OnSearchQueryChanged(string value) => RefreshList();

    public void RefreshList()
    {
        var all = App.Services.RecentFiles.Items;

        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? all
            : all.Where(b =>
                b.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                b.Author.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        RecentBooks = new ObservableCollection<RecentFile>(filtered);
        IsEmpty = RecentBooks.Count == 0;
    }

    [RelayCommand]
    private async Task ImportNewBookAsync()
    {
        if (ImportNewBookRequested == null || IsImporting) return;

        try
        {
            IsImporting = true;
            await ImportNewBookRequested.Invoke();
            RefreshList();
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private void OpenForRead(RecentFile book) => OpenBookForReadRequested?.Invoke(book);

    [RelayCommand]
    private void OpenForEdit(RecentFile book) => OpenBookForEditRequested?.Invoke(book);

    [RelayCommand]
    private async Task ExportBookAsync(RecentFile book)
    {
        if (ExportBookRequested != null)
            await ExportBookRequested.Invoke(book);
    }

    [RelayCommand]
    private async Task TogglePinAsync(RecentFile book)
    {
        book.IsPinned = !book.IsPinned;
        await App.Services.RecentFiles.AddOrUpdateAsync(book);
        RefreshList();
    }

    [RelayCommand]
    private async Task DeleteBookAsync(RecentFile book)
    {
        bool confirm = true;
        if (App.Services.MessageAlerter != null)
        {
            confirm = await App.Services.MessageAlerter.DisplayQuestionAsync(
                $"Удалить книгу «{book.Title}» из библиотеки?");
        }
        if (!confirm) return;

        await App.Services.RecentFiles.RemoveAsync(book.Id, deleteInternalCopy: true);
        RefreshList();
    }
}
