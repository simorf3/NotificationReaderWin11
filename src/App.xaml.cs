using System;
using System.Threading;
using System.Windows;
using NotificationReader.Services;
using NotificationReader.UI;

// Both WPF (System.Windows) and WinForms (System.Windows.Forms) are enabled,
// so disambiguate the shared type names to the WPF versions used here.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NotificationReader;

/// <summary>
/// Application entry point. Wires together all services and the tray icon.
/// This app has no main window; its lifetime is controlled explicitly.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "NotificationReader_SingleInstance_Mutex";

    private Mutex? _singleInstanceMutex;

    private SettingsService? _settingsService;
    private SpeechService? _speechService;
    private FilterService? _filterService;
    private NotificationService? _notificationService;
    private TrayIconManager? _trayIconManager;

    /// <summary>Exposes services to windows that need them.</summary>
    public SettingsService SettingsService => _settingsService!;
    public SpeechService SpeechService => _speechService!;
    public FilterService FilterService => _filterService!;
    public NotificationService NotificationService => _notificationService!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Notification Reader is already running. Check the system tray.",
                "Notification Reader",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // We control the window lifecycle manually (tray-only app).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            // Instantiate services in dependency order.
            _settingsService = new SettingsService();
            _speechService = new SpeechService();
            _filterService = new FilterService(_settingsService);
            _notificationService = new NotificationService(_settingsService, _filterService, _speechService);
            _trayIconManager = new TrayIconManager(_settingsService, _speechService, _notificationService);

            // Initialize in order.
            await _settingsService.InitializeAsync();
            await _speechService.InitializeAsync();
            _filterService.Reload();
            await _notificationService.InitializeAsync();
            await _trayIconManager.InitializeAsync();

            // Apply persisted settings to the speech engine.
            var settings = _settingsService.Settings;
            if (!string.IsNullOrWhiteSpace(settings.SelectedVoiceId))
            {
                _speechService.SetVoice(settings.SelectedVoiceId);
            }
            _speechService.SetRate(settings.SpeechRate);
        }
        catch (Exception ex)
        {
            Logger.Log("Fatal error during startup.", ex);
            MessageBox.Show(
                "Notification Reader failed to start:\n\n" + ex.Message,
                "Notification Reader",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayIconManager?.Dispose();
            _notificationService?.Dispose();
            _speechService?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log("Error during shutdown.", ex);
        }
        finally
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned; ignore.
            }
            _singleInstanceMutex?.Dispose();
        }

        base.OnExit(e);
    }
}
