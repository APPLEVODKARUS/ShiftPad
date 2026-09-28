using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GameShift.Helpers;
using GameShift.Models;
using GameShift.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;

namespace GameShift;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    // ── Кешированные ресурсы темы ────────────────────────────────────
    private readonly Brush _activeBrush;
    private readonly Brush _accentBrush;
    private readonly Brush _warnBrush;
    private readonly Brush _dangerBrush;
    private readonly Brush _mutedBrush;
    private readonly Brush _offBrush;
    private readonly Brush _heroBgActiveBrush;
    private readonly Brush _heroBgInactiveBrush;

    private readonly Color _activeColor;
    private readonly Color _offColor;

    // ── Сервисы ──────────────────────────────────────────────────────
    private readonly ILicenseService _licenseService = new TrialLicenseService();
    private readonly ISettingsService _settings;
    private readonly IGamepadService _gamepad;

    // ── Константы ────────────────────────────────────────────────────
    private const string FallbackAudioDevice = "Realtek (Speakers)";

    /// <summary>
    /// Максимум кнопок в комбинации. Подобрано так, чтобы BACK START BACK START
    /// влезало в одну строку поля шириной ~280px при уменьшенном размере пилюль.
    /// </summary>
    private const int MaxComboLength = 4;

    private static readonly int[] DelayValuesSeconds = { 0, 5, 15, 30, 60 };

    // ── Гарды UI-обновлений ──────────────────────────────────────────
    private bool _suspendUiUpdates;
    private bool _suspendDisplaySync;
    private bool _settingsLoaded;

    // ── Состояние записи комбинации ──────────────────────────────────
    private readonly List<GamepadButton> _activationCombo = new();
    private bool _isRecording;

    private string _persistedGamepadName = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _settings = app.Settings;
        _gamepad = app.Gamepad;

        _activeBrush = (Brush)FindResource("ActiveBrush");
        _accentBrush = (Brush)FindResource("AccentBrush");
        _warnBrush = (Brush)FindResource("WarnBrush");
        _dangerBrush = (Brush)FindResource("DangerBrush");
        _mutedBrush = (Brush)FindResource("MutedBrush");
        _offBrush = (Brush)FindResource("OffBrush");
        _heroBgActiveBrush = (Brush)FindResource("HeroIconBgActive");
        _heroBgInactiveBrush = (Brush)FindResource("HeroIconBgInactive");

        _activeColor = (Color)FindResource("ActiveColor");
        _offColor = (Color)FindResource("OffColor");

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings(_settings.Current);
        _settingsLoaded = true;

        _gamepad.Connected += OnGamepadConnected;
        _gamepad.Disconnected += OnGamepadDisconnected;
        _gamepad.ButtonsPressed += OnGamepadButtonsPressed;
        _gamepad.DeviceListChanged += OnGamepadDeviceListChanged;

        RefreshGamepadCombo();
        RefreshUi();
        UpdateActivationComboUi();
    }

    // ─────────────────────────────────────────────────────────────────
    //   ОБРАБОТЧИКИ ГЕЙМПАДА
    // ─────────────────────────────────────────────────────────────────
    private void OnGamepadConnected(object? sender, EventArgs e) => RefreshUi();

    private void OnGamepadDisconnected(object? sender, EventArgs e)
    {
        if (_isRecording) UpdateActivationComboUi();
        RefreshUi();
    }

    private void OnGamepadButtonsPressed(object? sender, GamepadButton[] buttons)
    {
        if (!_isRecording) return;

        bool added = false;
        foreach (var b in buttons)
        {
            if (_activationCombo.Count >= MaxComboLength) break;
            // Повторы разрешены — комбинации вида ↑↑ или START START валидны.
            _activationCombo.Add(b);
            added = true;
        }

        if (added) UpdateActivationComboUi();
    }

    private void OnGamepadDeviceListChanged(object? sender, EventArgs e)
    {
        RefreshGamepadCombo();
    }

    // ─────────────────────────────────────────────────────────────────
    //   GAMEPAD COMBOBOX
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Только реально подключённые устройства. Без «Любого геймпада».
    /// Если ничего нет — disabled-плейсхолдер «Геймпад не подключён»
    /// (одиночный неактивный пункт, выпадашка не открывается).
    ///
    /// Non-XInput устройства (DualSense без DS4Windows, Joy-Con и т.п.)
    /// в списке отображаются с пометкой «требует DS4Windows», чтобы
    /// пользователь сразу понимал, почему запись комбинации с ними
    /// не работает.
    /// </summary>
    private void RefreshGamepadCombo()
    {
        _suspendUiUpdates = true;
        try
        {
            GamepadCombo.Items.Clear();

            if (_gamepad.AvailableDevices.Count == 0)
            {
                var ghost = new ComboBoxItem
                {
                    Content = "Геймпад не подключён",
                    Tag = string.Empty,
                    IsEnabled = false,
                };
                GamepadCombo.Items.Add(ghost);
                GamepadCombo.SelectedItem = ghost;
                GamepadCombo.IsEnabled = false;
            }
            else
            {
                foreach (var device in _gamepad.AvailableDevices)
                {
                    GamepadCombo.Items.Add(new ComboBoxItem
                    {
                        Content = device.Name,
                        Tag = device.Name,
                    });
                }

                GamepadCombo.IsEnabled = true;
                SelectGamepadByName(_persistedGamepadName);
            }
        }
        finally
        {
            _suspendUiUpdates = false;
        }

        RefreshUi();
    }

    private void SelectGamepadByName(string name)
    {
        // Только реальные пункты — disabled-плейсхолдер пропускаем.
        foreach (ComboBoxItem item in GamepadCombo.Items)
        {
            if (!item.IsEnabled) continue;
            if ((item.Tag as string) == name)
            {
                GamepadCombo.SelectedItem = item;
                return;
            }
        }

        // Не нашли — выбираем первый активный пункт. _persistedGamepadName
        // не сбрасываем: при следующем подключении нужного устройства оно
        // опять автоматически выделится.
        foreach (ComboBoxItem item in GamepadCombo.Items)
        {
            if (item.IsEnabled)
            {
                GamepadCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void GamepadSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiUpdates) return;

        if (GamepadCombo.SelectedItem is ComboBoxItem item && item.IsEnabled)
            _persistedGamepadName = item.Tag as string ?? string.Empty;

        RefreshUi();
    }

    /// <summary>Информация о выбранном в combo box устройстве, либо null.</summary>
    private GamepadDeviceInfo? GetSelectedDeviceInfo()
    {
        if (GamepadCombo.SelectedItem is not ComboBoxItem item) return null;
        if (!item.IsEnabled) return null;

        string? name = item.Tag as string;
        if (string.IsNullOrEmpty(name)) return null;

        return _gamepad.AvailableDevices.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────
    //   SETTINGS ↔ UI
    // ─────────────────────────────────────────────────────────────────
    private void ApplySettings(AppSettings s)
    {
        _suspendUiUpdates = true;
        try
        {
            MainToggle.IsChecked = s.IsAutomationEnabled;
            SourceDisplayCombo.SelectedIndex = ClampIndex(s.SourceDisplayIndex, SourceDisplayCombo);
            TargetDisplayCombo.SelectedIndex = ClampIndex(s.TargetDisplayIndex, TargetDisplayCombo);
            ModeCombo.SelectedIndex = ClampIndex(s.DisplayMode, ModeCombo);
            AudioCombo.SelectedIndex = ClampIndex(s.AudioDeviceIndex, AudioCombo);
            SteamToggle.IsChecked = s.LaunchSteamBigPicture;
            ConfirmToggle.IsChecked = s.ConfirmRestore;
            TrayToggle.IsChecked = s.MinimizeToTray;
            AutoStartToggle.IsChecked = s.AutoStartWithWindows;
            CustomLauncherToggle.IsChecked = s.CustomLauncherEnabled;
            CustomLauncherPath.Text = s.CustomLauncherPath ?? string.Empty;
            DelayInCombo.SelectedIndex = DelayIndexFromSeconds(s.ActivationDelaySeconds);
            DelayOutCombo.SelectedIndex = DelayIndexFromSeconds(s.DeactivationDelaySeconds);

            _persistedGamepadName = s.GamepadDeviceName ?? string.Empty;

            // Если в файле сохранена комбинация длиннее нового лимита —
            // обрезаем, чтобы UI не показывал то, что нельзя ввести заново.
            _activationCombo.Clear();
            if (s.ActivationCombo is { Count: > 0 })
                _activationCombo.AddRange(s.ActivationCombo.Take(MaxComboLength));
        }
        finally
        {
            _suspendUiUpdates = false;
        }
    }

    private void PersistSettings()
    {
        if (!_settingsLoaded || _suspendUiUpdates) return;

        _settings.Update(s =>
        {
            s.IsAutomationEnabled = MainToggle.IsChecked == true;
            s.SourceDisplayIndex = SourceDisplayCombo.SelectedIndex;
            s.TargetDisplayIndex = TargetDisplayCombo.SelectedIndex;
            s.GamepadDeviceName = _persistedGamepadName;
            s.DisplayMode = ModeCombo.SelectedIndex;
            s.AudioDeviceIndex = AudioCombo.SelectedIndex;
            s.LaunchSteamBigPicture = SteamToggle.IsChecked == true;
            s.ConfirmRestore = ConfirmToggle.IsChecked == true;
            s.MinimizeToTray = TrayToggle.IsChecked == true;
            s.AutoStartWithWindows = AutoStartToggle.IsChecked == true;
            s.CustomLauncherEnabled = CustomLauncherToggle.IsChecked == true;
            s.CustomLauncherPath = CustomLauncherPath.Text ?? string.Empty;
            s.ActivationDelaySeconds = DelaySecondsFromIndex(DelayInCombo.SelectedIndex);
            s.DeactivationDelaySeconds = DelaySecondsFromIndex(DelayOutCombo.SelectedIndex);
            s.ActivationCombo = new List<GamepadButton>(_activationCombo);
        });
    }

    private static int ClampIndex(int index, ComboBox box)
    {
        if (box.Items.Count == 0) return -1;
        return Math.Clamp(index, 0, box.Items.Count - 1);
    }

    private static int DelayIndexFromSeconds(int seconds)
    {
        int idx = Array.IndexOf(DelayValuesSeconds, seconds);
        return idx >= 0 ? idx : 0;
    }

    private static int DelaySecondsFromIndex(int index) =>
        (index >= 0 && index < DelayValuesSeconds.Length) ? DelayValuesSeconds[index] : 0;

    // ─────────────────────────────────────────────────────────────────
    //   ОБРАБОТЧИКИ КОНТРОЛОВ
    // ─────────────────────────────────────────────────────────────────
    private void MainToggle_Checked(object sender, RoutedEventArgs e) => RefreshUi();
    private void MainToggle_Unchecked(object sender, RoutedEventArgs e) => RefreshUi();
    private void SteamToggle_Changed(object sender, RoutedEventArgs e) => RefreshUi();
    private void ConfirmToggle_Changed(object sender, RoutedEventArgs e) => RefreshUi();
    private void TrayToggle_Changed(object sender, RoutedEventArgs e) => RefreshUi();
    private void AutoStartToggle_Changed(object sender, RoutedEventArgs e) => RefreshUi();
    private void CustomLauncherToggle_Changed(object sender, RoutedEventArgs e) => RefreshUi();
    private void AnyConfig_Changed(object sender, SelectionChangedEventArgs e) => RefreshUi();
    private void CustomLauncher_Changed(object sender, TextChangedEventArgs e) => RefreshUi();

    private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл лаунчера",
            Filter = "Исполняемые файлы (*.exe;*.lnk)|*.exe;*.lnk|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(CustomLauncherPath.Text))
        {
            try
            {
                var dir = Path.GetDirectoryName(CustomLauncherPath.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    dialog.InitialDirectory = dir;
            }
            catch { /* невалидные символы — игнорируем */ }
        }

        if (dialog.ShowDialog() == true)
            CustomLauncherPath.Text = dialog.FileName;
    }

    private void DisplaySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiUpdates || _suspendDisplaySync) return;
        if (SourceDisplayCombo is null || TargetDisplayCombo is null) return;

        if (SourceDisplayCombo.SelectedIndex == TargetDisplayCombo.SelectedIndex)
        {
            _suspendDisplaySync = true;
            try
            {
                if (sender == SourceDisplayCombo)
                    TargetDisplayCombo.SelectedIndex = NextIndex(TargetDisplayCombo);
                else
                    SourceDisplayCombo.SelectedIndex = NextIndex(SourceDisplayCombo);
            }
            finally
            {
                _suspendDisplaySync = false;
            }
        }

        RefreshUi();
    }

    private int NextIndex(ComboBox box)
    {
        var other = ReferenceEquals(box, SourceDisplayCombo) ? TargetDisplayCombo : SourceDisplayCombo;
        int forbid = other.SelectedIndex;
        int count = box.Items.Count;
        if (count <= 1) return box.SelectedIndex;

        for (int i = 0; i < count; i++)
            if (i != forbid) return i;
        return box.SelectedIndex;
    }

    // ─────────────────────────────────────────────────────────────────
    //   КОМБИНАЦИЯ АКТИВАЦИИ
    // ─────────────────────────────────────────────────────────────────
    private void RecordCombo_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording) FinishRecording();
        else StartRecording();
    }

    private void ClearCombo_Click(object sender, RoutedEventArgs e)
    {
        _isRecording = false;
        _activationCombo.Clear();
        UpdateActivationComboUi();
        PersistSettings();
    }

    private void StartRecording()
    {
        _isRecording = true;
        _activationCombo.Clear();
        UpdateActivationComboUi();
    }

    private void FinishRecording()
    {
        _isRecording = false;
        UpdateActivationComboUi();
        PersistSettings();
    }

    private void UpdateActivationComboUi()
    {
        if (ActivationComboPanel is null) return;

        if (_isRecording)
        {
            if (_activationCombo.Count == 0)
            {
                ActivationComboPlaceholder.Visibility = Visibility.Collapsed;
                ActivationComboPanel.Visibility = Visibility.Collapsed;
                RecordingIndicator.Visibility = Visibility.Visible;

                // Подсказка зависит от выбранного устройства:
                //   • non-XInput (DualSense без DS4Windows и т.п.) — сообщаем
                //     пользователю, что нужен DS4Windows;
                //   • XInput подключён — стандартное «нажмите кнопки»;
                //   • вообще ничего — «подключите геймпад».
                var selectedDevice = GetSelectedDeviceInfo();
                if (selectedDevice is { IsSupported: false })
                    RecordingHint.Text = "Геймпад не поддерживается";
                else if (_gamepad.IsConnected)
                    RecordingHint.Text = "Нажмите кнопки на геймпаде";
                else
                    RecordingHint.Text = "Подключите геймпад";
            }
            else
            {
                ActivationComboPlaceholder.Visibility = Visibility.Collapsed;
                ActivationComboPanel.Visibility = Visibility.Visible;
                RecordingIndicator.Visibility = Visibility.Collapsed;
                RebuildPills();
            }

            RecordButtonText.Text = "Принять";
            RecordButtonIcon.Kind = PackIconMaterialKind.Check;
            RecordButtonIcon.Foreground = _activeBrush;
            return;
        }

        RecordingIndicator.Visibility = Visibility.Collapsed;

        if (_activationCombo.Count == 0)
        {
            ActivationComboPlaceholder.Visibility = Visibility.Visible;
            ActivationComboPanel.Visibility = Visibility.Collapsed;

            RecordButtonText.Text = "Записать комбинацию";
            RecordButtonIcon.Kind = PackIconMaterialKind.RecordCircleOutline;
            RecordButtonIcon.Foreground = _activeBrush;
        }
        else
        {
            ActivationComboPlaceholder.Visibility = Visibility.Collapsed;
            ActivationComboPanel.Visibility = Visibility.Visible;
            RebuildPills();

            RecordButtonText.Text = "Изменить";
            RecordButtonIcon.Kind = PackIconMaterialKind.PencilOutline;
            RecordButtonIcon.Foreground = _activeBrush;
        }
    }

    private void RebuildPills()
    {
        ActivationComboPanel.Children.Clear();

        for (int i = 0; i < _activationCombo.Count; i++)
        {
            if (i > 0)
                ActivationComboPanel.Children.Add(MakePillSeparator());

            ActivationComboPanel.Children.Add(MakePill(_activationCombo[i]));
        }
    }

    /// <summary>
    /// Уменьшенная пилюля. Локальные значения побеждают сетеры из стиля
    /// <c>GamepadButtonPill</c>, поэтому стиль трогать не надо: триггеры
    /// (hover и т.д.) продолжают работать, размеры — наши.
    /// </summary>
    private static Border MakePill(GamepadButton button) => new()
    {
        Style = (Style)Application.Current.FindResource("GamepadButtonPill"),
        Margin = new Thickness(0),
        Padding = new Thickness(7, 1, 7, 1),
        MinHeight = 20,
        MinWidth = 0,
        Child = new TextBlock
        {
            Style = (Style)Application.Current.FindResource("GamepadButtonPillText"),
            Text = button.DisplayName(),
            FontSize = 10,
            Margin = new Thickness(0),
        },
    };

    private TextBlock MakePillSeparator() => new()
    {
        Text = "+",
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = _mutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(3, 0, 3, 0),
    };

    // ─────────────────────────────────────────────────────────────────
    //   ЕДИНАЯ ТОЧКА ОБНОВЛЕНИЯ ОСНОВНОГО UI
    // ─────────────────────────────────────────────────────────────────
    private void RefreshUi()
    {
        if (_suspendUiUpdates) return;
        if (StatusDot is null) return;

        bool isActive = MainToggle?.IsChecked == true;

        UpdateLicenseBadge();
        UpdateStatusFooter(isActive);
        UpdateHero(isActive);
        UpdateMiniStats(isActive);

        PersistSettings();
    }

    private void UpdateLicenseBadge()
    {
        var status = _licenseService.GetStatus();

        LicenseDays.Text = status.IsExpired
            ? "истекла"
            : $"{status.DaysLeft} {RussianPlurals.Days(status.DaysLeft)}";

        bool urgent = status.IsUrgent || status.IsExpired;
        LicenseBadge.Tag = urgent ? "danger" : "warn";

        var pillBrush = urgent ? _dangerBrush : _warnBrush;
        LicenseIcon.Kind = urgent
            ? PackIconMaterialKind.ShieldAlertOutline
            : PackIconMaterialKind.ShieldOutline;
        LicenseIcon.Foreground = pillBrush;
        LicenseDays.Foreground = pillBrush;
    }

    private void License_Click(object sender, MouseButtonEventArgs e)
    {
        var status = _licenseService.GetStatus();

        string daysText = status.IsExpired
            ? "Срок пробного периода истёк."
            : $"Пробный период: осталось {status.DaysLeft} {RussianPlurals.Days(status.DaysLeft)}.";

        MessageBox.Show(
            this,
            $"{daysText}\n\n" +
            "В полной версии будут доступны:\n" +
            "  • неограниченное число профилей\n" +
            "  • расширенное управление дисплеями\n" +
            "  • приоритет техподдержки\n\n" +
            "Окно активации будет добавлено в следующей версии.",
            "Лицензия GameShift",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateHero(bool isActive)
    {
        HeroTitle.Text = isActive
            ? "Автоматизация включена"
            : "Автоматизация выключена";

        HeroSubtitle.Text = isActive
            ? "Система отслеживает подключение контроллера"
            : "Подключение контроллера не отслеживается";

        HeroIcon.Foreground = isActive ? _accentBrush : _offBrush;
        HeroIconBg.Fill = isActive ? _heroBgActiveBrush : _heroBgInactiveBrush;
    }

    private void UpdateMiniStats(bool isActive)
    {
        // ── Геймпад ─────────────────────────────────────────────────────
        // Отображаем то, что выбрано в combo box. Для XInput-совместимых
        // устройств точка зелёная (всё работает), для остальных — жёлтая
        // (выбран, но опрашиваться не будет, нужен DS4Windows).
        if (isActive)
        {
            var selected = GetSelectedDeviceInfo();
            if (selected != null)
            {
                GamepadStatus.Text = selected.Name;
                GamepadStatusDot.Fill = selected.IsSupported ? _activeBrush : _warnBrush;
            }
            else
            {
                GamepadStatus.Text = "Не подключён";
                GamepadStatusDot.Fill = _mutedBrush;
            }
        }
        else
        {
            GamepadStatus.Text = "Не отслеживается";
            GamepadStatusDot.Fill = _offBrush;
        }

        // ── Активный экран ──
        if (isActive && SourceDisplayCombo.SelectedItem is ComboBoxItem display)
        {
            ActiveDisplay.Text = display.Content?.ToString() ?? "—";
            ActiveDisplayDot.Fill = _activeBrush;
        }
        else
        {
            ActiveDisplay.Text = "—";
            ActiveDisplayDot.Fill = _offBrush;
        }

        // ── Аудио / Steam — заглушки до этапов 5 и 6 ──
        ActiveAudio.Text = isActive ? FallbackAudioDevice : "—";
        ActiveAudioDot.Fill = isActive ? _mutedBrush : _offBrush;

        SteamStatus.Text = isActive ? "В режиме рабочего стола" : "—";
        SteamStatusDot.Fill = isActive ? _mutedBrush : _offBrush;
    }

    private void UpdateStatusFooter(bool isActive)
    {
        StatusDot.Fill = isActive ? _activeBrush : _offBrush;

        if (StatusDot.Effect is DropShadowEffect glow)
        {
            glow.Color = isActive ? _activeColor : _offColor;
            glow.Opacity = isActive ? 0.9 : 0.0;
        }

        StatusLabel.Text = isActive ? "GameShift активен" : "GameShift остановлен";
    }

    // ─────────────────────────────────────────────────────────────────
    //   КНОПКИ В ПОДВАЛЕ
    // ─────────────────────────────────────────────────────────────────
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Сбросить все настройки до заводских?",
            "GameShift — Сброс настроек",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK) return;

        if (_isRecording) _isRecording = false;

        _suspendUiUpdates = true;
        try
        {
            MainToggle.IsChecked = true;
            SourceDisplayCombo.SelectedIndex = 0;
            TargetDisplayCombo.SelectedIndex = 1;
            ModeCombo.SelectedIndex = 0;
            AudioCombo.SelectedIndex = 0;
            SteamToggle.IsChecked = true;
            ConfirmToggle.IsChecked = true;
            TrayToggle.IsChecked = false;
            AutoStartToggle.IsChecked = false;
            CustomLauncherToggle.IsChecked = false;
            CustomLauncherPath.Text = string.Empty;
            DelayInCombo.SelectedIndex = 0;
            DelayOutCombo.SelectedIndex = 1;

            _persistedGamepadName = string.Empty;
            _activationCombo.Clear();
        }
        finally
        {
            _suspendUiUpdates = false;
        }

        SelectGamepadByName(string.Empty);
        RefreshUi();
        UpdateActivationComboUi();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "GameShift v1.0\n" +
            "Профессиональная утилита для игровых сессий.\n\n" +
            "© 2025 GameShift Team",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ─────────────────────────────────────────────────────────────────
    //   ИНТЕГРАЦИЯ С ТРЕЕМ И ЖИЗНЕННЫЙ ЦИКЛ ОКНА
    // ─────────────────────────────────────────────────────────────────
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isRecording) _isRecording = false;

        bool realShutdown =
            Application.Current is not App app
            || app.IsRealShutdown
            || !app.IsTrayAvailable;

        if (!realShutdown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamepad.Connected -= OnGamepadConnected;
        _gamepad.Disconnected -= OnGamepadDisconnected;
        _gamepad.ButtonsPressed -= OnGamepadButtonsPressed;
        _gamepad.DeviceListChanged -= OnGamepadDeviceListChanged;

        Loaded -= OnLoaded;

        base.OnClosed(e);
    }

    public void RestoreOriginalState()
    {
        MessageBox.Show(
            this,
            "Откат состояния будет доступен после интеграции с системными API.\n" +
            "Функция в разработке.",
            "GameShift",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}