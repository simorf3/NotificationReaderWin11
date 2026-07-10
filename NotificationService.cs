using System;
using System.IO;

namespace NotificationReader.Services;

/// <summary>
/// Very small file logger. Writes to
/// %LOCALAPPDATA%\NotificationReader\error.log. All failures are swallowed so
/// logging never crashes the app.
/// </summary>
public static class Logger
{
    private static readonly object SyncRoot = new();

    private static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NotificationReader");

    private static string LogPath => Path.Combine(LogDirectory, "error.log");

    public static void Log(string message, Exception? ex = null)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex != null)
                {
                    line += Environment.NewLine + ex;
                }
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never let logging throw.
        }
    }
}
