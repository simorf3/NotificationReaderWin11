using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using NotificationReader.Services;
using NotificationReader.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NotificationReader.UI;

/// <summary>
/// Builds and manages the system tray icon and its context menu. This is the
/// only user-facing surface of the app when idle.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SpeechService _speechService;
    private readonly NotificationService _notificationService;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _toggleItem;
    private ToolStripMenuItem? _voiceMenu;
    private ToolStripMenuItem? _rateMenu;

    private ToolStripMenuItem? _rateSlow;
    private ToolStripMenuItem? _rateNormal;
    private ToolStripMenuItem? _rateFast;

    private ToolStripMenuItem? _volumeMenu;
    private ToolStripMenuItem? _volumeLabel;
    private TrackBar? _volumeTrackBar;

    // Hover-to-preview state.
    private System.Windows.Forms.Timer? _previewTimer;
    private ToolStripMenuItem? _pendingPreviewItem;
    private ToolStripMenuItem? _loadingItem;
    private string? _loadingItemText;
    private const int PreviewHoverDelayMs = 450;

    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public TrayIconManager(
        SettingsService settingsService,
        SpeechService speechService,
        NotificationService notificationService)
    {
        _settingsService = settingsService;
        _speechService = speechService;
        _notificationService = notificationService;
    }

    public Task InitializeAsync()
    {
        _menu = new ContextMenuStrip();

        // Toggle: Reading Notifications
        _toggleItem = new ToolStripMenuItem("Reading Notifications")
        {
            CheckOnClick = true,
            Checked = _settingsService.Settings.IsEnabled
        };
        _toggleItem.Click += OnToggleClicked;
        _menu.Items.Add(_toggleItem);

        _menu.Items.Add(new ToolStripSeparator());

        // Voice submenu
        _voiceMenu = new ToolStripMenuItem("Voice");
        BuildVoiceMenu();
        _menu.Items.Add(_voiceMenu);

        // Speech rate submenu
        _rateMenu = new ToolStripMenuItem("Speech Rate");
        _rateSlow = new ToolStripMenuItem("Slow") { CheckOnClick = false };
        _rateNormal = new ToolStripMenuItem("Normal") { CheckOnClick = false };
        _rateFast = new ToolStripMenuItem("Fast") { CheckOnClick = false };
        _rateSlow.Click += (_, _) => SetRate(0.5);
        _rateNormal.Click += (_, _) => SetRate(1.0);
        _rateFast.Click += (_, _) => SetRate(1.5);
        _rateMenu.DropDownItems.Add(_rateSlow);
        _rateMenu.DropDownItems.Add(_rateNormal);
        _rateMenu.DropDownItems.Add(_rateFast);
        UpdateRateMenu();
        _menu.Items.Add(_rateMenu);

        // Volume submenu (slider)
        _volumeMenu = new ToolStripMenuItem("Volume");
        BuildVolumeMenu();
        _menu.Items.Add(_volumeMenu);

        _menu.Items.Add(new ToolStripSeparator());

        // Filters & Settings
        var settingsItem = new ToolStripMenuItem("Filters && Settings...");
        settingsItem.Click += (_, _) => OpenSettingsWindow();
        _menu.Items.Add(settingsItem);

        _menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            // Assigning the strip gives us native right-click behaviour AND gives
            // the reflection-based ShowContextMenu (used for left-click) something
            // to show. Without this, neither click shows anything.
            ContextMenuStrip = _menu,
            Text = BuildTooltip()
        };
        // Left-click also opens the menu (right-click is handled natively via
        // ContextMenuStrip above, so we only need to trigger it for left-click).
        // MouseUp is the reliable event for NotifyIcon click handling.
        _notifyIcon.MouseUp += OnTrayIconClick;
        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();

        // Startup balloon.
        try
        {
            _notifyIcon.BalloonTipTitle = "Notification Reader";
            _notifyIcon.BalloonTipText = "Notification Reader started";
            _notifyIcon.ShowBalloonTip(3000);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to show startup balloon.", ex);
        }

        return Task.CompletedTask;
    }

    private void BuildVoiceMenu()
    {
        if (_voiceMenu == null)
        {
            return;
        }

        _voiceMenu.DropDownItems.Clear();

        var voices = _speechService.Voices;
        if (voices.Count == 0)
        {
            _voiceMenu.DropDownItems.Add(new ToolStripMenuItem("(No voices found)") { Enabled = false });
            return;
        }

        string? selected = _settingsService.Settings.SelectedVoiceId?.Length > 0
            ? _settingsService.Settings.SelectedVoiceId
            : _speechService.CurrentVoiceId;

        bool addedOnlineHeader = false;
        bool addedLocalHeader = false;

        foreach (var voice in voices)
        {
            // Section headers so the AI (online) voices are clearly separated from the
            // offline ones.
            if (voice.IsOnline && !addedOnlineHeader)
            {
                _voiceMenu.DropDownItems.Add(new ToolStripMenuItem("Natural AI voices (need internet)") { Enabled = false });
                addedOnlineHeader = true;
            }
            else if (!voice.IsOnline && !addedLocalHeader)
            {
                _voiceMenu.DropDownItems.Add(new ToolStripSeparator());
                _voiceMenu.DropDownItems.Add(new ToolStripMenuItem("Offline voices") { Enabled = false });
                addedLocalHeader = true;
            }

            var item = new ToolStripMenuItem(voice.DisplayName)
            {
                Tag = voice.Id,
                Checked = voice.Id == selected
            };
            item.Click += OnVoiceClicked;
            // Hover to hear a short sample of the voice.
            item.MouseEnter += OnVoiceHover;
            item.MouseLeave += OnVoiceHoverLeave;
            _voiceMenu.DropDownItems.Add(item);
        }

        // Stop any preview and clean up indicators when the menu closes.
        _voiceMenu.DropDownClosed -= OnVoiceMenuClosed;
        _voiceMenu.DropDownClosed += OnVoiceMenuClosed;

        // Let the user install more offline voices straight from Windows settings.
        _voiceMenu.DropDownItems.Add(new ToolStripSeparator());
        var addVoices = new ToolStripMenuItem("Add more offline voices\u2026");
        addVoices.Click += (_, _) => OpenSpeechSettings();
        _voiceMenu.DropDownItems.Add(addVoices);
    }

    private static void OpenSpeechSettings()
    {
        try
        {
            // Opens Settings > Time & language > Speech, where extra TTS voices
            // can be added (these become usable by this app).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:speech",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to open Speech settings.", ex);
        }
    }

    private async void OnVoiceClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not string voiceId)
        {
            return;
        }

        _speechService.SetVoice(voiceId);
        _settingsService.Settings.SelectedVoiceId = voiceId;
        UpdateVoiceMenu();
        await _settingsService.SaveAsync();
    }

    // ---- Hover-to-preview ------------------------------------------------------

    private void OnVoiceHover(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not string)
        {
            return;
        }

        // A different voice is now hovered: stop the previous preview and its
        // loading indicator, then (re)start the debounce timer.
        _speechService.CancelPreview();
        ClearLoadingIndicator();
        _pendingPreviewItem = item;

        _previewTimer ??= CreatePreviewTimer();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnVoiceHoverLeave(object? sender, EventArgs e)
    {
        // Only cancel if we're leaving the item we were about to preview.
        if (sender is ToolStripMenuItem item && ReferenceEquals(item, _pendingPreviewItem))
        {
            _pendingPreviewItem = null;
        }
        _previewTimer?.Stop();
        _speechService.CancelPreview();
        ClearLoadingIndicator();
    }

    private void OnVoiceMenuClosed(object? sender, EventArgs e)
    {
        _previewTimer?.Stop();
        _pendingPreviewItem = null;
        _speechService.CancelPreview();
        ClearLoadingIndicator();
    }

    private System.Windows.Forms.Timer CreatePreviewTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = PreviewHoverDelayMs };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var item = _pendingPreviewItem;
            if (item is null || item.Tag is not string voiceId)
            {
                return;
            }

            ShowLoadingIndicator(item);
            try
            {
                await _speechService.PreviewVoiceAsync(voiceId);
            }
            finally
            {
                // Only clear if this item is still the one showing the indicator.
                if (ReferenceEquals(_loadingItem, item))
                {
                    ClearLoadingIndicator();
                }
            }
        };
        return timer;
    }

    private void ShowLoadingIndicator(ToolStripMenuItem item)
    {
        ClearLoadingIndicator();
        try
        {
            _loadingItem = item;
            _loadingItemText = item.Text;
            item.Text = "\u23F3 " + _loadingItemText; // ⏳ prefix
        }
        catch
        {
            _loadingItem = null;
            _loadingItemText = null;
        }
    }

    private void ClearLoadingIndicator()
    {
        if (_loadingItem == null)
        {
            return;
        }
        try
        {
            if (_loadingItemText != null)
            {
                _loadingItem.Text = _loadingItemText;
            }
        }
        catch { /* menu item may be gone */ }
        finally
        {
            _loadingItem = null;
            _loadingItemText = null;
        }
    }

    /// <summary>Refreshes the check marks in the voice submenu.</summary>
    public void UpdateVoiceMenu()
    {
        if (_voiceMenu == null)
        {
            return;
        }

        string? selected = _settingsService.Settings.SelectedVoiceId?.Length > 0
            ? _settingsService.Settings.SelectedVoiceId
            : _speechService.CurrentVoiceId;

        foreach (ToolStripItem obj in _voiceMenu.DropDownItems)
        {
            if (obj is ToolStripMenuItem mi && mi.Tag is string id)
            {
                mi.Checked = id == selected;
            }
        }
    }

    private async void SetRate(double rate)
    {
        _speechService.SetRate(rate);
        _settingsService.Settings.SpeechRate = rate;
        UpdateRateMenu();
        await _settingsService.SaveAsync();
    }

    private void BuildVolumeMenu()
    {
        if (_volumeMenu == null)
        {
            return;
        }

        _volumeMenu.DropDownItems.Clear();

        int vol = _settingsService.Settings.Volume;
        if (vol < 0) vol = 0;
        if (vol > SpeechService.MaxVolumePercent) vol = SpeechService.MaxVolumePercent;

        _volumeLabel = new ToolStripMenuItem($"Volume: {vol}%  (100% = normal)") { Enabled = false };
        _volumeMenu.DropDownItems.Add(_volumeLabel);

        _volumeTrackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = SpeechService.MaxVolumePercent,
            TickFrequency = 50,
            SmallChange = 10,
            LargeChange = 50,
            Value = vol,
            Width = 240,
            AutoSize = false,
            TickStyle = TickStyle.BottomRight
        };
        // Live feedback while dragging; persist when the drag/keys finish.
        _volumeTrackBar.ValueChanged += OnVolumeChanged;
        _volumeTrackBar.MouseUp += (_, _) => SaveVolume();
        _volumeTrackBar.KeyUp += (_, _) => SaveVolume();

        var host = new ToolStripControlHost(_volumeTrackBar)
        {
            AutoSize = false,
            Width = 250,
            Padding = new Padding(6, 2, 6, 2)
        };
        _volumeMenu.DropDownItems.Add(host);
    }

    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        if (sender is not TrackBar tb)
        {
            return;
        }

        int vol = tb.Value;
        if (_volumeLabel != null)
        {
            _volumeLabel.Text = $"Volume: {vol}%  (100% = normal)";
        }
        // Apply immediately so the next spoken notification uses the new level.
        _speechService.SetVolume(vol);
        _settingsService.Settings.Volume = vol;
    }

    private async void SaveVolume()
    {
        await _settingsService.SaveAsync();
    }

    private void UpdateRateMenu()
    {
        double rate = _settingsService.Settings.SpeechRate;
        if (_rateSlow != null) _rateSlow.Checked = Math.Abs(rate - 0.5) < 0.01;
        if (_rateNormal != null) _rateNormal.Checked = Math.Abs(rate - 1.0) < 0.01;
        if (_rateFast != null) _rateFast.Checked = Math.Abs(rate - 1.5) < 0.01;

        // If the stored rate is a custom value, none are checked but Normal is the closest default.
        if (_rateSlow is { Checked: false } && _rateNormal is { Checked: false } && _rateFast is { Checked: false })
        {
            if (_rateNormal != null && rate > 0)
            {
                // Leave unchecked to reflect the custom value truthfully.
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (_menu == null || _notifyIcon == null)
        {
            return;
        }

        // Right-click is already handled natively by ContextMenuStrip; only trigger
        // the menu ourselves for left-click so it doesn't open twice.
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Ensure the menu takes foreground so it dismisses when clicking elsewhere.
        if (_menu.Handle != IntPtr.Zero)
        {
            SetForegroundWindow(_menu.Handle);
        }

        // NotifyIcon.ShowContextMenu is private; invoking it (with ContextMenuStrip
        // assigned) shows the menu at the cursor with correct positioning/behaviour.
        var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (mi != null)
        {
            mi.Invoke(_notifyIcon, null);
        }
        else
        {
            // Fallback if the internal method is ever renamed.
            _menu.Show(System.Windows.Forms.Cursor.Position);
        }
    }

    private async void OnToggleClicked(object? sender, EventArgs e)
    {
        bool enabled = _toggleItem?.Checked ?? true;
        _notificationService.IsEnabled = enabled;
        _speechService.IsEnabled = enabled;
        _settingsService.Settings.IsEnabled = enabled;

        if (_notifyIcon != null)
        {
            _notifyIcon.Text = BuildTooltip();
        }

        await _settingsService.SaveAsync();
    }

    private string BuildTooltip()
    {
        string state = _settingsService.Settings.IsEnabled ? "On" : "Off";
        return $"Notification Reader — Reading: {state}";
    }

    private void OpenSettingsWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null)
            {
                try
                {
                    _settingsWindow.Activate();
                    return;
                }
                catch
                {
                    _settingsWindow = null;
                }
            }

            _settingsWindow = new SettingsWindow(_settingsService, ((App)Application.Current).FilterService);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ExitApp()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
            Application.Current.Shutdown();
        });
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            // Try to load the packed resource icon.
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var s = streamInfo.Stream;
                return new Icon(s);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load embedded app icon; using system default.", ex);
        }

        // Fallback: load from disk next to the exe, else system default.
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load app icon from disk.", ex);
        }

        return SystemIcons.Application;
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
            _previewTimer?.Stop();
            _previewTimer?.Dispose();
            _speechService.CancelPreview();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _menu?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log("Error disposing tray icon.", ex);
        }

        GC.SuppressFinalize(this);
    }
}
