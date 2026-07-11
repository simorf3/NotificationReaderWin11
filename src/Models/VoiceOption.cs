using System;
using System.Collections.Generic;

namespace NotificationReader.Models;

/// <summary>
/// A single selectable voice, unifying two very different sources:
///   * <b>Online neural</b> voices (Microsoft Edge "natural" AI voices) reached over the
///     internet — high quality, but require a connection.
///   * <b>Local</b> voices installed on the machine (SAPI/OneCore) — always available offline.
/// </summary>
public class VoiceOption
{
    /// <summary>
    /// Stable unique id persisted in settings. Online voices use "online:{shortName}"
    /// (e.g. "online:en-US-AriaNeural"); local voices use the raw WinRT voice id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Friendly name shown in the menu.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>True for online neural (AI) voices.</summary>
    public bool IsOnline { get; set; }

    /// <summary>For online voices, the engine short name, e.g. "en-US-AriaNeural".</summary>
    public string? OnlineShortName { get; set; }

    public static VoiceOption Online(string shortName, string displayName) => new()
    {
        Id = "online:" + shortName,
        DisplayName = displayName,
        IsOnline = true,
        OnlineShortName = shortName
    };

    public static VoiceOption Local(string winRtVoiceId, string displayName) => new()
    {
        Id = winRtVoiceId,
        DisplayName = displayName,
        IsOnline = false
    };
}

/// <summary>
/// Curated catalog of Microsoft's free online neural voices (English locales). These are
/// the same "natural" AI voices used by Narrator/Edge Read-aloud. The list is static
/// because the endpoint's voice set is stable; keeping it local avoids a network call
/// just to populate the menu.
/// </summary>
public static class OnlineVoiceCatalog
{
    /// <summary>(shortName, friendlyName) pairs, roughly ordered by popularity.</summary>
    private static readonly (string Short, string Friendly)[] Voices =
    {
        ("en-US-AriaNeural",              "Aria \u2014 US female (AI)"),
        ("en-US-JennyNeural",             "Jenny \u2014 US female (AI)"),
        ("en-US-AvaMultilingualNeural",   "Ava \u2014 US female (AI)"),
        ("en-US-EmmaMultilingualNeural",  "Emma \u2014 US female (AI)"),
        ("en-US-MichelleNeural",          "Michelle \u2014 US female (AI)"),
        ("en-US-AnaNeural",               "Ana \u2014 US female, young (AI)"),
        ("en-US-GuyNeural",               "Guy \u2014 US male (AI)"),
        ("en-US-AndrewMultilingualNeural","Andrew \u2014 US male (AI)"),
        ("en-US-BrianMultilingualNeural", "Brian \u2014 US male (AI)"),
        ("en-US-ChristopherNeural",       "Christopher \u2014 US male (AI)"),
        ("en-US-EricNeural",              "Eric \u2014 US male (AI)"),
        ("en-US-RogerNeural",             "Roger \u2014 US male (AI)"),
        ("en-US-SteffanNeural",           "Steffan \u2014 US male (AI)"),
        ("en-GB-SoniaNeural",             "Sonia \u2014 UK female (AI)"),
        ("en-GB-LibbyNeural",             "Libby \u2014 UK female (AI)"),
        ("en-GB-MaisieNeural",            "Maisie \u2014 UK female, young (AI)"),
        ("en-GB-RyanNeural",              "Ryan \u2014 UK male (AI)"),
        ("en-GB-ThomasNeural",            "Thomas \u2014 UK male (AI)"),
        ("en-AU-NatashaNeural",           "Natasha \u2014 Australian female (AI)"),
        ("en-AU-WilliamMultilingualNeural","William \u2014 Australian male (AI)"),
        ("en-CA-ClaraNeural",             "Clara \u2014 Canadian female (AI)"),
        ("en-CA-LiamNeural",              "Liam \u2014 Canadian male (AI)"),
        ("en-IE-EmilyNeural",             "Emily \u2014 Irish female (AI)"),
        ("en-IE-ConnorNeural",            "Connor \u2014 Irish male (AI)"),
    };

    /// <summary>The default online voice used on first run.</summary>
    public const string DefaultShortName = "en-US-AriaNeural";

    public static IReadOnlyList<VoiceOption> All()
    {
        var list = new List<VoiceOption>(Voices.Length);
        foreach (var (shortName, friendly) in Voices)
        {
            list.Add(VoiceOption.Online(shortName, friendly));
        }
        return list;
    }
}
