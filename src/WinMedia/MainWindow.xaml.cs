using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using WinMedia.Models;
using MessageBox = System.Windows.MessageBox;

namespace WinMedia;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".wmv", ".avi", ".m4v"
    };

    private readonly List<MediaItem> _allMedia = [];
    private readonly ObservableCollection<MediaItem> _filteredMedia = [];
    private string? _currentFolder;
    private MediaItem? _selectedMedia;

    public MainWindow()
    {
        InitializeComponent();
        GalleryListBox.ItemsSource = _filteredMedia;
        UpdateResultsText();
        ResetPreview();
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a media folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(_currentFolder) && Directory.Exists(_currentFolder))
        {
            dialog.SelectedPath = _currentFolder;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await LoadFolderAsync(dialog.SelectedPath);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFolder) || !Directory.Exists(_currentFolder))
        {
            return;
        }

        await LoadFolderAsync(_currentFolder);
    }

    private async Task LoadFolderAsync(string folderPath)
    {
        try
        {
            SetBusyState(true, "Scanning media files...");
            _currentFolder = folderPath;
            CurrentFolderText.Text = folderPath;
            ResetPreview();
            _allMedia.Clear();
            _filteredMedia.Clear();
            UpdateResultsText();

            var items = await Task.Run(() => DiscoverMedia(folderPath));
            _allMedia.AddRange(items);
            RebuildView();
            StatusText.Text = $"Loaded {_allMedia.Count} item(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to load folder.";
            MessageBox.Show($"Could not load the selected folder.\n\n{ex.Message}", "WinMedia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false, StatusText.Text);
        }
    }

    private static List<MediaItem> DiscoverMedia(string rootFolder)
    {
        var items = new List<MediaItem>();

        foreach (var filePath in EnumerateFilesSafe(rootFolder))
        {
            try
            {
                var extension = Path.GetExtension(filePath);
                var isImage = ImageExtensions.Contains(extension);
                var isVideo = VideoExtensions.Contains(extension);

                if (!isImage && !isVideo)
                {
                    continue;
                }

                var info = new FileInfo(filePath);
                items.Add(new MediaItem
                {
                    Name = info.Name,
                    Path = info.FullName,
                    Extension = extension.TrimStart('.').ToUpperInvariant(),
                    MediaType = isImage ? "Image" : "Video",
                    CreatedAt = info.CreationTime,
                    ModifiedAt = info.LastWriteTime,
                    SizeBytes = info.Length,
                    Thumbnail = isImage ? CreateImageThumbnail(info.FullName) : null
                });
            }
            catch
            {
            }
        }

        return items;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootFolder)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] subDirectories = [];
            string[] files = [];

            try
            {
                subDirectories = Directory.GetDirectories(current);
            }
            catch
            {
            }

            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
            }

            foreach (var directory in subDirectories)
            {
                pending.Push(directory);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static BitmapImage? CreateImageThumbnail(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 420;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildView()
    {
        IEnumerable<MediaItem> query = _allMedia;

        var searchTerm = SearchTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(item =>
                item.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        var selectedFilter = (FilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        query = selectedFilter switch
        {
            "Images" => query.Where(item => item.IsImage),
            "Videos" => query.Where(item => item.IsVideo),
            _ => query
        };

        var selectedSort = (SortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Name";
        query = selectedSort switch
        {
            "Date modified" => DescendingCheckBox.IsChecked == true
                ? query.OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.Name)
                : query.OrderBy(item => item.ModifiedAt).ThenBy(item => item.Name),
            "Size" => DescendingCheckBox.IsChecked == true
                ? query.OrderByDescending(item => item.SizeBytes).ThenBy(item => item.Name)
                : query.OrderBy(item => item.SizeBytes).ThenBy(item => item.Name),
            _ => DescendingCheckBox.IsChecked == true
                ? query.OrderByDescending(item => item.Name)
                : query.OrderBy(item => item.Name)
        };

        _filteredMedia.Clear();
        foreach (var item in query)
        {
            _filteredMedia.Add(item);
        }

        UpdateResultsText();

        if (_selectedMedia is null || !_filteredMedia.Contains(_selectedMedia))
        {
            GalleryListBox.SelectedItem = null;
            ResetPreview();
        }
    }

    private void UpdateResultsText()
    {
        ResultsText.Text = $"{_filteredMedia.Count} item(s)";
    }

    private void ResetPreview()
    {
        _selectedMedia = null;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewVideo.Stop();
        PreviewVideo.Source = null;
        PreviewVideo.Visibility = Visibility.Collapsed;
        PreviewPlaceholderText.Visibility = Visibility.Visible;
        SelectedNameText.Text = "No media selected";
        SelectedTypeText.Text = string.Empty;
        SelectedPathText.Text = string.Empty;
        SelectedMetaText.Text = string.Empty;
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        OpenInExplorerButton.IsEnabled = false;
    }

    private void GalleryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GalleryListBox.SelectedItem is not MediaItem item)
        {
            ResetPreview();
            return;
        }

        _selectedMedia = item;
        SelectedNameText.Text = item.Name;
        SelectedTypeText.Text = $"{item.MediaType} • {item.Extension} • {item.DisplaySize}";
        SelectedPathText.Text = item.Path;
        SelectedMetaText.Text = $"Created: {item.CreatedLabel}\nModified: {item.ModifiedLabel}\nFolder: {item.FolderName}";
        OpenInExplorerButton.IsEnabled = true;

        if (item.IsImage)
        {
            ShowImagePreview(item);
            return;
        }

        ShowVideoPreview(item);
    }

    private void ShowImagePreview(MediaItem item)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(item.Path);
            image.EndInit();
            image.Freeze();

            PreviewVideo.Stop();
            PreviewVideo.Source = null;
            PreviewVideo.Visibility = Visibility.Collapsed;
            PreviewImage.Source = image;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Visibility = Visibility.Collapsed;
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StatusText.Text = $"Previewing image: {item.Name}";
        }
        catch (Exception ex)
        {
            ResetPreview();
            StatusText.Text = $"Image preview failed: {ex.Message}";
        }
    }

    private void ShowVideoPreview(MediaItem item)
    {
        try
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewVideo.Stop();
            PreviewVideo.Source = new Uri(item.Path);
            PreviewVideo.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Visibility = Visibility.Collapsed;
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            PreviewVideo.Play();
            StatusText.Text = $"Previewing video: {item.Name}";
        }
        catch (Exception ex)
        {
            ResetPreview();
            StatusText.Text = $"Video preview failed: {ex.Message}";
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RebuildView();
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RebuildView();
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RebuildView();
    }

    private void DescendingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RebuildView();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia?.IsVideo != true)
        {
            return;
        }

        PreviewVideo.Play();
        StatusText.Text = $"Playing {_selectedMedia.Name}";
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia?.IsVideo != true)
        {
            return;
        }

        PreviewVideo.Pause();
        StatusText.Text = $"Paused {_selectedMedia.Name}";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia?.IsVideo != true)
        {
            return;
        }

        PreviewVideo.Stop();
        StatusText.Text = $"Stopped {_selectedMedia.Name}";
    }

    private void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia is null || !File.Exists(_selectedMedia.Path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_selectedMedia.Path}\"",
            UseShellExecute = true
        });
    }

    private void PreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia is not null)
        {
            StatusText.Text = $"Video ready: {_selectedMedia.Name}";
        }
    }

    private void PreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        var message = string.IsNullOrWhiteSpace(e.ErrorException?.Message)
            ? "Unsupported codec or unreadable file."
            : e.ErrorException.Message;

        StatusText.Text = $"Video failed: {message}";
    }

    private void SetBusyState(bool isBusy, string message)
    {
        OpenFolderButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        SearchTextBox.IsEnabled = !isBusy;
        FilterComboBox.IsEnabled = !isBusy;
        SortComboBox.IsEnabled = !isBusy;
        DescendingCheckBox.IsEnabled = !isBusy;
        StatusText.Text = message;
    }
}
