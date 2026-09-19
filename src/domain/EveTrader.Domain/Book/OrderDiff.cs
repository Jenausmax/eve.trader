using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Разметка событий жизни ордера. Чистые функции над состоянием
/// <see cref="ObserverState" />: наблюдатель их только вызывает.
/// </summary>
internal static class OrderDiff
{
    /// <summary>
    /// Базовая линия: первое наблюдение региона после включения в охват. Калибровка, а
    /// не рыночное событие, поэтому время события — интервал наблюдения, а не момент из
    /// <c>issued</c>: утверждать, что ордера появились сейчас, было бы неправдой.
    /// </summary>
    public static void EmitBaseline(
        ObserverState state,
        ReadOnlySpan<OrderSnapshot> orders,
        ObservationMeta meta,
        List<OrderEvent> events)
    {
        var span = EventTime.Between(meta.Collected.From, meta.Collected.To);

        foreach (ref readonly OrderSnapshot order in orders)
        {
            state.Features.Add(in order);
            events.Add(Event(state, OrderEventKind.Baseline, in order, span, meta, IskPrice.Zero, 0, false));
        }
    }

    /// <summary>
    /// Ведёт кандидатов на исчезновение. Реестр кандидатов живёт отдельно от базы
    /// наблюдения намеренно: база сменяется каждым полным наблюдением, и пропавший
    /// ордер из неё уходит сразу же — считать по ней значило бы забывать кандидата
    /// ровно тогда, когда он начал отсутствовать.
    /// </summary>
    public static void TrackAbsences(ObserverState state, ObservationMeta meta, List<OrderEvent> events)
    {
        // Кандидаты, которые не вернулись: вернувшихся сняли при обходе ордеров.
        foreach (var orderId in state.Absences.Keys.ToList())
        {
            Absence absence = state.Absences[orderId] with { Misses = state.Absences[orderId].Misses + 1 };

            if (absence.Misses >= state.Options.DisappearanceWindow)
            {
                _ = state.Absences.Remove(orderId);
                events.Add(Disappeared(state, in absence, meta));
            }
            else
            {
                state.Absences[orderId] = absence;
            }
        }

        // Новые кандидаты: были в прошлом полном наблюдении, в этом их нет.
        for (var slot = 0; slot < state.BaselineCount; slot++)
        {
            ref readonly OrderSnapshot gone = ref state.Baseline[slot];

            if (state.CurrentIndex.TryGet(gone.OrderId, out _) || state.Absences.ContainsKey(gone.OrderId))
            {
                continue;
            }

            var absence = new Absence(gone, state.BaselineMeta!.Collected.To, meta.Collected.From, 1);

            if (absence.Misses >= state.Options.DisappearanceWindow)
            {
                events.Add(Disappeared(state, in absence, meta));
            }
            else
            {
                state.Absences[gone.OrderId] = absence;
            }
        }
    }

    /// <summary>Сравнивает ордер с тем, каким он был в прошлом полном наблюдении.</summary>
    public static void Compare(
        ObserverState state,
        in OrderSnapshot before,
        in OrderSnapshot after,
        ObservationMeta meta,
        bool undersampled,
        List<OrderEvent> events)
    {
        if (before.IssuedUnix != after.IssuedUnix)
        {
            // Правка ордера: её момент источник сообщает точно.
            //
            // Цена при этом могла и не измениться. У ордера NPC это пополнение склада;
            // у остальных — перестановка на ту же цену, то есть действие владельца, и
            // выбрасывать его нельзя. Седьмого вида события ради этого случая не
            // заводится: шесть видов — утверждение спеки.
            var at = EventTime.At(after.Issued);

            var isNpc = state.Options.Npc.Matches(in after);
            OrderEventKind kind = isNpc && before.Price == after.Price
                ? OrderEventKind.NpcRestock
                : OrderEventKind.Repriced;

            events.Add(Event(state, kind, in after, at, meta, before.Price, 0, undersampled));

            return;
        }

        if (after.VolumeRemain < before.VolumeRemain)
        {
            // Остаток уменьшился при неподвижном issued — сделка по этому ордеру.
            // Момента источник не сообщает, поэтому честно пишется интервал.
            var between = EventTime.Between(state.BaselineMeta!.Collected.From, meta.Collected.To);

            events.Add(Event(
                state,
                OrderEventKind.ObservedFill,
                in after,
                between,
                meta,
                before.Price,
                before.VolumeRemain - after.VolumeRemain,
                undersampled));
        }

        // Ордер без изменений события не порождает.
    }

    /// <summary>Появление ордера, которого не было в предыдущем полном наблюдении.</summary>
    public static OrderEvent Appeared(
        ObserverState state,
        in OrderSnapshot order,
        ObservationMeta meta,
        bool undersampled)
    {
        // Новый ордер нельзя отредактировать в первые минуты жизни, поэтому при
        // достаточно частом наблюдении issued у впервые увиденного ордера — это момент
        // создания. При более редком шаге такой гарантии нет, и момента мы не знаем.
        EventTime when = undersampled
            ? EventTime.Between(state.BaselineMeta!.Collected.From, meta.Collected.To)
            : EventTime.At(order.Issued);

        return Event(state, OrderEventKind.Appeared, in order, when, meta, IskPrice.Zero, 0, undersampled);
    }

    /// <summary>Подтверждённое исчезновение. Причины не несёт — источник её не сообщает.</summary>
    public static OrderEvent Disappeared(ObserverState state, in Absence absence, ObservationMeta meta)
    {
        // Время события — интервал между последним наблюдением ордера и первым его
        // отсутствием; время наблюдения — момент подтверждения.
        var between = EventTime.Between(absence.LastSeenAt, absence.FirstAbsentAt);
        OrderSnapshot gone = absence.Order;

        return Event(state, OrderEventKind.Disappeared, in gone, between, meta, gone.Price, 0, false);
    }

    public static OrderEvent Event(
        ObserverState state,
        OrderEventKind kind,
        in OrderSnapshot order,
        EventTime when,
        ObservationMeta meta,
        IskPrice previousPrice,
        long filled,
        bool undersampled) =>
        new(
            kind,
            order.OrderId,
            order.TypeId,
            order.LocationId,
            order.IsBuy,
            order.Price,
            previousPrice,
            order.VolumeRemain,
            filled,
            when,
            meta.Collected.To,
            order.IssuedUnix,
            order.DurationDays,
            state.Options.Npc.Matches(in order),
            undersampled);

    /// <summary>Запоминает наблюдение как базу для следующего диффа.</summary>
    public static void Remember(ObserverState state, ReadOnlySpan<OrderSnapshot> orders, ObservationMeta meta)
    {
        if (state.Baseline.Length < orders.Length)
        {
            state.Baseline = new OrderSnapshot[HashHelpers.PowerOfTwoAtLeast(Math.Max(16, orders.Length))];
        }

        orders.CopyTo(state.Baseline);
        state.BaselineCount = orders.Length;
        state.BaselineMeta = meta;

        state.BaselineIndex.Reset(orders.Length);

        for (var slot = 0; slot < orders.Length; slot++)
        {
            state.BaselineIndex.Add(orders[slot].OrderId, slot);
        }
    }
}
