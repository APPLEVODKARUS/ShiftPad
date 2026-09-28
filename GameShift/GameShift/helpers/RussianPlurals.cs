namespace GameShift.Helpers;

/// <summary>
/// Правила склонения числительных для русского языка.
/// </summary>
public static class RussianPlurals
{
    /// <summary>
    /// Выбирает корректную форму слова для числа <paramref name="count"/>.
    /// </summary>
    /// <param name="one">Форма для 1, 21, 31, ... 101 («день»).</param>
    /// <param name="few">Форма для 2-4, 22-24, ... («дня»).</param>
    /// <param name="many">Форма для 0, 5-20, 25-30, ... («дней»).</param>
    public static string Choose(int count, string one, string few, string many)
    {
        int absMod100 = System.Math.Abs(count) % 100;
        int absMod10  = System.Math.Abs(count) % 10;

        if (absMod100 is >= 11 and <= 14) return many;
        return absMod10 switch
        {
            1           => one,
            2 or 3 or 4 => few,
            _           => many,
        };
    }

    /// <summary>«1 день», «2 дня», «5 дней».</summary>
    public static string Days(int count) => Choose(count, "день", "дня", "дней");
}
