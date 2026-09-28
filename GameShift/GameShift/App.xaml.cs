using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GameShift.Services;

namespace GameShift;

public partial class App : Application
{
    private TrayIconManager? _tray;
    private SettingsService? _settingsService;
    private GamepadService? _gamepadService;

    public ISettingsService Settings =>
        _settingsService ?? throw new InvalidOperationException(
            "Settings accessed before App.OnStartup completed.");

    public IGamepadService Gamepad =>
        _gamepadService ?? throw new InvalidOperationException(
            "Gamepad accessed before App.OnStartup completed.");

    public bool IsRealShutdown { get; private set; }

    public bool IsTrayAvailable => _tray?.IsAvailable ?? false;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SessionEnding += OnSessionEnding;

        _settingsService = new SettingsService();

        // Геймпад опрашивается всё время жизни приложения — нужно для
        // будущего ProfileEngine (этап 7), который должен ловить подключение
        // даже когда окно свёрнуто в трей.
        _gamepadService = new GamepadService();
        _gamepadService.Start();

        base.OnStartup(e);

        _tray = new TrayIconManager
        {
            OpenRequested = ShowMainWindow,
            RestoreRequested = RestoreOriginal,
            ExitRequested = ExitApp,
        };
        _tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        SessionEnding -= OnSessionEnding;

        _tray?.Dispose();
        _tray = null;

        _gamepadService?.Dispose();
        _gamepadService = null;

        _settingsService?.Dispose();
        _settingsService = null;

        base.OnExit(e);
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        IsRealShutdown = true;
    }

    public void ShowMainWindow()
    {
        if (Current?.MainWindow is not { } window) return;

        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Focus();
    }

    public void RestoreOriginal()
    {
        if (Current?.MainWindow is MainWindow mw)
            mw.RestoreOriginalState();
    }

    public void ExitApp()
    {
        IsRealShutdown = true;
        Shutdown();
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError(ex);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.SetObserved();
    }

    private static void ShowError(Exception ex)
    {
        void Show() =>
            MessageBox.Show(
                $"Критическая ошибка:\n{ex.Message}\n\n{ex.StackTrace}",
                "GameShift — Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Show();
        else
            dispatcher.Invoke(Show);
    }
}