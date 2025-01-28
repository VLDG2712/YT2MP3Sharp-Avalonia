/*
 * 
 *
 * This source code is licensed under the BSD-style license found in the
 * LICENSE file in the root directory of this source tree. 
 * 
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YT2MP3Sharp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Logpane.Text = "Ready...\n";
    }
    public async Task SelectFolder(Window parentWindow)
    {
        // Create an instance of FilePickerFolderOptions
        var options = new FolderPickerOpenOptions
        {
            Title = "Select a Folder",
            AllowMultiple = false
        };

        // Show the folder picker dialog
        var result = await parentWindow.StorageProvider.OpenFolderPickerAsync(options);

        if (result != null && result.Count > 0)
        {
            // Handle the selected folder
            var selectedFolder = result[0];
            string confPath = System.AppDomain.CurrentDomain.BaseDirectory;
            await using (StreamWriter outputFile = new StreamWriter(Path.Combine(confPath, "settings.dat")))
            {
                await outputFile.WriteAsync(selectedFolder.Path.LocalPath);
                await Dispatcher.UIThread.InvokeAsync(() => Spath.Content = selectedFolder.Path.LocalPath);
            }

        }
    }
    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
       await SelectFolder(this);
        
    }

    private async void Downloadbtn(object sender, RoutedEventArgs args)
    {

        string path = File.ReadAllText($"{System.AppDomain.CurrentDomain.BaseDirectory}settings.dat");
        string url = UrlTxt.Text;
        await Dispatcher.UIThread.InvokeAsync(() => Progressbar.Value = 0);
        
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(path)){
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += "Please fill in all the fields and select your Saving Path\n");
            return;
        }

        string format = (Formatbox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        string selectedQuality = Qualitybox.SelectedItem as string;
        try 
        {
            if (url.Contains("playlist"))
            {
                await Task.Run(() => DownloadPlaylistAsync(url, path, format));
            }
            else 
            {
                await Task.Run(() => DownloadVideoAsync(url, path, format, selectedQuality));
            }
        }

        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Error: {ex.Message}\n");
        }
    }
    private async Task<List<string>> GetAvailableVideoQualitiesAsync(string url)
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(url);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

        var videoQualities = streamManifest
            .GetVideoStreams()
            .Select(s => s.VideoQuality.Label)
            .Distinct()
            .ToList();
        return videoQualities;
    }
    private async Task DownloadVideoAsync(string url, string outputPath, string format, string selectedQuality)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += "Fetching Video Information\n");
            var youtube = new YoutubeClient();

            var video = await youtube.Videos.GetAsync(url);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

            var availableQualities = streamManifest
                .GetVideoStreams()
                .Select(s => s.VideoQuality.Label)
                .Distinct()
                .ToList();


            if (!availableQualities.Contains(selectedQuality) && format != "MP3")
            {
                //selectedQuality = availableQualities.FirstOrDefault();
                await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Selected quality {selectedQuality} not available. Falling back to default quality\n");
            }


            switch (format)
            {
                case "MP4":
                {
                        var audioStreamInfo = streamManifest
                            .GetAudioStreams()
                            .Where(s => s.Container == Container.Mp4)
                            .GetWithHighestBitrate();

                        var videoStreamInfo = streamManifest
                            .GetVideoStreams()
                            .Where(s => s.Container == Container.Mp4 && s.VideoQuality.Label == selectedQuality)
                            .FirstOrDefault();

                        if (videoStreamInfo != null && audioStreamInfo != null)
                        {
                            var sanitizedTitle = SanitizeFileName(video.Title);
                            var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Selected Quality: {selectedQuality}\n");
                            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Downloading: {video.Title}\n");
                            var streamInfos = new IStreamInfo[] { audioStreamInfo, videoStreamInfo };
                            var conversionRequest = new ConversionRequestBuilder(finalFilePath)
                                .SetFFmpegPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"))
                                .Build();
                            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Muxing: {video.Title}\n");
                            await youtube.Videos.DownloadAsync(streamInfos, conversionRequest);
                            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Download complete: {finalFilePath}\n");
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += "No suitable stream found\n");
                        }

                        break;


                }
                case "MP3":
                {
                    var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                    if (streamInfo != null)
                    {
                        var sanitizedTitle = SanitizeFileName(video.Title);
                        var tempFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                        var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp3");

                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Downloading: {video.Title}\n");
                        await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Converting: {video.Title}\n");

                        await Task.Run(() =>ConvertToMp3(tempFilePath, finalFilePath));

                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Download Complete: {finalFilePath}\n");
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += "No suitable audio stream found! \n");
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Error: {ex.Message}\n");
        }
    }
    private async Task DownloadPlaylistAsync(string playlistUrl, string outputPath, string format)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += "Fetching playlist information...\n");
            var youtube = new YoutubeClient();
            var playlist = await youtube.Playlists.GetAsync(playlistUrl);
            var videos = youtube.Playlists.GetVideosAsync(playlist.Id);

            int videoCount = 0;
            await foreach (var video in videos)
            {
                videoCount++;
            }
            await Dispatcher.UIThread.InvokeAsync(() => Progressbar.Maximum = videoCount);
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Found {videoCount} videos\n");

            var tasks = new ConcurrentBag<Task>();
            var throttler = new SemaphoreSlim(3); // TO DO SETTINGS CONCURENT DOWNLOADS

            await foreach (var video in youtube.Playlists.GetVideosAsync(playlist.Id))
            {
                await throttler.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

                        switch (format)
                        {
                            case "MP4":
                            {
                                var audioStreamInfo = streamManifest
                                    .GetAudioStreams()
                                    .Where(s => s.Container == Container.Mp4)
                                    .GetWithHighestBitrate();
                                var videoStreamInfo = streamManifest
                                    .GetVideoStreams()
                                    .Where(s => s.Container == Container.Mp4)
                                    .GetWithHighestVideoQuality();

                                    if (audioStreamInfo != null && videoStreamInfo != null)
                                {
                                    var sanitizedTitle = SanitizeFileName(video.Title);
                                    var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Downloading: {video.Title}\n");
                                    var streamInfos = new IStreamInfo[] { audioStreamInfo, videoStreamInfo };
                                    var conversionRequest = new ConversionRequestBuilder(finalFilePath)
                                        .SetFFmpegPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"))
                                        .Build();
                                    await youtube.Videos.DownloadAsync(streamInfos, conversionRequest);
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Download complete: {finalFilePath}\n");

                                }
                                else
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"No suitable stream found! for {video.Title}\n");
                                }

                                break;
                            }
                            case "MP3":
                            {
                                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                                if (streamInfo != null)
                                {
                                    var sanitizedTitle = SanitizeFileName(video.Title);
                                    var tempFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                                    var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp3");
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Download: {video.Title}\n");

                                    // download Async
                                    await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Converting: {video.Title}\n");

                                    // converting to MP3
                                    await Task.Run(() => ConvertToMp3(tempFilePath, finalFilePath));
                                    await Dispatcher.UIThread.InvokeAsync(() => {
                                        Logpane.Text += $"Download complete: {finalFilePath}\n";
                                        Logpane.CaretIndex = Logpane.Text.Length;});
                                }
                                else 
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"No suitable audio stream found for: {video.Title}\n");
                                }

                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Error Downloading: {video.Title} > {ex.Message}\n");
                    }
                    finally
                    {
                        throttler.Release();
                    }
                    await Dispatcher.UIThread.InvokeAsync(() => Progressbar.Value += 1);
                }));
            }
            await Task.WhenAll(tasks); 
        }
        catch (Exception ex) 
        { 
            await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Error: {ex.Message}\n"); 
        }
    }

    private async void UrlTxt_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(UrlTxt.Text))
        try
            {
                var availableQualities = await GetAvailableVideoQualitiesAsync(UrlTxt.Text);
                Qualitybox.ItemsSource = availableQualities;
                Qualitybox.SelectedIndex = 0;
            }
        catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"Error loading video qualities: {ex.Message}\n");
            }
    }

    private void ConvertToMp3(string inputFilePath, string outputFilePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            int coreCount = Environment.ProcessorCount;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments =
                    $"-i \"{inputFilePath}\" -threads {coreCount} -vn -ar 44100 -ac 2 -b:a 320k \"{outputFilePath}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;
                process.OutputDataReceived += async (sender, args) =>
                {
                    if (args.Data != null)
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"{args.Data}\n");
                };
                process.ErrorDataReceived += async (sender, args) =>
                {
                    if (args.Data != null)
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"{args.Data}\n");
                };
                process.Start();
                process.WaitForExit();
            }

            File.Delete(inputFilePath); // Deletes File After Conversion!
        }
        else
        {
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            int coreCount = Environment.ProcessorCount;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments =
                    $"-i \"{inputFilePath}\" -threads {coreCount} -vn -ar 44100 -ac 2 -b:a 320k \"{outputFilePath}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;
                process.OutputDataReceived += async (sender, args) =>
                {
                    if (args.Data != null)
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"{args.Data}\n");
                };
                process.ErrorDataReceived += async (sender, args) =>
                {
                    if (args.Data != null)
                        await Dispatcher.UIThread.InvokeAsync(() => Logpane.Text += $"{args.Data}\n");
                };
                process.Start();
                process.WaitForExit();
            }

            File.Delete(inputFilePath); // Deletes File After Conversion!
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        // Sanitizes File Names!
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedTitle = new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
        return sanitizedTitle;
    }
   
}