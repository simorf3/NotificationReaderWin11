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

            // Pre-populate the per-app list from notifications already sitting in
            // Action Center, so the "Apps" tab isn't empty on first run and the
            // user doesn't have to wait for each app to notify again.
            await SeedKnownAppsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to initialize notification listener.", ex);
            IsListening = false;
        }
    }

    /// <summary>
    /// Reads notifications currently in Action Center and records the apps that
    /// posted them, so they appear in Settings &gt; Apps without waiting for a new
    /// notification. Best-effort: any failure is logged and ignored.
    /// </summary>
    private async Task SeedKnownAppsAsync()
    {
        try
        {
            if (_listener == null)
            {
                return;
            }

            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (current == null)
            {
                return;
            }

            bool added = false;
            foreach (var n in current)
            {
                string appName = string.Empty;
                try
                {
                    appName = n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
                }
                catch { /* some apps expose no AppInfo */ }

                if (RememberAppInternal(appName))
                {
                    added = true;
                }
            }

            if (added)
            {
                await _settingsService.SaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to seed known apps from Action Center.", ex);
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

            var textParts = ExtractTextParts(notification);
            string fullText = string.Join(". ", textParts);

            if (string.IsNullOrWhiteSpace(fullText) && string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            // Remember this app so the user can choose to mute it later in Settings.
            RememberApp(appName);

            // Per-app mute: if the user has switched this app off, don't read it.
            if (IsAppMuted(appName))
            {
                return;
            }

            // Filtering still sees the app name and the complete text so filter
            // rules keyed on either keep working.
            if (!_filterService.ShouldSpeak(appName, fullText))
            {
                return;
            }

            string speech = BuildSpokenText(appName, textParts);
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

    /// <summary>True if the user has muted this app (case-insensitive).</summary>
    private bool IsAppMuted(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        var muted = _settingsService.Settings.MutedApps;
        return muted != null &&
               muted.Any(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Records an app the first time it sends a notification, so it appears in the
    /// per-app list in Settings. Persists in the background only when a genuinely
    /// new app is seen (to avoid frequent disk writes).
    /// </summary>
    private void RememberApp(string appName)
    {
        if (RememberAppInternal(appName))
        {
            // Fire-and-forget save; failures are logged inside SaveAsync.
            _ = _settingsService.SaveAsync();
        }
    }

    /// <summary>
    /// Adds an app to KnownApps if not already present. Returns true if it was a
    /// genuinely new app (so the caller can decide when to persist).
    /// </summary>
    private bool RememberAppInternal(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        var known = _settingsService.Settings.KnownApps ??= new System.Collections.Generic.List<string>();
        if (known.Any(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        known.Add(appName);
        return true;
    }

    private static System.Collections.Generic.List<string> ExtractTextParts(UserNotification notification)
    {
        var result = new System.Collections.Generic.List<string>();
        try
        {
            NotificationVisual? visual = notification.Notification?.Visual;
            if (visual == null)
            {
                return result;
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
                result = binding.GetTextElements()
                    .Select(t => t.Text)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to extract notification text.", ex);
        }

        return result;
    }

    /// <summary>
    /// Builds the text that is actually spoken from a notification's text elements.
    ///
    /// Two deliberate choices:
    ///   * The <b>app name is never spoken</b> — the user can tell which app it is from
    ///     the message itself, so announcing "WhatsApp:" / "Outlook:" every time is noise.
    ///   * For <b>WhatsApp group chats</b>, the group name is skipped. A WhatsApp toast is
    ///     laid out as [conversation title, message body]. In a group the body is prefixed
    ///     with the sender ("Sender: message"), so the sender is already spoken and the
    ///     group name in the title is redundant — we drop it. In a one-to-one chat the body
    ///     has no sender prefix, so the title (the contact's name) is kept so you still know
    ///     who messaged.
    ///
    /// Limitation: a one-to-one message whose text itself starts with "short phrase: " can
    /// be misread as a group message (dropping the contact name). This is uncommon.
    /// </summary>
    private static string BuildSpokenText(string appName, System.Collections.Generic.List<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        bool isWhatsApp = appName.IndexOf("whatsapp", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isWhatsApp && parts.Count >= 2)
        {
            string body = parts[1];
            bool looksLikeGroup = System.Text.RegularExpressions.Regex.IsMatch(
                body, @"^[^:\r\n]{1,40}:\s");
            if (looksLikeGroup)
            {
                // Skip the group-name title; speak the sender-prefixed body (and any rest).
                return string.Join(". ", parts.Skip(1));
            }
        }

        return string.Join(". ", parts);
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
