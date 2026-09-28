using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameShift.Helpers;
using H.NotifyIcon;
using MahApps.Metro.IconPacks;

namespace GameShift.Services;

/// <summary>
/// Инкапсулирует жизненный цикл иконки трея: создание, контекстное меню,
/// клики, корректную утилизацию. Внешний код подписывается через
/// callback-свойства — это проще DI-контейнера на текущей стадии,
/// а изолированность позволяет полностью пропустить тестирование трея,
/// если поднимать его в окружении не получается (CI, RDP без shell, …).
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private TaskbarIcon? _icon;

    /// <summary>Иконка успешно поднята и видна пользователю.</summary>
    public bool IsAvailable => _icon is not null;

    // ── Callbacks ────────────────────────────────────────────────────
    /// <summary>Левый клик / двойной клик / пункт «Открыть».</summary>
    public Action? OpenRequested    { get; set; }

    /// <summary>Пункт «Восстановить экран» в меню.</summary>
    public Action? RestoreRequested { get; set; }

    /// <summary>Пункт «Выход» в меню.</summary>
    public Action? ExitRequested    { get; set; }

    /// <summary>
    /// Поднимает иконку в трее. Если что-то пошло не так — возвращает false
    /// и оставляет менеджер в неактивном состоянии (см. <see cref="IsAvailable"/>).
    /// </summary>
    public bool Initialize()
    {
        try
        {
            _icon = new TaskbarIcon
            {
                ToolTipText = "GameShift",
                Icon        = TrayIconFactory.CreateGamepadIcon(),
                ContextMenu = BuildContextMenu(),
            };

            _icon.TrayLeftMouseUp      += OnTrayClick;
            _icon.TrayMouseDoubleClick += OnTrayClick;

            // ForceCreate гарантирует появление иконки прямо сейчас,
            // а не лениво при первом доступе.
            _icon.ForceCreate(enablesEfficiencyMode: false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameShift] Tray init failed: {ex}");
            DisposeIcon();
            return false;
        }
    }

    public void Dispose() => DisposeIcon();

    private void DisposeIcon()
    {
        if (_icon is null) return;
        try
        {
            _icon.TrayLeftMouseUp      -= OnTrayClick;
            _icon.TrayMouseDoubleClick -= OnTrayClick;
            _icon.Dispose();
        }
        catch
        {
            // Освобождаем при любом раскладе — процесс всё равно умирает,
            // но «по нормальному» — отписаться и подавить ошибку.
        }
        _icon = null;
    }

    // ── Меню ─────────────────────────────────────────────────────────
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        // Заголовок-«лейбл» — некликабельный пункт с названием.
        var activeColor = (Color)Application.Current.FindResource("ActiveColor");
        menu.Items.Add(new MenuItem
        {
            Header     = "GameShift",
            IsEnabled  = false,
            FontWeight = FontWeights.SemiBold,
            Icon       = MakeMenuIcon(PackIconMaterialKind.GamepadVariant, activeColor),
        });
        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(
            "Открыть",
            PackIconMaterialKind.WindowMaximize,
            () => OpenRequested?.Invoke()));

        menu.Items.Add(MakeMenuItem(
            "Восстановить экран",
            PackIconMaterialKind.RestoreAlert,
            () => RestoreRequested?.Invoke()));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(
            "Выход",
            PackIconMaterialKind.Power,
            () => ExitRequested?.Invoke()));

        return menu;
    }

    private static MenuItem MakeMenuItem(
        string               header,
        PackIconMaterialKind kind,
        Action               onClick)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon   = MakeMenuIcon(kind),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static UIElement MakeMenuIcon(PackIconMaterialKind kind, Color? tint = null)
    {
        var icon = new PackIconMaterial
        {
            Kind   = kind,
            Width  = 14,
            Height = 14,
        };
        if (tint is { } c)
            icon.Foreground = new SolidColorBrush(c);
        return icon;
    }

    private void OnTrayClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
}
