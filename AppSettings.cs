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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YT2MP3Sharp;

/// <summary>
/// User settings, stored as JSON in the per-user config folder
/// (~/.config/YT2MP3Sharp on Linux, %AppData%\YT2MP3Sharp on Windows).
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? OutputFolder { get; set; }

    public MediaFormat Format { get; set; } = MediaFormat.Mp3;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YT2MP3Sharp",
        "settings.json");

    /// <summary>Older versions kept only the save folder, in a file next to the executable.</summary>
    private static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.dat");

    /// <summary>Never throws: unreadable or missing settings simply give the defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();

            if (File.Exists(LegacySettingsPath))
            {
                var folder = File.ReadAllText(LegacySettingsPath).Trim();
                return new AppSettings { OutputFolder = folder.Length == 0 ? null : folder };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall through to the defaults.
        }

        return new AppSettings();
    }

    /// <summary>Returns an error message when the settings could not be written, otherwise null.</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
