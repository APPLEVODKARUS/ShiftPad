using System.Collections.Generic;
using GameShift.Models;

namespace GameShift.Models;

/// <summary>
/// Сериализуемое состояние приложения. Простой mutable-класс, потому что
/// мы массово накатываем поля из/в UI. Дефолты соответствуют значениям
/// в XAML — при первом запуске (нет файла настроек) UI не «прыгает».
///
/// Версионирование — поле <see cref="Version"/>. Пока всегда 1.
/// При несовместимом изменении схемы поднимаем номер и в SettingsService
/// добавляем мигратор. Сейчас не нужно.
/// </summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    // ── Главный тоггл ───────────────────────────────────────────────
    public bool IsAutomationEnabled { get; set; } = true;

    // ── Дисплеи ─────────────────────────────────────────────────────
    // Пока сохраняем индексы ComboBox. На этапе 4 (DisplayService)
    // заменим на стабильные идентификаторы мониторов.
    public int SourceDisplayIndex { get; set; } = 0;
    public int TargetDisplayIndex { get; set; } = 1;
    public int DisplayMode        { get; set; } = 0; // 0=только цель, 1=цель+источник, 2=дублировать

    // ── Геймпад / Аудио ─────────────────────────────────────────────
    /// <summary>
    /// Имя выбранного геймпада. Пустая строка — выбор не сделан,
    /// используется первый подключённый. Имя берётся из Windows.Gaming.Input
    /// (RawGameController.DisplayName). Сохраняется по имени, а не по
    /// индексу — индексы плавают при подключении/отключении.
    /// </summary>
    public string GamepadDeviceName { get; set; } = string.Empty;

    public int AudioDeviceIndex { get; set; } = 0;

    // ── Лаунчеры ────────────────────────────────────────────────────
    public bool   LaunchSteamBigPicture { get; set; } = true;
    public bool   CustomLauncherEnabled { get; set; } = false;
    public string CustomLauncherPath    { get; set; } = string.Empty;

    // ── Поведение ───────────────────────────────────────────────────
    public bool ConfirmRestore       { get; set; } = true;
    public bool MinimizeToTray       { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = false;

    // ── Задержки ────────────────────────────────────────────────────
    // Храним секунды, не индексы — устойчиво к перестановке пунктов
    // ComboBox в будущем. Допустимый набор: 0, 5, 15, 30, 60.
    public int ActivationDelaySeconds   { get; set; } = 0;
    public int DeactivationDelaySeconds { get; set; } = 5;

    // ── Комбинация активации ────────────────────────────────────────
    public List<GamepadButton> ActivationCombo { get; set; } = new();
}