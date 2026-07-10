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

    /// <summary>The selected voice's <see cref="Windows.Media.SpeechSynthesis.VoiceInformation.Id"/>.</summary>
    public string SelectedVoiceId { get; set; } = string.Empty;

    /// <summary>Relative speech rate. 0.5 = slow, 1.0 = normal, 1.5 = fast.</summary>
    public double SpeechRate { get; set; } = 1.0;

    /// <summary>User-defined regex filter rules.</summary>
    public List<FilterRule> FilterRules { get; set; } = new();
}
