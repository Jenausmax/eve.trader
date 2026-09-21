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
            OrderEvent moment = LakeFacts.Event(row);

            timeline.events.Add((EffectiveMomentOf(moment, row.Envelope.KnownAt), moment));
        }

        timeline.events.Sort((left, right) => left.At.CompareTo(right.At));

        foreach (FactRow row in baselineRows.Where(row => row.Region == region))
        {
            _ = timeline.baselineMoments.Add(row.Envelope.KnownAt);
        }

        return timeline;
    }

    /// <summary>
    /// Момент, на который событие следует применять при восстановлении состава.
    ///
    /// Для всех событий это время наблюдения: событие обнаруживается там же, где
    /// проявилось, и порядок наблюдений — это и есть порядок, в котором стакан менялся
    /// на наших глазах. Брать вместо этого время события нельзя: <c>issued</c>
    /// перестановки вполне попадает внутрь интервала, за который собирался предыдущий
    /// снимок, — у архива этот интервал в минуты, — и такая перестановка выпала бы из
    /// восстановления навсегда, оставив ордер со старой ценой.
    ///
    /// Исчезновение — единственное исключение, и по построению. Его время наблюдения —
    /// момент подтверждения окном, а не момент пропажи: мы намеренно ждём несколько
    /// наблюдений, прежде чем поверить. Восстановлению нужна пропажа, иначе ордер живёт
    /// в стакане ровно столько, сколько длилось подтверждение.
    /// </summary>
    public static DateTimeOffset EffectiveMomentOf(OrderEvent moment, DateTimeOffset observedAt) =>
        moment.Kind == OrderEventKind.Disappeared ? moment.EventTime.To : observedAt;

    /// <summary>Было ли наблюдение на этот момент базовой линией.</summary>
    public bool IsBaseline(DateTimeOffset at) => baselineMoments.Contains(at);

    /// <summary>
    /// Состав стакана на момент наблюдения: ближайший предшествующий чекпойнт плюс
    /// события, наблюдённые после него и до этого момента.
    ///
    /// Якорь и события сравниваются по одним часам — по времени наблюдения. Смешение
    /// двух шкал в одном сравнении и было ошибкой, которую поймала приёмка: перестановка
    /// с <c>issued</c> внутри интервала сбора предыдущего снимка оказывалась «раньше»
    /// якоря и выпадала навсегда. См. <see cref="EffectiveMomentOf" /> — там же про
    /// единственное исключение.
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
