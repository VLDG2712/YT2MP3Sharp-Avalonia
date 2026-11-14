# YT2MP3Sharp

A small desktop app that downloads YouTube videos and playlists as MP3 or MP4. I built it because there are not too many simple ones out there :wink:.

It is a UI written in C# ([.NET](https://dotnet.microsoft.com/en-us/)) with [AvaloniaUI](https://github.com/AvaloniaUI/Avalonia), using [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) for the downloads and [ffmpeg](https://www.ffmpeg.org/) for the conversion.

## Features

- Single videos or whole playlists (three downloads at a time)
- MP3 (320 kbps) or MP4, with a quality picker for single MP4 downloads
- Progress bar, cancel button and a log you can copy from
- Remembers your save folder and format

## Download

Get the latest build from the [releases page](https://github.com/VLDG2712/YT2MP3Sharp-Avalonia/releases/latest). The .NET runtime is included, so there is nothing else to install.

| System | File | ffmpeg |
| --- | --- | --- |
| Windows x64 | [YT2MP3Sharp-win-x64.zip](https://github.com/VLDG2712/YT2MP3Sharp-Avalonia/releases/latest/download/YT2MP3Sharp-win-x64.zip) | included |
| Linux x64 | [YT2MP3Sharp-linux-x64.tar.gz](https://github.com/VLDG2712/YT2MP3Sharp-Avalonia/releases/latest/download/YT2MP3Sharp-linux-x64.tar.gz) | install it yourself |
| Linux ARM64 | [YT2MP3Sharp-linux-arm64.tar.gz](https://github.com/VLDG2712/YT2MP3Sharp-Avalonia/releases/latest/download/YT2MP3Sharp-linux-arm64.tar.gz) | install it yourself |

### Windows

Unzip and run `YT2MP3Sharp.exe`.

### Linux

Install ffmpeg with your package manager, then unpack and run:

```sh
sudo pacman -S ffmpeg        # Arch
sudo apt install ffmpeg      # Debian / Ubuntu

tar -xzf YT2MP3Sharp-linux-x64.tar.gz
./YT2MP3Sharp/YT2MP3Sharp
```

## ffmpeg

The app looks for ffmpeg next to its own executable first and then on your `PATH`, and uses the first one that actually runs. If none is found it tells you in the log. You can download ffmpeg from [here](https://www.ffmpeg.org/download.html).

## Settings

Your save folder and format are stored in `%AppData%\YT2MP3Sharp\settings.json` on Windows and `~/.config/YT2MP3Sharp/settings.json` on Linux.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and ffmpeg.

```sh
dotnet build YT2MP3Sharp.csproj -c Release
dotnet run --project YT2MP3Sharp.csproj -c Release
```

## Releasing

Change `<Version>` in `YT2MP3Sharp.csproj` and push to `main`. The GitHub workflow builds all three packages and publishes them as a release with that version number.

## License

BSD 3-Clause, see [LICENSE](LICENSE).
