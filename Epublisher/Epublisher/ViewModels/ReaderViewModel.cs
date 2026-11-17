using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Epublisher.ViewModels
{
    internal partial class ReaderViewModel : ViewModelBase
    {
        public RecentFile Book { get; }

        [ObservableProperty]
        private string _bookTitle;

        [ObservableProperty]
        private Uri? _readerSourceUri;

        [ObservableProperty]
        private double _readingProgress;

        [ObservableProperty]
        private string _progressPercentageText = "0%";

        [ObservableProperty]
        private bool _isLoading = true;

        public event Action<RecentFile>? EditBookRequested;

        public ReaderViewModel(RecentFile book)
        {
            Book = book ?? throw new ArgumentNullException(nameof(book));
            _bookTitle = book.Title;
            _readingProgress = book.ReadingProgress;
            _progressPercentageText = $"{Math.Round(book.ReadingProgress * 100)}%";

            _ = InitializeReaderAsync();
        }

        private async Task InitializeReaderAsync()
        {
            try
            {
                IsLoading = true;
                if (string.IsNullOrEmpty(Book.ReaderHtmlPath) || !File.Exists(Book.ReaderHtmlPath))
                {
                    var assembler = new SinglePageEpubAssembler();
                    await assembler.BuildSinglePageHtmlAsync(Book.InternalEpubPath);
                }

                if (!File.Exists(Book.ReaderHtmlPath))
                {
                    throw new FileNotFoundException("Не удалось найти или собрать файл для чтения.", Book.ReaderHtmlPath);
                }

                ReaderSourceUri = new Uri(Book.ReaderHtmlPath, UriKind.Absolute);

                Book.LastMode = LastWorkMode.Reading;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task UpdateScrollProgressAsync(double progress)
        {
            try
            {
                double clamped = Math.Clamp(progress, 0.0, 1.0);
                if (Math.Abs(clamped - ReadingProgress) < 0.01) return;

                ReadingProgress = clamped;
                ProgressPercentageText = $"{Math.Round(clamped * 100)}%";

                Book.ReadingProgress = clamped;
                await App.Services.RecentFiles.AddOrUpdateAsync(Book);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        [RelayCommand]
        private void EditCurrentBook() => EditBookRequested?.Invoke(Book);
    }
}
