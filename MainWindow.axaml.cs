/*
 * 
Copyright (c) 2024, VLDG2712
 * All rights reserved.
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
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YT2MP3Sharp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
            using (StreamWriter outputFile = new StreamWriter(Path.Combine(confPath, "settings.conf")))
            {
                await outputFile.WriteAsync(selectedFolder.Path.LocalPath);
            }

        }
    }
    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
       await SelectFolder(this);
        
    }


    private async void downloadbtn(object sender, RoutedEventArgs args)
    {

        string path = File.ReadAllText($"{System.AppDomain.CurrentDomain.BaseDirectory}settings.conf");
        string url = urlTxt.Text;
        logpane.Text = "";
        progressbar.Value = 0;
        
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(path)){
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = "Please fill in All the Fields\n");
            return;
        }

        string format = (formatbox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        try 
        {
            if (url.Contains("playlist"))
            {
                await Task.Run(() => DownloadPlaylistAsync(url, path, format));
            }
            else 
            {
                await Task.Run(() => DownloadVideoAsync(url, path, format));
            }
        }

        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Error: {ex.Message}\n");
        }
    }
    private async Task DownloadVideoAsync(string url, string outputPath, string format)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = "Fetching Video Information\n");
            var youtube = new YoutubeClient();

            var video = await youtube.Videos.GetAsync(url);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

            if (format == "MP4")
            {
                var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

                if (streamInfo != null)
                {
                    var sanitizedTitle = SanitizeFileName(video.Title);
                    var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");

                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Downloading: {video.Title}\n");
                    await youtube.Videos.Streams.DownloadAsync(streamInfo, finalFilePath);
                }
                else 
                {
                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = "No suitable stream found\n");
                }
            }
            else if (format == "MP3")
            {
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo != null)
                {
                    var sanitizedTitle = SanitizeFileName(video.Title);
                    var tempFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                    var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp3");

                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Downloading: {video.Title}\n");
                    await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);
                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Converting: {video.Title}\n");

                    await Task.Run(() =>ConvertToMp3(tempFilePath, finalFilePath));

                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Download Complete: {finalFilePath}\n");
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = "No suitable audio stream found! \n");
                }
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Error: {ex.Message}\n");
        }
    }
    private async Task DownloadPlaylistAsync(string playlistUrl, string outputPath, string format)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = "Fetching playlist information...\n");
            var youtube = new YoutubeClient();
            var playlist = await youtube.Playlists.GetAsync(playlistUrl);
            var videos = youtube.Playlists.GetVideosAsync(playlist.Id);

            int videoCount = 0;
            await foreach (var video in videos)
            {
                videoCount++;
            }
            await Dispatcher.UIThread.InvokeAsync(() => progressbar.Maximum = videoCount);
            await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Found {videoCount} videos\n");

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

                        if (format == "MP4")
                        {
                            var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                            if (streamInfo != null)
                            {
                                var sanitizedTitle = SanitizeFileName(video.Title);
                                var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Downloading: {video.Title}\n");
                                
                                //DownloadAsync
                                await youtube.Videos.Streams.DownloadAsync(streamInfo, finalFilePath);
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Download complete: {finalFilePath}\n");

                            }
                            else
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"No suitable stream found! for {video.Title}\n");
                            }
                        }
                        else if (format == "MP3")
                        {
                            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                            if (streamInfo != null)
                            {
                                var sanitizedTitle = SanitizeFileName(video.Title);
                                var tempFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp4");
                                var finalFilePath = Path.Combine(outputPath, $"{sanitizedTitle}.mp3");
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Download: {video.Title}\n");

                                // Download Async
                                await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Converting: {video.Title}\n");

                                // Convert To MP3 Async
                                await Task.Run(() => ConvertToMp3(tempFilePath, finalFilePath));
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Download complete: {finalFilePath}\n");
                            }
                            else 
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"No suitable audio stream found for: {video.Title}\n");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Error Downloading: {video.Title} > {ex.Message}\n");
                    }
                    finally
                    {
                        throttler.Release();
                    }
                    await Dispatcher.UIThread.InvokeAsync(() => progressbar.Value += 1);
                }));
            }
            await Task.WhenAll(tasks);
       }
       catch (Exception ex)
       {
        await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"Error: {ex.Message}\n");
       }
    }
    private void ConvertToMp3(string inputFilePath, string outputFilePath)
    {
        // Starts ffmpeg Process
        var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ffmpeg", "ffmpeg");

        int coreCount = Environment.ProcessorCount;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{inputFilePath}\" -threads {coreCount} -vn -ar 44100 -ac 2 -b:a 320k \"{outputFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = processStartInfo})
        {
            process.OutputDataReceived += async (sender, args) => { if (args.Data != null) await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"{args.Data}\n");};
            process.ErrorDataReceived += async (sender, args) => { if (args.Data != null) await Dispatcher.UIThread.InvokeAsync(() => logpane.Text = $"{args.Data}\n");};

        
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }
        File.Delete(inputFilePath); // Deletes File After Conversion!
    }
    private string SanitizeFileName(string fileName)
    {
        // Sanitizes File Names!
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedTitle = new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
        return sanitizedTitle;
    }
   
}