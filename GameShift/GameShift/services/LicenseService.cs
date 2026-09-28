using System;
using System.Globalization;
using System.IO;

namespace GameShift.Services;

/// <summary>
/// Состояние лицензии на текущий момент. Безопасно копируется,
/// сравнивается по значению — удобно для биндинга/проверок в UI.
/// </summary>
public sealed record LicenseStatus(int DaysLeft, DateTime ExpiresAtUtc)
{
    /// <summary>Срок истёк (или вот-вот истечёт в течение часа).</summary>
    public bool IsExpired => DaysLeft <= 0;

    /// <summary>Осталось три дня и меньше — UI должен переключиться в "danger".</summary>
    public bool IsUrgent  => DaysLeft is > 0 and <= 3;
}

public interface ILicenseService
{
    LicenseStatus GetStatus();
}

/// <summary>
/// Заглушка лицензии-триала. Хранит дату «первого запуска» в LocalAppData
/// и считает остаток триала от неё. Корректно переживает рестарт —
/// в отличие от прежнего <c>static readonly DateTime ... = DateTime.Now.AddDays(30)</c>,
/// который перезапускал отсчёт при каждом запуске.
///
/// На технической фазе заменяется реализацией, читающей подписанный
/// лицензионный файл (или данные сервера активации). Контракт
/// <see cref="ILicenseService"/> при этом не меняется.
/// </summary>
public sealed class TrialLicenseService : ILicenseService
{
    private const int TrialDurationDays = 30;
    private const string StorageFileName = "trial.dat";

    private readonly string _storagePath;
    private DateTime? _cachedInstallDateUtc;

    public TrialLicenseService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameShift");

        _storagePath = Path.Combine(dir, StorageFileName);
    }

    public LicenseStatus GetStatus()
    {
        var installDate = _cachedInstallDateUtc ??= LoadOrCreateInstallDate();
        var expires     = installDate.AddDays(TrialDurationDays);

        // Округляем вверх: если до полуночи UTC меньше суток, всё ещё
        // показываем «1 день», а не «0» — иначе на последний день
        // пользователь увидит нулевой счётчик ещё до фактического конца.
        var daysLeft = Math.Max(0, (int)Math.Ceiling((expires - DateTime.UtcNow).TotalDays));

        return new LicenseStatus(daysLeft, expires);
    }

    // ── Persistence ──────────────────────────────────────────────────
    private DateTime LoadOrCreateInstallDate()
    {
        if (TryLoad(out var saved))
            return saved;

        var now = DateTime.UtcNow;
        TrySave(now);
        return now;
    }

    private bool TryLoad(out DateTime utcDate)
    {
        utcDate = default;
        try
        {
            if (!File.Exists(_storagePath)) return false;

            var raw = File.ReadAllText(_storagePath).Trim();

            // RoundtripKind нельзя сочетать с AssumeUniversal: TryParse бросает
            // ArgumentException, catch ниже его глотал, и триал начинался заново
            // при каждом запуске.
            if (!DateTime.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return false;
            }

            utcDate = parsed.ToUniversalTime();

            // Защита от «даты из будущего» — пользователь крутил часы
            // или файл повреждён. Сбрасываем на текущий момент.
            if (utcDate > DateTime.UtcNow.AddMinutes(5))
            {
                utcDate = default;
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TrySave(DateTime utcDate)
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(
                _storagePath,
                utcDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        catch
        {
            // Если LocalAppData недоступен (read-only окружение, политика
            // EDR, …) — лицензия будет работать в режиме «отсчёт от запуска»,
            // но UI не сломается.
        }
    }
}
