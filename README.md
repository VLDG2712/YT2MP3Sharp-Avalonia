YT2MP3Sharp is an app built by me as there are not to many out there :wink:.

This is just an UI written in C# with AvaloniaUI, YoutubeExplode libraries and ffmpeg as dependency.

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


Download ffmpeg from [here](https://www.ffmpeg.org/download.html).

