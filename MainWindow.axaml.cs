/*
 * 
 *
 * This source code is licensed under the BSD-style license found in the
 * LICENSE file in the root directory of this source tree. 
 * 
 */

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YT2MP3Sharp;

public partial class MainWindow : Window
{
    private const int Mp3Index = 0;
    private const int Mp4Index = 1;
    private const int MaxLogLength = 200_000;

    private readonly AppSettings _settings;
    private readonly MediaDownloader _downloader;

    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _qualityCts;
    private string? _qualitiesLoadedFor;

    public MainWindow()
    {
        InitializeComponent();

        _downloader = new MediaDownloader(AppendLog);
        _settings = AppSettings.Load();

        Formatbox.SelectedIndex = _settings.Format == MediaFormat.Mp4 ? Mp4Index : Mp3Index;
        ShowOutputFolder();

        Formatbox.SelectionChanged += Formatbox_SelectionChanged;
        UrlTxt.LostFocus += UrlTxt_LostFocus;
        UrlTxt.KeyDown += UrlTxt_KeyDown;
        Closing += (_, _) => _downloadCts?.Cancel();

        AppendLog("Ready.");
    }

    private bool IsBusy => _downloadCts is not null;

    private MediaFormat SelectedFormat => Formatbox.SelectedIndex == Mp4Index ? MediaFormat.Mp4 : MediaFormat.Mp3;

    // ----- Event handlers ---------------------------------------------------------------

