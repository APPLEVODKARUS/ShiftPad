using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using GameShift.Models;

namespace GameShift.Services;

public interface ISettingsService
{
    /// <summary>Текущее состояние. Изменять только через <see cref="Update"/>.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Применить мутацию к <see cref="Current"/> и запросить отложенное сохранение.
    /// Несколько вызовов подряд схлопываются в одну запись на диск.
    /// </summary>
    void Update(Action<AppSettings> mutate);

    /// <summary>Принудительно сохранить прямо сейчас (синхронно).</summary>
    void SaveNow();
}

/// <summary>
/// JSON-сохранение настроек в LocalAppData/GameShift/settings.json.
///
/// Особенности:
///   • Atomic write через .tmp + File.Replace — при сбое питания
///     основной файл либо старый, либо новый, но не «полу-записанный».
///   • Debounce 500мс — при перетаскивании тоггла или быстром
///     переборе ComboBox мы не пишем диск на каждый чих.
///   • Все ошибки I/O глотаются в Debug.WriteLine — потеря настроек
///     не должна обрушить приложение.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private const string FileName = "settings.json";
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter() },
    };

    private readonly string          _path;
    private readonly DispatcherTimer _debounceTimer;
    private bool _pendingSave;

    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameShift");

        _path   = Path.Combine(dir, FileName);
        Current = Load();

        _debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            SaveNow();
        };
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        _pendingSave = true;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void SaveNow()
    {
        _pendingSave = false;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp  = _path + ".tmp";
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(tmp, json);

            // Atomic replace. File.Replace требует существования destination —
            // на первом запуске его нет, тогда обычный Move.
            if (File.Exists(_path))
                File.Replace(tmp, _path, destinationBackupFileName: null);
            else
                File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameShift] Settings save failed: {ex}");
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();

            var json   = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Битый файл / несовместимая схема / нет прав — стартуем с дефолтов.
            // Старый файл при следующем сохранении перезапишется корректным.
            Debug.WriteLine($"[GameShift] Settings load failed, using defaults: {ex}");
            return new AppSettings();
        }
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        if (_pendingSave) SaveNow();
    }
}