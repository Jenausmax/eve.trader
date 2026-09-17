namespace EveTrader.Domain.Facts;

/// <summary>
/// Окно локального хранения: глубина, на которую ордербук держится локально. Настройка,
/// а не константа — довод за 24 месяца не размер, а нестационарность: микроструктура
/// через границу патча описывает два разных мира.
///
/// Дневная история под окно не подпадает: тренды и сезонность живут именно там, а плата
/// за полную глубину мала.
/// </summary>
public sealed record MaterializationWindow
{
    private MaterializationWindow(TimeSpan orderBookDepth)
    {
        OrderBookDepth = orderBookDepth;
    }

    public TimeSpan OrderBookDepth { get; }

    public static MaterializationWindow Of(TimeSpan orderBookDepth) =>
        orderBookDepth > TimeSpan.Zero
            ? new MaterializationWindow(orderBookDepth)
            : throw new ArgumentOutOfRangeException(nameof(orderBookDepth), orderBookDepth, "Глубина окна положительна");

    /// <summary>Подпадает ли набор под окно вообще.</summary>
    public static bool IsWindowed(FactSet set) => set is not (FactSet.HistoryDaily or FactSet.Coverage);

    /// <summary>
    /// Интервал, который положено держать локально на указанный момент. Момент приходит
    /// аргументом: в домене нет «сейчас».
    /// </summary>
    public TimeRange LocalRange(FactSet set, TimeRange everything, DateTimeOffset asOf)
    {
        if (!IsWindowed(set))
        {
            return everything;
        }

        DateTimeOffset from = asOf - OrderBookDepth;

        return from <= everything.From ? everything : TimeRange.Between(from, everything.To);
    }
}
