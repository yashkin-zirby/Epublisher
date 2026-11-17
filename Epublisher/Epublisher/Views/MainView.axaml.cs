
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.Services;
using Epublisher.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Epublisher.Views;

public partial class MainView : DrawerPage
{
    private MainViewModel? _subscribedVm;
    public MainView()
    {
        InitializeComponent();
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedVm != null)
        {
            UnsubscribeFromHomeEvents(_subscribedVm);
            _subscribedVm = null;
        }

        if (DataContext is MainViewModel vm)
        {
            _subscribedVm = vm;
            RootHomeView.DataContext = vm.Home;
            SubscribeToHomeEvents(vm);
        }
    }

    private void SubscribeToHomeEvents(MainViewModel vm)
    {
        vm.Home.OpenBookForReadRequested += OnOpenBookForReadRequested;
        vm.Home.OpenBookForEditRequested += OnOpenBookForEditRequested;
        vm.Home.ImportNewBookRequested += ImportAndOpenBookAsync;
        vm.Home.ExportBookRequested += ExportBookToFileAsync;
    }

    private void UnsubscribeFromHomeEvents(MainViewModel vm)
    {
        vm.Home.OpenBookForReadRequested -= OnOpenBookForReadRequested;
        vm.Home.OpenBookForEditRequested -= OnOpenBookForEditRequested;
        vm.Home.ImportNewBookRequested -= ImportAndOpenBookAsync;
        vm.Home.ExportBookRequested -= ExportBookToFileAsync;
    }

    private async void OnOpenBookForReadRequested(RecentFile book) => await OpenReaderAsync(book);
    private async void OnOpenBookForEditRequested(RecentFile book) => await OpenEditorAsync(book);

    // --- Navigation Helpers ---

    private async Task OpenReaderAsync(RecentFile book)
    {
        CloseMenu();

        var readerVm = new ReaderViewModel(book);
        readerVm.EditBookRequested += async b =>
        {
            await InnerNavigationPage.PopAsync();
            await OpenEditorAsync(b);
        };

        await InnerNavigationPage.PushAsync(new ReaderView
        {
            DataContext = readerVm
        });
    }

    private async Task OpenEditorAsync(RecentFile book)
    {
        CloseMenu();
        var editorVm = new CreateEpubViewModel(book);
        editorVm.ExportBookRequested += ExportBookToFileAsync;

        await InnerNavigationPage.PushAsync(new CreateEpubView
        {
            DataContext = editorVm
        });
    }

    // --- File Picker Import & Export ---

    private async Task ImportAndOpenBookAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите книгу",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Книги") { Patterns = new[] { "*.epub", "*.fb2", "*.pdf", "*.txt" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;

        try
        {
            var selectedFile = files.First();
            await using Stream stream = await selectedFile.OpenReadAsync();

            RecentFile importedBook = await App.Services.BookImporter.ImportAsync(
                fileStream: stream,
                fileName: selectedFile.Name,
                externalUri: selectedFile.Path?.ToString()
            );

            _subscribedVm?.Home.RefreshList();
            await OpenReaderAsync(importedBook);
        }
        catch (Exception ex)
        {
            ExceptionProcesser.Process(ex);
        }
    }

    private async Task ExportBookToFileAsync(RecentFile book)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        string safeName = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "book";

        var targetFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспортировать в EPUB",
            SuggestedFileName = $"{safeName}.epub",
            DefaultExtension = "epub",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("EPUB книга") { Patterns = new[] { "*.epub" } }
            }
        });

        if (targetFile == null) return;

        try
        {
            
            await App.Services.BookExporter.ExportToStorageFileAsync(book, targetFile);
            _subscribedVm?.Home.RefreshList();

            if (App.Services.MessageAlerter != null)
            {
                await App.Services.MessageAlerter.DisplayMessageAsync("Книга успешно сохранена в EPUB.");
            }
        }
        catch (Exception ex)
        {
            ExceptionProcesser.Process(ex);
        }
    }

    // --- Side Menu Button Handlers ---

    private void CloseMenu()
    {
        if (_subscribedVm != null)
            _subscribedVm.IsMenuOpen = false;
    }

    private async void OnNavigateHomeClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        _subscribedVm?.Home.RefreshList();
        await InnerNavigationPage.PopToRootAsync();
    }

    private async void OnOpenEpubClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        await ImportAndOpenBookAsync();
    }

    private async void OnNavigateCreateEpubClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        await InnerNavigationPage.PopToRootAsync();
        await InnerNavigationPage.PushAsync(new CreateEpubView
        {
            DataContext = new CreateEpubViewModel()
        });
    }

    private async void OnNavigateSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        await InnerNavigationPage.PopToRootAsync();
        await InnerNavigationPage.PushAsync(new SettingsView
        {
            DataContext = new SettingsViewModel()
        });
    }
}