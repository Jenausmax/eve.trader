using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Свёртка последовательных наблюдений одного региона в события жизни ордера.
///
/// Наблюдатель держит состояние, и это не прихоть: исчезновение — не свойство пары
/// снимков, а вывод по нескольким подряд. Источник допускает пропуски, и ордер,
/// пропавший на один снимок, возвращается в следующем; исчезновение по единственному
/// сравнению — систематическая выдумка на дефектах источника, и выдумка эта выглядит
/// ровно как исполнение.
///
/// Само состояние живёт в <see cref="ObserverState" />, разметка — в
/// <see cref="OrderDiff" />. Здесь только порядок шагов одного наблюдения.
/// </summary>
public sealed class RegionObserver(RegionId region, DiffOptions options, FeatureOptions featureOptions)
{
    private readonly ObserverState state = new(options, featureOptions);

    public RegionId Region { get; } = region;

    public bool HasBaseline => state.HasBaseline;

    public int PendingDisappearances => state.Absences.Count;

    public ObservationOutcome Observe(ReadOnlySpan<OrderSnapshot> orders, ObservationMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        if (meta.Region != Region)
        {
            throw new ArgumentException($"Наблюдатель региона {Region} получил наблюдение по {meta.Region}", nameof(meta));
        }

        var events = new List<OrderEvent>();

        state.Features.Reset();

        if (!state.HasBaseline || meta.IsBaseline)
        {
            OrderDiff.EmitBaseline(state, orders, meta, events);
            OrderDiff.Remember(state, orders, meta);

            return new ObservationOutcome(
                events, state.Features.Build(meta, meta.Observation), 0, orders.Length, state.Absences.Count);
        }

        var undersampled = !options.IsCompleteByReprice(meta.Step);
        var sourceGaps = 0;

        state.CurrentIndex.Reset(orders.Length);

        for (var slot = 0; slot < orders.Length; slot++)
        {
            state.CurrentIndex.Add(orders[slot].OrderId, slot);
        }

        foreach (ref readonly OrderSnapshot order in orders)
        {
            // Признаки собираются здесь же: стакан уже в руках, второй проход не нужен.
            state.Features.Add(in order);

            // Ордер вернулся после отсутствия — это дефект источника, а не появление.
            if (state.Absences.Remove(order.OrderId))
            {
                sourceGaps++;
            }

            if (state.BaselineIndex.TryGet(order.OrderId, out var previousSlot))
            {
                OrderDiff.Compare(state, in state.Baseline[previousSlot], in order, meta, undersampled, events);
            }
            else
            {
                events.Add(OrderDiff.Appeared(state, in order, meta, undersampled));
            }
        }

        // Частичное наблюдение об отсутствии ордера не говорит ничего: его там просто
        // не спрашивали. Счётчик окна не двигается, кандидаты не заводятся, и базой
        // такое наблюдение не становится.
        if (meta.IsComplete)
        {
            OrderDiff.TrackAbsences(state, meta, events);
            OrderDiff.Remember(state, orders, meta);
        }

        return new ObservationOutcome(
            events, state.Features.Build(meta, meta.Observation), sourceGaps, orders.Length, state.Absences.Count);
    }
}
