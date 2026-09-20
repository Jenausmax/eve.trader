namespace EveTrader.Application.Live;

/// <summary>Решение бюджета ошибок: слать запрос или ждать.</summary>
/// <param name="IsAllowed">Запрос разрешён.</param>
/// <param name="ResumeAt">До какого момента ждать; значимо только при запрете.</param>
public readonly record struct BudgetVerdict(bool IsAllowed, DateTimeOffset ResumeAt)
{
    public static BudgetVerdict Allowed { get; } = new(true, DateTimeOffset.MinValue);

    public static BudgetVerdict PausedUntil(DateTimeOffset resumeAt) => new(false, resumeAt);

    /// <summary>
    /// Причина отсутствия наблюдений, пригодная для записи покрытия. Пауза обязана быть
    /// видна в данных: иначе пробел выглядит рыночным фактом, а не нашим молчанием.
    /// </summary>
    public string Reason =>
        IsAllowed
            ? string.Empty
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"бюджет ошибок источника исчерпан, пауза до {ResumeAt:yyyy-MM-dd HH:mm:ss}");
}
