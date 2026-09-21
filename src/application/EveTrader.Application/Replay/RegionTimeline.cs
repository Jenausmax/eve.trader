using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Replay;

/// <summary>
/// История одного региона в озере: чекпойнты и события, упорядоченные по времени
/// наблюдения.
///
/// Восстановление идёт от ближайшего предшествующего чекпойнта, а не от начала истории
/// региона: через год свёртка всей истории — это миллиарды строк на один запрос, то есть
/// требование записано, а исполнить его нельзя.
/// </summary>
internal sealed class RegionTimeline
{
    private readonly List<(DateTimeOffset At, List<OrderSnapshot> Book)> checkpoints = [];

    private readonly List<(DateTimeOffset At, OrderEvent Event)> events = [];

    private readonly HashSet<DateTimeOffset> baselineMoments = [];

    public static RegionTimeline Of(
        RegionId region,
        IReadOnlyList<FactRow> checkpointRows,
        IReadOnlyList<FactRow> eventRows,
        IReadOnlyList<FactRow> baselineRows)
    {
        ArgumentNullException.ThrowIfNull(checkpointRows);
        ArgumentNullException.ThrowIfNull(eventRows);
        ArgumentNullException.ThrowIfNull(baselineRows);

        var timeline = new RegionTimeline();

        foreach (IGrouping<DateTimeOffset, FactRow> snapshot in checkpointRows
            .Where(row => row.Region == region)
            .GroupBy(row => row.Envelope.KnownAt)
            .OrderBy(group => group.Key))
        {
            timeline.checkpoints.Add((snapshot.Key, [.. snapshot.Select(LakeFacts.CheckpointOrder)]));
        }

        foreach (FactRow row in eventRows.Where(row => row.Region == region))
        {
            // Событие кладётся по времени события, а не по времени получения.
            //
            // Это не придирка, а суть би-темпоральности. Исчезновение происходит, когда
            // ордер пропал, а узнаём мы о нём позже — когда окно подтвердило. Состояние
            // мира на момент определяется тем, что произошло к этому моменту, и
            // раскладка по времени получения дала бы стакан, в котором ордер ещё жив
            // ровно столько, сколько длилось подтверждение.
            //
            // Для интервального события берётся верхняя граница: к ней событие уже
            // случилось наверняка.
            timeline.events.Add((row.Envelope.EventTime.To, LakeFacts.Event(row)));
        }

        timeline.events.Sort((left, right) => left.At.CompareTo(right.At));

        foreach (FactRow row in baselineRows.Where(row => row.Region == region))
        {
            _ = timeline.baselineMoments.Add(row.Envelope.KnownAt);
        }

        return timeline;
    }

    /// <summary>Было ли наблюдение на этот момент базовой линией.</summary>
    public bool IsBaseline(DateTimeOffset at) => baselineMoments.Contains(at);

    /// <summary>
    /// Состав стакана на момент наблюдения: ближайший предшествующий чекпойнт плюс
    /// события, случившиеся к этому моменту.
    ///
    /// «Случившиеся», а не «ставшие известными»: восстановление отвечает на вопрос, как
    /// выглядел стакан тогда, а не что мы о нём знали тогда. Второе тоже осмысленный
    /// вопрос, и на него отвечает чтение на момент времени по времени получения — но это
    /// другой вопрос и другой запрос.
    /// </summary>
    public IReadOnlyList<OrderSnapshot> BookAt(DateTimeOffset at)
    {
        (DateTimeOffset At, List<OrderSnapshot> Book) anchor = checkpoints
            .LastOrDefault(point => point.At <= at);

        List<OrderSnapshot> from = anchor.Book ?? [];

        // Базовая линия сама по себе состав не восстанавливает: её строки лежат в своём
        // наборе, а чекпойнт снимается тем же наблюдением. Поэтому опора — чекпойнт.
        IEnumerable<OrderEvent> after = events
            .Where(moment => moment.At > anchor.At && moment.At <= at)
            .Select(moment => moment.Event);

        return BookReconstruction.Rebuild(from, after);
    }
}
