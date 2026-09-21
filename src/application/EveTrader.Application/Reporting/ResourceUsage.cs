using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>
/// Расход ресурсов за интервал: сколько спросили у источника и сколько получили.
///
/// Считается по двум разным следам, и это не дублирование. Число запросов и доля
/// «не изменилось» восстанавливаются из журнала покрытия — он живёт столько же, сколько
/// факты. Объём трафика восстанавливается из окна сырых страниц, а оно живёт сорок
/// восемь часов: за пределами окна байты назвать нечем, и отчёт об этом говорит вслух,
/// а не подставляет ноль.
/// </summary>
/// <param name="Within">Интервал отчёта.</param>
/// <param name="Observations">Попыток наблюдения.</param>
/// <param name="Requests">Запросов к источнику.</param>
/// <param name="NotModified">Ответов «не изменилось».</param>
/// <param name="Failed">Отказов.</param>
/// <param name="BytesReceived">Принято байт в пределах окна сырых страниц.</param>
/// <param name="RawPages">Сколько страниц лежит в окне за интервал.</param>
/// <param name="TrafficKnownFrom">С какого момента объём трафика известен; за окном — <see langword="null" />.</param>
public sealed record ResourceUsage(
    TimeRange Within,
    long Observations,
    long Requests,
    long NotModified,
    long Failed,
    long BytesReceived,
    int RawPages,
    DateTimeOffset? TrafficKnownFrom)
{
    /// <summary>Доля ответов «не изменилось» среди состоявшихся попыток.</summary>
    public double NotModifiedFraction =>
        Observations == 0 ? 0d : NotModified / (double)Observations;

    /// <summary>Покрывает ли окно сырых страниц весь интервал отчёта.</summary>
    public bool TrafficCoversWholeInterval =>
        TrafficKnownFrom is { } from && from <= Within.From;
}
