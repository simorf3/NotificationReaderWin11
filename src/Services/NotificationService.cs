using System;
using System.Linq;
using System.Windows;
using NotificationReader.Models;
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

    private void OnNotificationChanged(
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
            // GetNotification is synchronous in the WinRT API (there is no
            // GetNotificationAsync overload). It returns the notification for
            // the given id, or null if it is no longer available.
            UserNotification? notification =
                sender.GetNotification(args.UserNotificationId);

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
            NotificationVisual? visual = notification.Notification?.Visual;
            if (visual == null)
            {
                return string.Empty;
            }

            // Prefer the standard ToastGeneric binding; fall back to the first
            // available binding if the toast uses a different template. The
            // WinRT Notification type does not expose the raw XML, so all text
            // is read through the visual bindings' text elements.
            NotificationBinding? binding =
                visual.GetBinding(KnownNotificationBindings.ToastGeneric);

            if (binding == null && visual.Bindings != null && visual.Bindings.Count > 0)
            {
                binding = visual.Bindings[0];
            }

            if (binding != null)
            {
                var parts = binding.GetTextElements()
                    .Select(t => t.Text)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim());

                return string.Join(". ", parts);
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