    private async void DownloadBtn_Click(object? sender, RoutedEventArgs e) => await StartDownloadAsync();

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_downloadCts is null)
            return;

        _downloadCts.Cancel();
        CancelBtn.IsEnabled = false;
        StatusTxt.Text = "Cancelling...";
    }

    private async void SelectFolderButton_Click(object? sender, RoutedEventArgs e) => await PickOutputFolderAsync();

    private async void UrlTxt_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await StartDownloadAsync();
    }

    private async void UrlTxt_LostFocus(object? sender, RoutedEventArgs e) => await RefreshQualitiesAsync();

    private async void Formatbox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settings.Format != SelectedFormat)
        {
            _settings.Format = SelectedFormat;
            SaveSettings();
        }

        await RefreshQualitiesAsync();
    }

    // ----- Download ---------------------------------------------------------------------

    private async Task StartDownloadAsync()
    {
        if (IsBusy)
            return;

        var url = UrlTxt.Text?.Trim() ?? string.Empty;
        if (url.Length == 0)
        {
            AppendLog("Paste a YouTube video or playlist link first.");
            return;
        }

        var isPlaylist = MediaDownloader.IsPlaylistUrl(url);
        if (!isPlaylist && !MediaDownloader.IsVideoUrl(url))
        {
            AppendLog("That doesn't look like a YouTube video or playlist link.");
            return;
        }

        var ffmpegPath = MediaDownloader.FindFfmpeg();
        if (ffmpegPath is null)
        {
            AppendLog("ffmpeg was not found. Install it, or put the ffmpeg executable next to this app.");
            return;
        }

        var outputFolder = _settings.OutputFolder;
        if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
        {
            AppendLog("Choose a folder to save to.");
            outputFolder = await PickOutputFolderAsync();
            if (outputFolder is null)
                return;
        }

        var format = SelectedFormat;
        var quality = format == MediaFormat.Mp4 && !isPlaylist ? Qualitybox.SelectedItem as string : null;

        var cts = new CancellationTokenSource();
        _downloadCts = cts;
        SetBusy(true);
        Progressbar.Value = 0;
        StatusTxt.Text = "Starting...";

        try
        {
            // Task.Run keeps the stream-manifest parsing off the UI thread. The Progress<T>
            // objects are created here, on the UI thread, so their callbacks run on it too.
            if (isPlaylist)
            {
                var progress = new Progress<PlaylistProgress>(p =>
                {
                    Progressbar.Value = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
                    StatusTxt.Text = $"{p.Completed} / {p.Total}";
                });

                var result = await Task.Run(
                    () => _downloader.DownloadPlaylistAsync(url, outputFolder, format, ffmpegPath, progress, cts.Token),
                    cts.Token);

                AppendLog(result.Failed == 0
                    ? $"Playlist finished: {result.Succeeded} downloaded."
                    : $"Playlist finished: {result.Succeeded} downloaded, {result.Failed} failed.");
                StatusTxt.Text = result.Failed == 0 ? "Done" : $"{result.Failed} failed";
            }
            else
            {
                var progress = new Progress<double>(p =>
                {
                    Progressbar.Value = p * 100;
                    StatusTxt.Text = $"{p:P0}";
                });

                await Task.Run(
                    () => _downloader.DownloadVideoAsync(url, outputFolder, format, quality, ffmpegPath, progress, cts.Token),
                    cts.Token);

                Progressbar.Value = 100;
                StatusTxt.Text = "Done";
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            StatusTxt.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            StatusTxt.Text = "Failed";
        }
        finally
        {
            _downloadCts = null;
            cts.Dispose();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UrlTxt.IsEnabled = !busy;
        Formatbox.IsEnabled = !busy;
        SelectFolderBtn.IsEnabled = !busy;
        DownloadBtn.IsEnabled = !busy;
        Qualitybox.IsEnabled = !busy && Qualitybox.ItemCount > 0;

        CancelBtn.IsEnabled = busy;
        CancelBtn.IsVisible = busy;
    }

    // ----- Quality list -----------------------------------------------------------------

    /// <summary>
    /// Fills the quality box for a single video in MP4 mode. MP3 and playlists always
    /// use the best available stream, so the box stays empty and disabled for them.
    /// </summary>
    private async Task RefreshQualitiesAsync()
    {
        var url = UrlTxt.Text?.Trim() ?? string.Empty;
        var wanted = SelectedFormat == MediaFormat.Mp4 && MediaDownloader.IsVideoUrl(url);

        if (!wanted)
        {
            _qualityCts?.Cancel();
            _qualityCts = null;
            _qualitiesLoadedFor = null;
            Qualitybox.ItemsSource = null;
            Qualitybox.IsEnabled = false;
            return;
        }

        if (url == _qualitiesLoadedFor)
            return;

        _qualityCts?.Cancel();
        var cts = new CancellationTokenSource();
        _qualityCts = cts;
        _qualitiesLoadedFor = url;

        try
        {
            var qualities = await Task.Run(() => _downloader.GetMp4QualitiesAsync(url, cts.Token), cts.Token);

            // A newer lookup (or a format change) replaced this one while it was running.
            if (!ReferenceEquals(_qualityCts, cts))
                return;

            Qualitybox.ItemsSource = qualities;
            Qualitybox.SelectedIndex = qualities.Count > 0 ? 0 : -1;
            Qualitybox.IsEnabled = !IsBusy && qualities.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // Superseded, nothing to do.
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_qualityCts, cts))
            {
                _qualitiesLoadedFor = null;
                AppendLog($"Couldn't load the available qualities: {ex.Message}");
            }
        }
    }

    // ----- Save folder and settings -----------------------------------------------------

    private async Task<string?> PickOutputFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save downloads",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return null;

        _settings.OutputFolder = path;
        SaveSettings();
        ShowOutputFolder();
        return path;
    }

    private void ShowOutputFolder()
    {
        var folder = _settings.OutputFolder;
        var hasFolder = !string.IsNullOrEmpty(folder);

        Spath.Text = hasFolder ? folder : "No save folder selected";
        ToolTip.SetTip(Spath, hasFolder ? folder : null);
    }

    private void SaveSettings()
    {
        var error = _settings.Save();
        if (error is not null)
            AppendLog($"Couldn't save settings: {error}");
    }

    // ----- Log --------------------------------------------------------------------------

    /// <summary>Safe to call from any thread.</summary>
    private void AppendLog(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(message));
            return;
        }

        var text = $"{Logpane.Text}[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (text.Length > MaxLogLength)
            text = text[^(MaxLogLength / 2)..];

        Logpane.Text = text;
        Logpane.CaretIndex = text.Length;
    }
}
