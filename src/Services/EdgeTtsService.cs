using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotificationReader.Services;

/// <summary>
/// Synthesizes speech using Microsoft's free online neural ("natural") text-to-speech
/// engine — the very same engine Microsoft Edge's "Read aloud" feature uses. This makes
/// the high-quality AI voices (Aria, Guy, Jenny, Andrew, Ryan, Sonia, ...) available to
/// this app, which is otherwise impossible: those neural voices are NOT exposed to
/// third-party apps through any local Windows API.
///
/// No API key or account is required. It does, however, need an internet connection —
/// callers should fall back to a local (offline) voice if synthesis throws.
///
/// Protocol reference: the well-established open-source "edge-tts" project.
/// </summary>
public static class EdgeTtsService
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumFullVersion = "143.0.3650.75";
    private static string SecMsGecVersion => $"1-{ChromiumFullVersion}";

    private const string WssBaseUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
        "?TrustedClientToken=" + TrustedClientToken;

    // Windows FILETIME epoch (1601-01-01) expressed as seconds before the Unix epoch.
    private const double WinEpoch = 11644473600.0;

    /// <summary>
    /// Synthesizes <paramref name="text"/> with the given online neural voice and returns
    /// the audio as a 48 kbps mono MP3 byte array. Throws on network/protocol failure so
    /// the caller can fall back to an offline voice.
    /// </summary>
    /// <param name="voiceShortName">e.g. "en-US-AriaNeural".</param>
    /// <param name="rate">SSML prosody rate, e.g. "+0%", "-20%", "+50%".</param>
    public static async Task<byte[]> SynthesizeAsync(
        string text,
        string voiceShortName,
        string rate = "+0%",
        string pitch = "+0Hz",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        string sec = GenerateSecMsGec();
        string connectionId = Guid.NewGuid().ToString("N");
        string url =
            $"{WssBaseUrl}&ConnectionId={connectionId}" +
            $"&Sec-MS-GEC={sec}&Sec-MS-GEC-Version={SecMsGecVersion}";

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");

        await ws.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        // 1) Send the synthesis configuration.
        string timestamp = DateToString();
        string configMessage =
            $"X-Timestamp:{timestamp}\r\n" +
            "Content-Type:application/json; charset=utf-8\r\n" +
            "Path:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
            "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
            "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        await SendTextAsync(ws, configMessage, cancellationToken).ConfigureAwait(false);

        // 2) Send the SSML request.
        string requestId = Guid.NewGuid().ToString("N");
        string ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{voiceShortName}'>" +
            $"<prosody pitch='{pitch}' rate='{rate}' volume='{volume}'>" +
            $"{Escape(text)}</prosody></voice></speak>";
        string ssmlMessage =
            $"X-RequestId:{requestId}\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{timestamp}Z\r\n" +
            "Path:ssml\r\n\r\n" +
            ssml;
        await SendTextAsync(ws, ssmlMessage, cancellationToken).ConfigureAwait(false);

        // 3) Receive audio frames until "turn.end".
        using var audio = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                                 .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return audio.ToArray();
                }
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            byte[] data = message.ToArray();

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string txt = Encoding.UTF8.GetString(data);
                if (txt.Contains("Path:turn.end"))
                {
                    break;
                }
            }
            else // Binary audio frame: [2-byte big-endian header length][header][audio].
            {
                if (data.Length < 2)
                {
                    continue;
                }
                int headerLength = (data[0] << 8) | data[1];
                int audioStart = 2 + headerLength;
                if (audioStart < data.Length)
                {
                    audio.Write(data, audioStart, data.Length - audioStart);
                }
            }
        }

        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch
        {
            // Closing is best-effort; the audio has already been received.
        }

        return audio.ToArray();
    }

    private static async Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Generates the "Sec-MS-GEC" token required by the endpoint: SHA-256 of the current
    /// Windows FILETIME (rounded down to the nearest 5 minutes) concatenated with the
    /// trusted client token, returned as an uppercase hex string.
    /// </summary>
    private static string GenerateSecMsGec()
    {
        double ticks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        ticks += WinEpoch;
        ticks -= ticks % 300;            // round down to 5-minute boundary
        ticks *= 1_000_000_000.0 / 100;  // seconds -> 100-nanosecond intervals

        string toHash = ticks.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                        + TrustedClientToken;
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(toHash));
        return Convert.ToHexString(hash); // uppercase
    }

    private static string DateToString() =>
        DateTime.UtcNow.ToString(
            "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
