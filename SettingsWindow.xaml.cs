using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;

namespace NotificationReader.Services;

/// <summary>
/// Wraps the WinRT <see cref="SpeechSynthesizer"/>. Speech requests are queued
/// and processed one at a time on a background task so they never overlap.
/// </summary>
public class SpeechService : IDisposable
{
    // Name fragments used by Microsoft's modern neural/natural voices on Windows 11.
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

    private double _rate = 1.0;
    private bool _disposed;

    /// <summary>Master toggle mirrored from settings.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Voices considered "natural"/neural, or all voices as a fallback.</summary>
    public IReadOnlyList<VoiceInformation> NaturalVoices { get; private set; } = Array.Empty<VoiceInformation>();

    /// <summary>The currently selected voice id, if any.</summary>
    public string? CurrentVoiceId { get; private set; }

    public Task InitializeAsync()
    {
        _synthesizer = new SpeechSynthesizer();

        var all = SpeechSynthesizer.AllVoices;
        var natural = all
            .Where(v => NaturalVoiceFragments.Any(frag =>
                v.DisplayName.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        NaturalVoices = natural.Count > 0 ? natural : all.ToList();

        // Default to the first natural voice.
        var first = NaturalVoices.FirstOrDefault();
        if (first != null)
        {
            _synthesizer.Voice = first;
            CurrentVoiceId = first.Id;
        }

        ApplyRate();

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));

        return Task.CompletedTask;
    }

    /// <summary>Selects a voice by its <see cref="VoiceInformation.Id"/>.</summary>
    public void SetVoice(string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return;
        }

        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId);
        if (voice != null)
        {
            try
            {
                _synthesizer.Voice = voice;
                CurrentVoiceId = voice.Id;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to set voice '{voiceId}'.", ex);
            }
        }
    }

    /// <summary>
    /// Sets the relative speech rate. Input uses the app convention
    /// (0.5 slow, 1.0 normal, 1.5 fast) and is mapped onto the WinRT
    /// SpeakingRate range (0.5 - 6.0).
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

                await SynthesizeAndPlayAsync(text).ConfigureAwait(false);
            }
        }
    }

    private async Task SynthesizeAndPlayAsync(string text)
    {
        try
        {
            using SpeechSynthesisStream stream =
                await _synthesizer.SynthesizeTextToStreamAsync(text);

            // Copy the WinRT stream into a managed MemoryStream for SoundPlayer.
            using var netStream = stream.AsStreamForRead();
            using var memory = new MemoryStream();
            await netStream.CopyToAsync(memory).ConfigureAwait(false);
            memory.Position = 0;

            using var player = new SoundPlayer(memory);
            player.PlaySync();
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to synthesize/play speech.", ex);
        }
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
