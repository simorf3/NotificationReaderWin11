using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NotificationReader.Models;

namespace NotificationReader.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> to
/// %LOCALAPPDATA%\NotificationReader\settings.json.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public AppSettings Settings { get; private set; } = new();

    public string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NotificationReader");

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public async Task InitializeAsync()
    {
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializes settings from disk. Falls back to defaults if the file is
    /// missing or corrupt. Invalid regex patterns are dropped.
    /// </summary>
    public async Task LoadAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Settings = new AppSettings();
                return;
            }

            string json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                Settings = new AppSettings();
                return;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            Settings = loaded ?? new AppSettings();

            // Validate rules: drop any with an invalid regex pattern.
            if (Settings.FilterRules != null)
            {
                Settings.FilterRules.RemoveAll(rule =>
                {
                    if (string.IsNullOrEmpty(rule.Pattern))
                    {
                        return false; // empty pattern is allowed (matches nothing meaningful, but keep)
                    }
                    try
                    {
                        _ = new Regex(rule.Pattern);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        Logger.Log($"Dropping filter rule '{rule.Name}' with invalid regex: {rule.Pattern}");
                        return true;
                    }
                });
            }
            else
            {
                Settings.FilterRules = new();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load settings; using defaults.", ex);
            Settings = new AppSettings();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>Serializes the current settings to disk with indented JSON.</summary>
    public async Task SaveAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            string tempPath = SettingsPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            // Atomic-ish replace.
            if (File.Exists(SettingsPath))
            {
                File.Replace(tempPath, SettingsPath, null);
            }
            else
            {
                File.Move(tempPath, SettingsPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to save settings.", ex);
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
