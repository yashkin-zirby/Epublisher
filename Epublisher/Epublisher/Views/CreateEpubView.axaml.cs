using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Epublisher.DataEntities;
using Epublisher.Helpers;
using Epublisher.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Epublisher.Views
{
    public partial class CreateEpubView : ContentPage
    {
        private CreateEpubViewModel? _vm;
        private bool _isPreviewLoaded;
        private int? _pendingScrollChapterIndex;
        private readonly List<int> _searchMatchIndices = new();
        private int _currentMatchIndex = -1;
        private int _savedSelStart;
        private int _savedSelEnd;

        public CreateEpubView()
        {
            InitializeComponent();
            PreviewWebView.NavigationStarted += (_, args) =>
            {
                if (_vm == null || !_vm.IsSingleChapterPreview || args.Request == null)
                    return;

                string uriStr = args.Request.ToString();
                string targetFileName = Path.GetFileName(uriStr.Split('#').First());
                string currentFileName = _vm.SelectedChapter?.FullFileName ?? string.Empty;
                if (!string.IsNullOrEmpty(targetFileName) &&
                    !targetFileName.Equals(currentFileName, StringComparison.OrdinalIgnoreCase) &&
                    (targetFileName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                     targetFileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                {
                    args.Cancel = true;
                    _vm.SelectChapterByHref(uriStr);
                }
            };
            PreviewWebView.NavigationCompleted += async (_, args) =>
            {
                if (args.IsSuccess)
                {
                    _isPreviewLoaded = true;
                    await ApplyPreviewThemeAsync();
                    if (_pendingScrollChapterIndex.HasValue)
                    {
                        int targetIndex = _pendingScrollChapterIndex.Value;
                        _pendingScrollChapterIndex = null;
                        ScrollPreviewToChapter(targetIndex);
                    }
                }
            };
            ChapterEditorBox.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.SelectionStartProperty ||
                    args.Property == TextBox.SelectionEndProperty)
                {
                    if (ChapterEditorBox.SelectionStart != ChapterEditorBox.SelectionEnd)
                    {
                        _savedSelStart = ChapterEditorBox.SelectionStart;
                        _savedSelEnd = ChapterEditorBox.SelectionEnd;
                    }
                }
            };
        }
        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _isPreviewLoaded = false;
            PreviewWebView.Source = null;
            base.OnUnloaded(e);
        }
        private void OnInsertBottomTextClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            _vm.CustomCssContent = CssHelper.EnsureBottomTextStyleExists(_vm.CustomCssContent);
            ApplyFormat(EpubTextFormatingHelper.ApplyBottomText);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_vm != null)
            {
                _vm.PreviewHtmlRebuilt -= OnPreviewHtmlRebuilt;
                _vm.ScrollPreviewToChapterRequested -= ScrollPreviewToChapter;
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is CreateEpubViewModel vm)
            {
                _vm = vm;
                _vm.ScrollPreviewToChapterRequested += ScrollPreviewToChapter;
                _vm.PreviewHtmlRebuilt += OnPreviewHtmlRebuilt;
                _vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
        protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || _vm == null) return;

            bool hasCtrlOrCmd = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ||
                                e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);

            if (!hasCtrlOrCmd) return;

            switch (e.Key)
            {
                case Avalonia.Input.Key.F:

                    ChapterSearchBox.Focus();
                    ChapterSearchBox.SelectAll();
                    e.Handled = true;
                    break;

                case Avalonia.Input.Key.B:
                    if (ChapterEditorBox.IsFocused)
                    {
                        ApplyFormat(EpubTextFormatingHelper.ApplyBold);
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.I:
                    if (ChapterEditorBox.IsFocused)
                    {
                        ApplyFormat(EpubTextFormatingHelper.ApplyItalic);
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.S:
                    _ = _vm.SaveAndRebuildPreviewAsync();
                    e.Handled = true;
                    break;
            }
        }
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CreateEpubViewModel.SelectedChapter))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateSearchMatches(focusEditor: false);
                });
            }
        }
        private async void ScrollPreviewToChapter(int chapterIndex)
        {
            if (_vm == null || _vm.IsSingleChapterPreview) return;

            if (!_isPreviewLoaded)
            {
                _pendingScrollChapterIndex = chapterIndex;
                return;
            }

            try
            {
                string script = "(function() {\n"+
            $"        var el = document.getElementById('epub-sec-{chapterIndex}');\n"+
            "    if (el) {\n"+
            "       el.scrollIntoView({ behavior: 'smooth', block: 'start' });\n"+
            "    }\n"+
            "})();";
                await PreviewWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        private void OnScrollToSelectedChapterClick(object? sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedChapter != null)
            {
                int idx = _vm.Chapters.IndexOf(_vm.SelectedChapter);
                if (idx >= 0) ScrollPreviewToChapter(idx);
            }
        }
        private void InsertCssSnippet(string snippet)
        {
            if (_vm == null) return;

            string current = CssEditorBox.Text ?? string.Empty;
            var result = CssHelper.InsertSnippet(current, CssEditorBox.SelectionStart, snippet);

            _vm.CustomCssContent = result.UpdatedText;
            CssEditorBox.Focus();
            CssEditorBox.SelectionStart = result.NewSelectionStart;
            CssEditorBox.SelectionEnd = result.NewSelectionEnd;
        }

        private void OnInsertFullpageCssClick(object? sender, RoutedEventArgs e) =>
            InsertCssSnippet(CssHelper.FullpageImageCss);

        private void OnInsertInlineImageCssClick(object? sender, RoutedEventArgs e) =>
            InsertCssSnippet(CssHelper.InlineImageCss);

        private void OnInsertDividerCssClick(object? sender, RoutedEventArgs e) =>
            InsertCssSnippet(CssHelper.SceneDividerCss);

        private void OnInsertBlockquoteCssClick(object? sender, RoutedEventArgs e) =>
            InsertCssSnippet(CssHelper.BlockquoteCss);

        private void OnInsertDropCapCssClick(object? sender, RoutedEventArgs e) =>
            InsertCssSnippet(CssHelper.DropCapCss);
        private void OnAddDefaultStylesClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            _vm.CustomCssContent = CssHelper.EnsureDefaultEditorStylesExist(_vm.CustomCssContent);
        }
        private void OnFormatHtmlClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            _vm.ChapterHtmlContent = EpubTextFormatingHelper.FormatHtml(_vm.ChapterHtmlContent);
        }
        private void OnInsertChapterLinkClick(object? sender, RoutedEventArgs e)
        {
            if (_vm?.TargetChapterForLink == null) return;

            var target = _vm.TargetChapterForLink;
            string href = string.IsNullOrEmpty(target.FullFileName) ? $"{target.Id}.xhtml" : target.FullFileName;

            ApplyFormat((text, selStart, selEnd) =>
            {
                int start = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
                int end = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);

                // Если пользователь выделил текст — делаем его текстом ссылки, иначе берем название целевой главы
                string linkLabel = start < end
                    ? text.Substring(start, end - start)
                    : System.Net.WebUtility.HtmlEncode(target.Title);

                string linkTag = $"<a href=\"{href}\" epub:type=\"backlink\">{linkLabel}</a>";
                string updated = text.Remove(start, end - start).Insert(start, linkTag);
                int newPos = start + linkTag.Length;

                return new Helpers.TextFormatResult(updated, newPos, newPos);
            });
        }
        private void OnPreviewHtmlRebuilt()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_vm?.PreviewUri != null)
                {
                    _isPreviewLoaded = false;
                    PreviewWebView.Source = new Uri("about:blank");
                    var uriBuilder = new UriBuilder(_vm.PreviewUri)
                    {
                        Query = $"t={DateTime.UtcNow.Ticks}"
                    };

                    PreviewWebView.Source = uriBuilder.Uri;
                }
            });
        }

        private async Task ApplyPreviewThemeAsync()
        {
            if (!_isPreviewLoaded) return;

            try
            {
                bool isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
                string bgHex = isDark ? "#18181B" : "#FFFFFF";
                string fgHex = isDark ? "#E4E4E7" : "#1A1A1A";
                string linkHex = isDark ? "#60A5FA" : "#1D4ED8";

                string script = "(function() {var styleId = 'app-theme-override';\n"+
                "    var existing = document.getElementById(styleId);\n"+
                "    if (!existing) {\n"+
                "        existing = document.createElement('style');\n" +
                "        existing.id = styleId;\n" +
                "        document.head.appendChild(existing);\n" +
                "    }\n"+
                "    existing.textContent = `\n" +
                "        html, body { background-color: "+bgHex+ " !important; color: "+fgHex+" !important; }}\n" +
                "        body *:not(a):not(a *):not(img):not(svg):not(path) { color: "+fgHex+" !important; background-color: transparent !important; }\n" +
                "        a, a * {color: "+linkHex+" !important; }\n"+
                "    `;\n" +
                "})();";

                await PreviewWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }

        // --- Кнопки форматирования выделенного текста ---

        private void ApplyFormat(Func<string, int, int, TextFormatResult> formatter)
        {
            if (_vm == null) return;

            string currentText = ChapterEditorBox.Text ?? string.Empty;
            int selStart = ChapterEditorBox.SelectionStart;
            int selEnd = ChapterEditorBox.SelectionEnd;
            if (selStart == selEnd && _savedSelStart != _savedSelEnd &&
                _savedSelStart <= currentText.Length && _savedSelEnd <= currentText.Length)
            {
                selStart = _savedSelStart;
                selEnd = _savedSelEnd;
            }

            var result = formatter(currentText, selStart, selEnd);
            _vm.ChapterHtmlContent = result.UpdatedText;

            _savedSelStart = result.NewSelectionStart;
            _savedSelEnd = result.NewSelectionEnd;

            ChapterEditorBox.Focus();
            ChapterEditorBox.SelectionStart = result.NewSelectionStart;
            ChapterEditorBox.SelectionEnd = result.NewSelectionEnd;
        }
        private void OnChapterSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateSearchMatches(focusEditor: false);
        }

        private async void OnChapterFileNameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (_vm != null && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                await _vm.RenameSelectedChapterFileCommand.ExecuteAsync(tb.Text);
            }
        }

        private async void OnChapterFileNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter && _vm != null && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                await _vm.RenameSelectedChapterFileCommand.ExecuteAsync(tb.Text);
                ChapterEditorBox.Focus();
                e.Handled = true;
            }
        }

        private async void OnImageResourceNameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (_vm != null && sender is TextBox tb && tb.DataContext is BookResourceItem item && !string.IsNullOrWhiteSpace(tb.Text))
            {
                await _vm.RenameImageResourceAsync(item, tb.Text);
            }
        }

        private async void OnImageResourceNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter && _vm != null && sender is TextBox tb && tb.DataContext is BookResourceItem item && !string.IsNullOrWhiteSpace(tb.Text))
            {
                await _vm.RenameImageResourceAsync(item, tb.Text);
                e.Handled = true;
            }
        }

        private void OnChapterSearchKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                    NavigateSearchMatch(-1);
                else
                    NavigateSearchMatch(1);

                e.Handled = true;
            }
        }
        private void OnFindNextClick(object? sender, RoutedEventArgs e) => NavigateSearchMatch(1);

        private void OnFindPreviousClick(object? sender, RoutedEventArgs e) => NavigateSearchMatch(-1);

        private void UpdateSearchMatches(bool focusEditor)
        {
            _searchMatchIndices.Clear();
            _currentMatchIndex = -1;

            string query = ChapterSearchBox.Text ?? string.Empty;
            string content = ChapterEditorBox.Text ?? string.Empty;

            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content))
            {
                SearchCounterText.Text = string.Empty;
                return;
            }

            int index = 0;
            while ((index = content.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                _searchMatchIndices.Add(index);
                index += query.Length;
            }

            if (_searchMatchIndices.Count > 0)
            {
                _currentMatchIndex = 0;
                HighlightCurrentMatch(query.Length, focusEditor);
            }
            else
            {
                SearchCounterText.Text = "0/0";
            }
        }

        private void NavigateSearchMatch(int step)
        {
            string query = ChapterSearchBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(query)) return;

            if (_searchMatchIndices.Count == 0)
            {
                UpdateSearchMatches(focusEditor: true);
                return;
            }

            _currentMatchIndex = (_currentMatchIndex + step + _searchMatchIndices.Count) % _searchMatchIndices.Count;
            HighlightCurrentMatch(query.Length, focusEditor: true);
        }

        private void HighlightCurrentMatch(int length, bool focusEditor)
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatchIndices.Count) return;

            int startPos = _searchMatchIndices.ElementAt(_currentMatchIndex);
            SearchCounterText.Text = $"{_currentMatchIndex + 1}/{_searchMatchIndices.Count}";

            ChapterEditorBox.CaretIndex = startPos + length;
            ChapterEditorBox.SelectionStart = startPos;
            ChapterEditorBox.SelectionEnd = startPos + length;

            if (focusEditor)
            {
                ChapterEditorBox.Focus();
            }
        }

        private void OnFormatBoldClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ApplyBold);

        private void OnFormatItalicClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ApplyItalic);

        private void OnFormatUnderlineClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ApplyUnderline);

        private void OnFormatStrikeClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ApplyStrikethrough);

        private void OnFormatH1Click(object? sender, RoutedEventArgs e) =>
            ApplyFormat((t, s, end) => EpubTextFormatingHelper.ApplyHeading(t, s, end, 1));

        private void OnFormatH2Click(object? sender, RoutedEventArgs e) =>
            ApplyFormat((t, s, end) => EpubTextFormatingHelper.ApplyHeading(t, s, end, 2));

        private void OnFormatParagraphClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ApplyParagraph);

        private void OnAlignCenterClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat((t, s, end) => EpubTextFormatingHelper.ApplyAlignment(t, s, end, "center"));

        private void OnAlignJustifyClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat((t, s, end) => EpubTextFormatingHelper.ApplyAlignment(t, s, end, "justify"));

        private void OnInsertPageBreakClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat((t, s, _) => EpubTextFormatingHelper.InsertPageBreak(t, s));
        private void OnClearFormattingClick(object? sender, RoutedEventArgs e) =>
            ApplyFormat(EpubTextFormatingHelper.ClearFormatting);

        private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            string dialogTitle = "Select image";
            if (Application.Current?.TryFindResource("Editor.SelectImageDialogTitle", Application.Current.ActualThemeVariant, out object? res) == true
                && res is string localizedTitle
                && !string.IsNullOrWhiteSpace(localizedTitle))
            {
                dialogTitle = localizedTitle;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = dialogTitle,
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
            });

            if (files.Count == 0) return;

            try
            {
                var imgFile = files.First();
                await using Stream stream = await imgFile.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);

                string? relPath = await _vm.SaveImageAssetToBookAsync(imgFile.Name, ms.ToArray());
                if (!string.IsNullOrEmpty(relPath))
                {
                    ApplyFormat((t, s, _) => EpubTextFormatingHelper.InsertImageTag(t, s, relPath, fullPage: false));
                }
                _vm.RefreshImageResources();
            }
            catch (Exception ex)
            {
                ExceptionProcesser.Process(ex);
            }
        }
    }
}