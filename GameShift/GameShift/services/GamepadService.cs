using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using GameShift.Models;
using Windows.Gaming.Input;
using WgiGamepad = Windows.Gaming.Input.Gamepad;
using WgiGamepadButtons = Windows.Gaming.Input.GamepadButtons;

namespace GameShift.Services;

public interface IGamepadService
{
    bool IsConnected { get; }
    bool IsAnyButtonHeld { get; }
    string? ConnectedDeviceName { get; }
    IReadOnlyList<GamepadDeviceInfo> AvailableDevices { get; }

    event EventHandler? Connected;
    event EventHandler? Disconnected;
    event EventHandler<GamepadButton[]>? ButtonsPressed;
    event EventHandler? DeviceListChanged;

    void Start();
    void Stop();
}

/// <summary>
/// Опрос геймпадов через Windows.Gaming.Input — нативный Microsoft API.
///
/// Зачем не XInput: XInput не отдаёт имена устройств (всё «Xbox 360 Controller»),
/// не видит DualSense без сторонних драйверов, и считается legacy-слоем
/// поверх WGI начиная с Windows 10. WGI = что использует современный Xbox app
/// и Steam Input под капотом.
///
/// Архитектура:
///   • RawGameController.RawGameControllers даёт ВСЕ устройства с правильными
///     именами через DisplayName.
///   • Gamepad.Gamepads — подмножество, которое представляется как
///     стандартный Xbox-style геймпад. Только эти умеем опрашивать.
///   • Подписываемся на *Added/*Removed события WGI — список обновляется
///     сам, без поллинга устройств.
/// </summary>
public sealed class GamepadService : IGamepadService, IDisposable
{
    private const int PollIntervalMs = 50;
    private const double TriggerPressThreshold = 0.12; // 0..1

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _ui;

    private WgiGamepad? _activeGamepad;
    private WgiGamepadButtons _previousButtons;
    private bool _previousLeftTrigger;
    private bool _previousRightTrigger;

    public bool IsConnected => _activeGamepad is not null;
    public bool IsAnyButtonHeld { get; private set; }
    public string? ConnectedDeviceName { get; private set; }

    public IReadOnlyList<GamepadDeviceInfo> AvailableDevices { get; private set; }
        = Array.Empty<GamepadDeviceInfo>();

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<GamepadButton[]>? ButtonsPressed;
    public event EventHandler? DeviceListChanged;

    public GamepadService()
    {
        _ui = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        // События WGI прилетают НЕ на UI-поток — поэтому в обработчиках
        // делаем _ui.BeginInvoke перед обновлением свойств / fire'ингом наших событий.
        // Именно асинхронно: синхронный Invoke держит поток WGI, пока UI-поток
        // выполняет RefreshDevices, который сам обращается к WGI, — риск взаимной блокировки.
        RawGameController.RawGameControllerAdded += OnRawControllerAdded;
        RawGameController.RawGameControllerRemoved += OnRawControllerRemoved;
        WgiGamepad.GamepadAdded += OnGamepadAdded;
        WgiGamepad.GamepadRemoved += OnGamepadRemoved;

        RefreshDevices();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        RawGameController.RawGameControllerAdded -= OnRawControllerAdded;
        RawGameController.RawGameControllerRemoved -= OnRawControllerRemoved;
        WgiGamepad.GamepadAdded -= OnGamepadAdded;
        WgiGamepad.GamepadRemoved -= OnGamepadRemoved;
    }

    private void OnRawControllerAdded(object? sender, RawGameController e) => _ui.BeginInvoke(RefreshDevices);
    private void OnRawControllerRemoved(object? sender, RawGameController e) => _ui.BeginInvoke(RefreshDevices);
    private void OnGamepadAdded(object? sender, WgiGamepad e) => _ui.BeginInvoke(RefreshDevices);
    private void OnGamepadRemoved(object? sender, WgiGamepad e) => _ui.BeginInvoke(RefreshDevices);

    /// <summary>
    /// Перестраивает список устройств и выбирает активный для опроса.
    /// Активный = первый Gamepad из <see cref="WgiGamepad.Gamepads"/>;
    /// MainWindow в дальнейшем сможет переопределить выбор по имени.
    /// </summary>
    private void RefreshDevices()
    {
        var rawList = RawGameController.RawGameControllers;
        var gamepadList = WgiGamepad.Gamepads;

        // Какие RawGameController-ы являются Xbox-style (попадают в Gamepad.Gamepads).
        var supportedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in gamepadList)
        {
            try
            {
                var raw = RawGameController.FromGameController(g);
                if (raw is not null && !string.IsNullOrEmpty(raw.NonRoamableId))
                    supportedIds.Add(raw.NonRoamableId);
            }
            catch { /* геймпад мог исчезнуть — игнорируем */ }
        }

