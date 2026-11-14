/*
 * 
Copyright (c) 2024, VLDG2712
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the
 * LICENSE file in the root directory of this source tree. 
 * 
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YT2MP3Sharp;

public enum MediaFormat
{
    Mp3,
    Mp4
}

public readonly record struct PlaylistProgress(int Completed, int Total);

public readonly record struct PlaylistResult(int Succeeded, int Failed);

/// <summary>
/// All YouTube and ffmpeg work. Knows nothing about the UI: it reports through the
/// log callback and the progress objects it is given, from whatever thread it runs on.
/// </summary>
public sealed class MediaDownloader
{
    private const int MaxParallelDownloads = 3;
    private const string Mp3Bitrate = "320k";
    private const int MaxFileNameBytes = 200;

    private readonly YoutubeClient _youtube = new();
    private readonly Action<string> _log;

    public MediaDownloader(Action<string> log)
    {
        _log = log;
    }

    public static bool IsVideoUrl(string url) => VideoId.TryParse(url) is not null;

    /// <summary>A link to a video inside a playlist (watch?v=...&amp;list=...) counts as a single video.</summary>
    public static bool IsPlaylistUrl(string url) => !IsVideoUrl(url) && PlaylistId.TryParse(url) is not null;

    /// <summary>
    /// Looks for a working ffmpeg next to the app first, then on PATH. A copy that exists
    /// but cannot start (wrong architecture, missing shared libraries) is skipped.
    /// Null when no usable ffmpeg is found.
    /// </summary>
    public static string? FindFfmpeg()
    {
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        var directories = new List<string> { AppContext.BaseDirectory };
        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        directories.AddRange(searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var directory in directories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate) && CanRun(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry, skip it.
            }
        }

        return null;
    }

    private static bool CanRun(string ffmpegPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                ArgumentList = { "-version" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return false;

            // Drain the pipes so the process can never block on a full buffer.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Quality labels that can be saved as MP4 without re-encoding, best first.</summary>
    public async Task<IReadOnlyList<string>> GetMp4QualitiesAsync(string url, CancellationToken cancellationToken)
    {
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(ParseVideoId(url), cancellationToken);

        return Mp4VideoStreams(manifest)
            .Select(s => s.VideoQuality.Label)
            .Distinct()
            .ToList();
    }

    public async Task DownloadVideoAsync(
        string url,
        string outputFolder,
        MediaFormat format,
        string? quality,
        string ffmpegPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _log("Fetching video information...");
        var video = await _youtube.Videos.GetAsync(ParseVideoId(url), cancellationToken);

        _log($"Downloading: {video.Title}");
        var fileName = SanitizeFileName(video.Title, video.Id.Value);
        var savedTo = await DownloadAsync(video.Id, video.Title, outputFolder, fileName, format, quality, ffmpegPath, progress, cancellationToken);
        _log($"Saved: {savedTo}");
    }

    public async Task<PlaylistResult> DownloadPlaylistAsync(
        string url,
        string outputFolder,
        MediaFormat format,
        string ffmpegPath,
        IProgress<PlaylistProgress>? progress,
        CancellationToken cancellationToken)
    {
        var playlistId = PlaylistId.TryParse(url)
            ?? throw new ArgumentException("That doesn't look like a YouTube playlist link.");

        _log("Fetching playlist information...");
        var playlist = await _youtube.Playlists.GetAsync(playlistId, cancellationToken);

        var videos = new List<PlaylistVideo>();
        await foreach (var video in _youtube.Playlists.GetVideosAsync(playlistId, cancellationToken))
            videos.Add(video);

        if (videos.Count == 0)
        {
            _log($"\"{playlist.Title}\" has no videos that can be downloaded.");
            return default;
        }

        _log($"\"{playlist.Title}\": {videos.Count} videos");
        progress?.Report(new PlaylistProgress(0, videos.Count));

        // Two videos with the same title must not write to the same file.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(PlaylistVideo Video, string FileName)>(videos.Count);
        foreach (var video in videos)
        {
            var fileName = SanitizeFileName(video.Title, video.Id.Value);
            if (!usedNames.Add(fileName))
            {
                fileName = $"{fileName} [{video.Id.Value}]";
                usedNames.Add(fileName);
            }

            items.Add((video, fileName));
        }

        var completed = 0;
        var failed = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelDownloads,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(items, options, async (item, token) =>
        {
            try
            {
                _log($"Downloading: {item.Video.Title}");
                var savedTo = await DownloadAsync(item.Video.Id, item.Video.Title, outputFolder, item.FileName, format, null, ffmpegPath, null, token);
                _log($"Saved: {Path.GetFileName(savedTo)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad video (private, age-restricted, removed) must not stop the rest.
                Interlocked.Increment(ref failed);
                _log($"Failed: {item.Video.Title} ({ex.Message})");
            }
            finally
            {
                progress?.Report(new PlaylistProgress(Interlocked.Increment(ref completed), items.Count));
            }
        });

        return new PlaylistResult(items.Count - failed, failed);
    }

    private async Task<string> DownloadAsync(
        VideoId videoId,
        string title,
        string outputFolder,
        string fileName,
        MediaFormat format,
        string? quality,
        string ffmpegPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);

        return format == MediaFormat.Mp4
            ? await DownloadMp4Async(videoId, title, manifest, outputFolder, fileName, quality, ffmpegPath, progress, cancellationToken)
            : await DownloadMp3Async(videoId, manifest, outputFolder, fileName, ffmpegPath, progress, cancellationToken);
    }

    private async Task<string> DownloadMp4Async(
        VideoId videoId,
        string title,
        StreamManifest manifest,
        string outputFolder,
        string fileName,
        string? quality,
        string ffmpegPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var videoStreams = Mp4VideoStreams(manifest).ToList();
        if (videoStreams.Count == 0)
            throw new InvalidOperationException("no MP4 video stream is available");

        var videoStream = quality is null
            ? null
            : videoStreams.FirstOrDefault(s => s.VideoQuality.Label == quality);

        if (videoStream is null)
        {
            videoStream = videoStreams[0];
            if (quality is not null)
                _log($"{quality} isn't available for \"{title}\", using {videoStream.VideoQuality.Label} instead.");
        }

        var audioStream = PickAudioStream(manifest, preferMp4: true);

        // Everything is written to a temporary file and only moved into place once it is
        // complete, so a failed or cancelled download never leaves a broken file behind
        // and never destroys an earlier good copy.
        var finalPath = Path.Combine(outputFolder, fileName + ".mp4");
        var tempPath = TempPath(outputFolder, videoId, ".mp4");
        try
        {
            var request = new ConversionRequestBuilder(tempPath)
                .SetFFmpegPath(ffmpegPath)
                .Build();

            await _youtube.Videos.DownloadAsync(new[] { audioStream, videoStream }, request, progress, cancellationToken);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        return finalPath;
    }

    private async Task<string> DownloadMp3Async(
        VideoId videoId,
        StreamManifest manifest,
        string outputFolder,
        string fileName,
        string ffmpegPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var audioStream = PickAudioStream(manifest, preferMp4: false);

        var finalPath = Path.Combine(outputFolder, fileName + ".mp3");
        var downloadPath = TempPath(outputFolder, videoId, ".part");
        var convertedPath = TempPath(outputFolder, videoId, ".mp3");
        try
        {
            // The download is the slow part; leave the last 10% of the bar for the conversion.
            var downloadProgress = progress is null ? null : new ScaledProgress(progress, 0.9);
            await _youtube.Videos.Streams.DownloadAsync(audioStream, downloadPath, downloadProgress, cancellationToken);

            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-i", downloadPath,
                    "-vn", "-ar", "44100", "-ac", "2", "-b:a", Mp3Bitrate,
                    "-f", "mp3", convertedPath
                },
                cancellationToken);

            File.Move(convertedPath, finalPath, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            TryDelete(downloadPath);
            TryDelete(convertedPath);
        }

        return finalPath;
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // ArgumentList quotes each argument itself, so titles containing quotes or
        // spaces cannot break the command line.
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"could not start ffmpeg ({ex.Message})", ex);
        }

        // Both pipes are drained while ffmpeg runs; reading them one after the other
        // can deadlock once a pipe buffer fills up.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        await standardOutput;
        var errors = (await standardError).Trim();

        if (process.ExitCode != 0)
        {
            var lastLine = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            throw new InvalidOperationException(
                string.IsNullOrEmpty(lastLine)
                    ? $"ffmpeg failed with exit code {process.ExitCode}"
                    : $"ffmpeg failed: {lastLine}");
        }
    }

    private static VideoId ParseVideoId(string url) =>
        VideoId.TryParse(url) ?? throw new ArgumentException("That doesn't look like a YouTube video link.");

    /// <summary>
    /// Video-only MP4 streams, best quality first. At equal quality H.264 is preferred
    /// over AV1 because far more players and TVs can decode it.
    /// </summary>
    private static IEnumerable<VideoOnlyStreamInfo> Mp4VideoStreams(StreamManifest manifest) =>
        manifest
            .GetVideoOnlyStreams()
            .Where(s => s.Container == Container.Mp4)
            .OrderByDescending(s => s.VideoQuality)
            .ThenByDescending(s => s.VideoCodec.StartsWith("avc", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.Bitrate);

    private static IStreamInfo PickAudioStream(StreamManifest manifest, bool preferMp4)
    {
        var candidates = manifest.GetAudioOnlyStreams().ToList();

        // Dubbed videos carry one audio track per language. Without this filter the
        // highest-bitrate pick can land on a dub instead of the original audio.
        var original = candidates.Where(s => s.IsAudioLanguageDefault != false).ToList();
        if (original.Count > 0)
            candidates = original;

        if (preferMp4)
        {
            // AAC can be copied straight into an MP4 file; anything else gets re-encoded.
            var mp4 = candidates.Where(s => s.Container == Container.Mp4).ToList();
            if (mp4.Count > 0)
                candidates = mp4;
        }

        return candidates.TryGetWithHighestBitrate()
            ?? throw new InvalidOperationException("no audio stream is available");
    }

    private static string TempPath(string folder, VideoId videoId, string extension) =>
        Path.Combine(folder, $".yt2mp3-{videoId.Value}-{Guid.NewGuid():N}{extension}");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file is not worth failing the download for.
        }
    }

    /// <summary>
    /// Removes characters that are invalid on Windows, Linux or macOS file systems.
    /// Path.GetInvalidFileNameChars() is not enough: on Linux it only reports '/' and NUL,
    /// which produces names that break on NTFS/exFAT drives.
    /// </summary>
    internal static string SanitizeFileName(string title, string fallback)
    {
        const string invalidCharacters = "<>:\"/\\|?*";

        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
        {
            if (!char.IsControl(character) && !invalidCharacters.Contains(character))
                builder.Append(character);
        }

        var name = builder.ToString().Trim().TrimEnd('.');

        // Most file systems cap a name at 255 bytes, and the extension still has to fit.
        while (Encoding.UTF8.GetByteCount(name) > MaxFileNameBytes)
        {
            var cut = char.IsLowSurrogate(name[^1]) && name.Length > 1 ? 2 : 1;
            name = name[..^cut];
        }

        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? fallback : name;
    }

    private sealed class ScaledProgress : IProgress<double>
    {
        private readonly IProgress<double> _inner;
        private readonly double _scale;

        public ScaledProgress(IProgress<double> inner, double scale)
        {
            _inner = inner;
            _scale = scale;
        }

        public void Report(double value) => _inner.Report(value * _scale);
    }
}
