using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NotificationReader.Models;
using Windows.Media.SpeechSynthesis;

namespace NotificationReader.Services;

/// <summary>
/// Turns notification text into speech. Two voice sources are supported and presented as
/// one unified list:
///   * <b>Online neural</b> voices (the Microsoft Edge "natural" AI voices) via
///     <see cref="EdgeTtsService"/> — high quality, need an internet connection.
///   * <b>Local</b> voices via the WinRT <see cref="SpeechSynthesizer"/> — always work offline.
///
/// Requests are queued and processed one at a time on a background task so they never
/// overlap. If an online voice fails (e.g. no internet), we transparently fall back to a
/// local voice so notifications are still read aloud.
/// </summary>
public class SpeechService : IDisposable
{
    // Name fragments used by Microsoft's modern neural/natural local voices on Windows 11.
    private static readonly string[] NaturalVoiceFragments =
    {
        "Natural", "Jenny", "Aria", "Guy", "Davis", "Ana", "Michelle",
        "Andrew", "Brian", "Emma", "Ryan", "Sonia", "Libby", "Mia",
        "Leah", "Alfie", "Olivia", "Abbi", "Bella", "Hollie", "Abbie",
        "Ava", "Steffan"
    };

    private SpeechSynthesizer _synthesizer = null!;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private CancellationTokenSource _cts = new();
    private Task? _worker;

    // Maps a local VoiceOption.Id to its underlying WinRT voice.
    private readonly Dictionary<string, VoiceInformation> _localVoices = new();
    private VoiceInformation? _fallbackLocalVoice;

    private VoiceOption? _currentVoice;
    private double _rate = 1.0;
    // Playback gain multiplier. 1.0 = source volume; >1.0 amplifies (louder).
    private float _gain = 2.0f;
    private bool _disposed;

    // Voice preview (hover-to-hear) runs outside the notification queue and is
    // cancelled whenever the user hovers a different voice or moves away.
    private CancellationTokenSource? _previewCts;
    private readonly object _previewLock = new();

    /// <summary>Master toggle mirrored from settings.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>All selectable voices: online neural voices first, then local voices.</summary>
    public IReadOnlyList<VoiceOption> Voices { get; private set; } = Array.Empty<VoiceOption>();

    /// <summary>The currently selected voice id, if any.</summary>
    public string? CurrentVoiceId => _currentVoice?.Id;

