YT2MP3Sharp is an app built by me as there are not to many out there :wink:
This is just an UI written in C# with AvaloniaUI, YoutubeExplode libraries and ffmpeg as dependency

To build do:
- ```dotnet restore```
- ```dotnet build -c Release```
- go to Release folder and create a folder structure like this:
  ```
  Resources/
  |
    ffmpeg/
      |
        ffmpeg or ffmpeg.exe (depending on target ur using)
  ```
<h4>!Tip! to build on Windows, you have to modify the line where the process is executed, from 'ffmpeg' to 'ffmpeg.exe' like this:</h4>

```var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ffmpeg", "ffmpeg");```

to

```var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ffmpeg", "ffmpeg.exe");```

Download ffmpeg from [here](https://www.ffmpeg.org/download.html).

