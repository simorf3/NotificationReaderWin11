using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using NotificationReader.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

// Disambiguate WPF vs WinForms (both enabled project-wide via implicit usings).
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace NotificationReader.Services;

/// <summary>
/// Listens for incoming Windows notifications via the
/// <see cref="UserNotificationListener"/> and forwards them to the speech queue
/// after filtering.
/// </summary>
public class NotificationService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly FilterService _filterService;
    private readonly SpeechService _speechService;

    private UserNotificationListener? _listener;
    private bool _disposed;

    public NotificationService(
        SettingsService settingsService,
        FilterService filterService,
        SpeechService speechService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _speechService = speechService ?? throw new ArgumentNullException(nameof(speechService));
    }

    /// <summary>Whether the app is actively subscribed to notifications.</summary>
    public bool IsListening { get; private set; }

    /// <summary>Master enable flag, mirrored to settings and the speech service.</summary>
    public bool IsEnabled
    {
        get => _settingsService.Settings.IsEnabled;
        set
        {
            _settingsService.Settings.IsEnabled = value;
            _speechService.IsEnabled = value;
        }
    }

    public async Task InitializeAsync()
    {
        // Keep the speech service in sync with persisted enable flag.
        _speechService.IsEnabled = _settingsService.Settings.IsEnabled;

        try
        {
            _listener = UserNotificationListener.Current;

            UserNotificationListenerAccessStatus status =
                await _listener.RequestAccessAsync();

            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                Logger.Log($"Notification access not granted: {status}");
                ShowAccessMessage(status);
                IsListening = false;
                return;
            }

            _listener.NotificationChanged += OnNotificationChanged;
            IsListening = true;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to initialize notification listener.", ex);
            IsListening = false;
        }
    }

    private static void ShowAccessMessage(UserNotificationListenerAccessStatus status)
    {
        string detail = status == UserNotificationListenerAccessStatus.Denied
            ? "Access is currently Denied."
            : "Access has not been granted yet.";

        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                "Notification Reader needs permission to read your notifications.\n\n" +
                detail + "\n\n" +
                "Please enable it in:\n" +
                "Settings > Privacy & security > Notifications\n" +
                "(also confirm notifications are on in Settings > System > Notifications)\n\n" +
                "Then restart Notification Reader.",
                "Notification Access Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private async void OnNotificationChanged(
        UserNotificationListener sender,
        UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added)
        {
            return;
        }

        if (!IsEnabled || !_speechService.IsEnabled)
        {
            return;
        }

        try
        {
            UserNotification? notification =
                await sender.GetNotificationAsync(args.UserNotificationId);

            if (notification == null)
            {
                return;
            }

            string appName = string.Empty;
            try
            {
                appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to read notification app name.", ex);
            }

            string textContent = ExtractText(notification);

            if (string.IsNullOrWhiteSpace(textContent) && string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            if (!_filterService.ShouldSpeak(appName, textContent))
            {
                return;
            }

            string speech = BuildSpeechString(appName, textContent);
            if (!string.IsNullOrWhiteSpace(speech))
            {
                _speechService.Enqueue(speech);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Error handling notification.", ex);
        }
    }

    private static string ExtractText(UserNotification notification)
    {
        try
        {
            NotificationBinding? binding =
                notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);

            // Prefer the parsed binding text elements when available.
            if (binding != null)
            {
                var texts = binding.GetTextElements();
                var parts = texts
                    .Select(t => t.Text)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim());
                string joined = string.Join(". ", parts);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return joined;
                }
            }

            // Fallback: query all <text> elements from the raw XML content.
            XmlDocument? content = notification.Notification?.Content;
            if (content != null)
            {
                var nodes = content.GetElementsByTagName("text");
                var sb = new StringBuilder();
                for (uint i = 0; i < nodes.Length; i++)
                {
                    string? value = nodes.Item(i)?.InnerText;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(". ");
                        }
                        sb.Append(value.Trim());
                    }
                }
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to extract notification text.", ex);
        }

        return string.Empty;
    }

    private static string BuildSpeechString(string appName, string textContent)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return textContent;
        }
        if (string.IsNullOrWhiteSpace(textContent))
        {
            return appName;
        }
        return $"{appName}: {textContent}";
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
            if (_listener != null)
            {
                _listener.NotificationChanged -= OnNotificationChanged;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Error detaching notification listener.", ex);
        }

        IsListening = false;
        GC.SuppressFinalize(this);
    }
}