    public Task InitializeAsync()
    {
        _synthesizer = new SpeechSynthesizer();

        // ---- Local (offline) voices -------------------------------------------------
        var localWinRt = SpeechSynthesizer.AllVoices ?? (IReadOnlyList<VoiceInformation>)Array.Empty<VoiceInformation>();
        var localOrdered = localWinRt
            .OrderByDescending(v => IsNatural(v.DisplayName))
            .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var localOptions = new List<VoiceOption>();
        foreach (var v in localOrdered)
        {
            _localVoices[v.Id] = v;
            localOptions.Add(VoiceOption.Local(v.Id, v.DisplayName + "  (offline)"));
        }

        // Best local voice to fall back to when an online voice can't be reached.
        _fallbackLocalVoice = localOrdered.FirstOrDefault(v => IsNatural(v.DisplayName))
                              ?? _synthesizer.Voice
                              ?? localOrdered.FirstOrDefault();

        // ---- Online neural (AI) voices ---------------------------------------------
        var onlineOptions = OnlineVoiceCatalog.All();

        // Unified list: AI voices first (that's what the user wants front-and-centre).
        Voices = onlineOptions.Concat(localOptions).ToList();

        // Default selection: the flagship online AI voice. Synthesis falls back to a
        // local voice automatically if there's no internet, so this is safe.
        _currentVoice = Voices.FirstOrDefault(v => v.IsOnline && v.OnlineShortName == OnlineVoiceCatalog.DefaultShortName)
                        ?? Voices.FirstOrDefault();
        ApplyLocalVoiceIfNeeded();
        ApplyRate();

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));

        return Task.CompletedTask;
    }

    /// <summary>True if the display name looks like a modern natural/neural voice.</summary>
    private static bool IsNatural(string displayName) =>
        !string.IsNullOrEmpty(displayName) &&
        NaturalVoiceFragments.Any(frag =>
            displayName.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>Selects a voice by its <see cref="VoiceOption.Id"/>.</summary>
    public void SetVoice(string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return;
        }

        var match = Voices.FirstOrDefault(v => v.Id == voiceId);
        if (match == null)
        {
            return;
        }

        _currentVoice = match;
        ApplyLocalVoiceIfNeeded();
    }

    private void ApplyLocalVoiceIfNeeded()
    {
        // For local voices, point the WinRT synthesizer at the selected voice now.
        if (_currentVoice is { IsOnline: false } &&
            _localVoices.TryGetValue(_currentVoice.Id, out var winRtVoice))
        {
            try
            {
                _synthesizer.Voice = winRtVoice;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to set local voice '{_currentVoice.Id}'.", ex);
            }
        }
    }

    /// <summary>
    /// Sets the relative speech rate. Input uses the app convention
    /// (0.5 slow, 1.0 normal, 1.5 fast).
    /// </summary>
    public void SetRate(double rate)
    {
        _rate = rate;
        ApplyRate();
    }

    private void ApplyRate()
    {
        if (_synthesizer == null)
        {
            return;
        }

        // App convention rate maps directly onto WinRT SpeakingRate (which supports 0.5 - 6.0).
        double winrtRate = _rate;
        if (winrtRate < 0.5) winrtRate = 0.5;
        if (winrtRate > 6.0) winrtRate = 6.0;

        try
        {
            _synthesizer.Options.SpeakingRate = winrtRate;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to apply speaking rate.", ex);
        }
    }

    /// <summary>
    /// Sets the playback volume as a percentage (0-200). 100 = source volume;
    /// above 100 amplifies so speech can be made louder than the raw audio.
    /// </summary>
    public void SetVolume(int percent)
    {
        if (percent < 0) percent = 0;
        if (percent > MaxVolumePercent) percent = MaxVolumePercent;
        _gain = percent / 100f;
    }

    /// <summary>Maximum volume percentage the slider allows (amplification headroom).</summary>
    public const int MaxVolumePercent = 400;

    /// <summary>Converts the app rate (0.5-1.5) into an SSML prosody rate like "+0%".</summary>
    private string OnlineRateString()
    {
        int pct = (int)Math.Round((_rate - 1.0) * 100.0);
        if (pct > 100) pct = 100;
        if (pct < -50) pct = -50;
        return (pct >= 0 ? "+" : "") + pct + "%";
    }

    /// <summary>Queues text to be spoken.</summary>
    public void Enqueue(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _disposed)
        {
            return;
        }

        _queue.Enqueue(text);
        _signal.Release();
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_queue.TryDequeue(out var text))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await SpeakAsync(text, token).ConfigureAwait(false);
            }
        }
    }

    private async Task SpeakAsync(string text, CancellationToken token)
    {
        var voice = _currentVoice;

        // Online neural voice: synthesize over the network, then play the MP3. If anything
        // goes wrong (no internet, endpoint error) fall back to a local voice.
        if (voice is { IsOnline: true, OnlineShortName: { } shortName })
        {
            try
            {
                byte[] mp3 = await EdgeTtsService
                    .SynthesizeAsync(text, shortName, OnlineRateString(), cancellationToken: token)
                    .ConfigureAwait(false);

                if (mp3.Length > 0)
                {
                    PlayMp3(mp3);
                    return;
                }

                Logger.Log("Online voice returned no audio; falling back to a local voice.");
            }
            catch (Exception ex)
            {
                Logger.Log("Online voice failed (offline?); falling back to a local voice.", ex);
            }

            await SpeakWithLocalFallbackAsync(text).ConfigureAwait(false);
            return;
        }

        // Local (offline) voice.
        await SpeakLocalAsync(text).ConfigureAwait(false);
    }

    private async Task SpeakWithLocalFallbackAsync(string text)
    {
        try
        {
            if (_fallbackLocalVoice != null)
            {
                _synthesizer.Voice = _fallbackLocalVoice;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to select fallback local voice.", ex);
        }

        await SpeakLocalAsync(text).ConfigureAwait(false);
    }

    private async Task SpeakLocalAsync(string text)
    {
        try
        {
            using SpeechSynthesisStream stream =
                await _synthesizer.SynthesizeTextToStreamAsync(text);

            using var netStream = stream.AsStreamForRead();
            using var memory = new MemoryStream();
            await netStream.CopyToAsync(memory).ConfigureAwait(false);
            memory.Position = 0;

            // The WinRT stream is WAV; play through NAudio so the volume slider
            // (which can amplify beyond 100%) applies to local voices too.
            using var reader = new WaveFileReader(memory);
            PlayWithGain(reader, _gain, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to synthesize/play local speech.", ex);
        }
    }

    /// <summary>Decodes and plays an MP3 byte array synchronously (via NAudio).</summary>
    private void PlayMp3(byte[] mp3, CancellationToken token = default)
    {
        using var ms = new MemoryStream(mp3);
        using var reader = new Mp3FileReader(ms);
        PlayWithGain(reader, _gain, token);
    }

    /// <summary>
    /// Plays a decoded audio stream synchronously, applying the gain multiplier.
    /// A soft-clipping limiter (tanh) is used so gains above 100% raise the
    /// perceived loudness substantially without the harsh distortion that plain
    /// multiplication would cause when the signal exceeds full scale.
    /// </summary>
    private static void PlayWithGain(WaveStream reader, float gain, CancellationToken token)
    {
        var sampleProvider = reader.ToSampleProvider();
        var loud = new LoudnessSampleProvider(sampleProvider, gain);

        using var output = new WaveOutEvent();
        output.Init(loud);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
        {
            if (token.IsCancellationRequested)
            {
                try { output.Stop(); } catch { /* ignore */ }
                break;
            }
            Thread.Sleep(40);
        }
    }

    /// <summary>
    /// Applies gain then a tanh soft-clip. Small/quiet samples are boosted almost
    /// linearly; loud peaks saturate gently toward ±1.0 instead of clipping hard.
    /// This is a simple loudness maximiser that makes speech noticeably louder.
    /// </summary>
    private sealed class LoudnessSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _gain;

        public LoudnessSampleProvider(ISampleProvider source, float gain)
        {
            _source = source;
            _gain = gain;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                float s = buffer[offset + i] * _gain;
                // tanh soft-clip keeps output within [-1, 1] with a smooth knee.
                buffer[offset + i] = (float)Math.Tanh(s);
            }
            return read;
        }
    }

    /// <summary>
    /// Plays a short spoken sample of the given voice, for the hover-to-preview
    /// feature. Runs independently of the notification queue and never changes the
    /// user's selected voice. A new preview cancels any in-progress one.
    /// </summary>
    public async Task PreviewVoiceAsync(string voiceId)
    {
        var voice = Voices.FirstOrDefault(v => v.Id == voiceId);
        if (voice == null || _disposed)
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_previewLock)
        {
            try { _previewCts?.Cancel(); } catch { /* ignore */ }
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            cts = _previewCts;
        }
        var token = cts.Token;

        string cleanName = StripVoiceSuffix(voice.DisplayName);
        string sample = $"Hello, this is {cleanName}. This is how I sound.";

        try
        {
            if (voice is { IsOnline: true, OnlineShortName: { } shortName })
            {
                byte[] mp3 = await EdgeTtsService
                    .SynthesizeAsync(sample, shortName, OnlineRateString(), cancellationToken: token)
                    .ConfigureAwait(false);

                if (mp3.Length > 0 && !token.IsCancellationRequested)
                {
                    await Task.Run(() => PlayMp3(mp3, token), token).ConfigureAwait(false);
                }
            }
            else if (_localVoices.TryGetValue(voice.Id, out var winRtVoice))
            {
                // Use a throwaway synthesizer so the shared one (and the user's
                // selected voice) is left untouched.
                using var synth = new SpeechSynthesizer { Voice = winRtVoice };
                try { synth.Options.SpeakingRate = Math.Clamp(_rate, 0.5, 6.0); } catch { /* ignore */ }

                using SpeechSynthesisStream stream = await synth.SynthesizeTextToStreamAsync(sample);
                using var netStream = stream.AsStreamForRead();
                using var memory = new MemoryStream();
                await netStream.CopyToAsync(memory, token).ConfigureAwait(false);
                memory.Position = 0;

                if (!token.IsCancellationRequested)
                {
                    await Task.Run(() =>
                    {
                        using var reader = new WaveFileReader(memory);
                        PlayWithGain(reader, _gain, token);
                    }, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { /* superseded by a newer preview */ }
        catch (Exception ex)
        {
            Logger.Log($"Voice preview failed for '{voiceId}'.", ex);
        }
    }

    /// <summary>Stops any in-progress voice preview.</summary>
    public void CancelPreview()
    {
        lock (_previewLock)
        {
            try { _previewCts?.Cancel(); } catch { /* ignore */ }
        }
    }

    /// <summary>Strips the "(offline)" / "(online)" suffixes for a clean spoken name.</summary>
    private static string StripVoiceSuffix(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "this voice";
        }

        int paren = displayName.IndexOf('(');
        string name = paren > 0 ? displayName.Substring(0, paren) : displayName;
        return name.Trim();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            _cts.Cancel();
            // Wake the worker if it is waiting.
            _signal.Release();
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Logger.Log("Error stopping speech worker.", ex);
        }
        finally
        {
            _cts.Dispose();
            _signal.Dispose();
            _synthesizer?.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