        var newList = new List<GamepadDeviceInfo>(rawList.Count);
        foreach (var raw in rawList)
        {
            string name = !string.IsNullOrWhiteSpace(raw.DisplayName)
                ? raw.DisplayName
                : "Геймпад";

            bool isSupported = !string.IsNullOrEmpty(raw.NonRoamableId)
                            && supportedIds.Contains(raw.NonRoamableId);

            newList.Add(new GamepadDeviceInfo(name, raw.NonRoamableId ?? "", isSupported));
        }

        AvailableDevices = newList;

        // Выбираем активный геймпад для опроса. Сейчас просто первый —
        // ProfileEngine на этапе 7 будет учитывать пользовательский выбор.
        var newActive = gamepadList.FirstOrDefault();

        bool wasConnected = _activeGamepad is not null;
        bool willConnected = newActive is not null;

        _activeGamepad = newActive;
        _previousButtons = WgiGamepadButtons.None;
        _previousLeftTrigger = false;
        _previousRightTrigger = false;
        IsAnyButtonHeld = false;

        if (newActive is not null)
        {
            try
            {
                var raw = RawGameController.FromGameController(newActive);
                ConnectedDeviceName = raw?.DisplayName;
            }
            catch { ConnectedDeviceName = null; }
        }
        else
        {
            ConnectedDeviceName = null;
        }

        if (wasConnected && !willConnected)
            Disconnected?.Invoke(this, EventArgs.Empty);
        else if (!wasConnected && willConnected)
            Connected?.Invoke(this, EventArgs.Empty);

        DeviceListChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_activeGamepad is null) return;

        try
        {
            var reading = _activeGamepad.GetCurrentReading();
            var newlyPressed = reading.Buttons & ~_previousButtons;

            var pressed = new List<GamepadButton>(4);

            if ((newlyPressed & WgiGamepadButtons.A) != 0) pressed.Add(GamepadButton.A);
            if ((newlyPressed & WgiGamepadButtons.B) != 0) pressed.Add(GamepadButton.B);
            if ((newlyPressed & WgiGamepadButtons.X) != 0) pressed.Add(GamepadButton.X);
            if ((newlyPressed & WgiGamepadButtons.Y) != 0) pressed.Add(GamepadButton.Y);
            if ((newlyPressed & WgiGamepadButtons.LeftShoulder) != 0) pressed.Add(GamepadButton.LB);
            if ((newlyPressed & WgiGamepadButtons.RightShoulder) != 0) pressed.Add(GamepadButton.RB);
            if ((newlyPressed & WgiGamepadButtons.View) != 0) pressed.Add(GamepadButton.Back);
            if ((newlyPressed & WgiGamepadButtons.Menu) != 0) pressed.Add(GamepadButton.Start);
            if ((newlyPressed & WgiGamepadButtons.LeftThumbstick) != 0) pressed.Add(GamepadButton.LeftStick);
            if ((newlyPressed & WgiGamepadButtons.RightThumbstick) != 0) pressed.Add(GamepadButton.RightStick);
            if ((newlyPressed & WgiGamepadButtons.DPadUp) != 0) pressed.Add(GamepadButton.DPadUp);
            if ((newlyPressed & WgiGamepadButtons.DPadDown) != 0) pressed.Add(GamepadButton.DPadDown);
            if ((newlyPressed & WgiGamepadButtons.DPadLeft) != 0) pressed.Add(GamepadButton.DPadLeft);
            if ((newlyPressed & WgiGamepadButtons.DPadRight) != 0) pressed.Add(GamepadButton.DPadRight);

            // Триггеры — это axes 0..1, обрабатываем как кнопки с порогом.
            bool leftPressed = reading.LeftTrigger >= TriggerPressThreshold;
            bool rightPressed = reading.RightTrigger >= TriggerPressThreshold;

            if (leftPressed && !_previousLeftTrigger) pressed.Add(GamepadButton.LT);
            if (rightPressed && !_previousRightTrigger) pressed.Add(GamepadButton.RT);

            _previousButtons = reading.Buttons;
            _previousLeftTrigger = leftPressed;
            _previousRightTrigger = rightPressed;
            IsAnyButtonHeld = reading.Buttons != WgiGamepadButtons.None
                                 || leftPressed || rightPressed;

            if (pressed.Count > 0)
                ButtonsPressed?.Invoke(this, pressed.ToArray());
        }
        catch
        {
            // Геймпад отключили прямо в момент чтения — RefreshDevices через
            // GamepadRemoved-event подчистит _activeGamepad.
        }
    }

    public void Dispose() => Stop();
}