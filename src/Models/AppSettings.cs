using System.Collections.Generic;

namespace NotificationReader.Models;

/// <summary>
/// Persisted application settings. Serialized to
/// %LOCALAPPDATA%\NotificationReader\settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Master on/off toggle for reading notifications aloud.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The selected voice's <see cref="NotificationReader.Models.VoiceOption.Id"/>. For online
    /// neural voices this is "online:{shortName}"; for local voices it is the WinRT voice id.
    /// </summary>
    public string SelectedVoiceId { get; set; } = string.Empty;

    /// <summary>Relative speech rate. 0.5 = slow, 1.0 = normal, 1.5 = fast.</summary>
    public double SpeechRate { get; set; } = 1.0;

    /// <summary>
    /// Playback volume as a percentage. 100 = normal, values above 100 amplify
    /// (louder than the source, via a soft-clipping limiter). Range 0-400.
    /// Defaults to 200 so speech is clearly audible out of the box.
    /// </summary>
    public int Volume { get; set; } = 200;

    /// <summary>User-defined regex filter rules.</summary>
    public List<FilterRule> FilterRules { get; set; } = new();

    /// <summary>
    /// App display-names the user has chosen NOT to have read aloud. Any app not
    /// in this list is read (subject to the filter rules). Populated from the
    /// "Apps" tab in Settings.
    /// </summary>
    public List<string> MutedApps { get; set; } = new();

    /// <summary>
    /// App display-names seen so far (i.e. apps that have sent a notification while
    /// the app was running). Used to populate the per-app list in Settings so the
    /// user can pick which ones to mute.
    /// </summary>
    public List<string> KnownApps { get; set; } = new();
}
