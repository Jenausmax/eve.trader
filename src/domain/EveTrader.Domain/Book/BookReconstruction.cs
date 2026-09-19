namespace EveTrader.Domain.Book;

/// <summary>
/// Восстановление состава стакана из чекпойнта и последующих событий.
///
/// В цепочку входят только полные наблюдения: по частичному нельзя сказать, чего в
/// стакане не было, а значит и состав из него не выводится. Свёртка идёт от ближайшего
/// предшествующего чекпойнта, а не от начала истории региона.
/// </summary>
public static class BookReconstruction
{
    /// <summary>
    /// Применяет события к составу чекпойнта. События подаются в порядке наблюдения;
    /// порядок результата — по идентификатору ордера, чтобы два восстановления одного
    /// момента совпадали побитово.
    /// </summary>
    public static IReadOnlyList<OrderSnapshot> Rebuild(
        IReadOnlyList<OrderSnapshot> checkpoint,
        IEnumerable<OrderEvent> events)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(events);

        var book = checkpoint.ToDictionary(static order => order.OrderId);

        foreach (OrderEvent moment in events)
        {
            Apply(book, in moment);
        }

        return [.. book.Values.OrderBy(static order => order.OrderId)];
    }

    public static void Apply(Dictionary<long, OrderSnapshot> book, in OrderEvent moment)
    {
        ArgumentNullException.ThrowIfNull(book);

        switch (moment.Kind)
        {
            case OrderEventKind.Baseline:
            case OrderEventKind.Appeared:
                book[moment.OrderId] = OrderFromEvent.Rebuild(in moment);

                break;

            case OrderEventKind.Repriced:
            case OrderEventKind.NpcRestock:
            case OrderEventKind.ObservedFill:
                // Событие несёт состояние ордера после себя — этого достаточно, чтобы
                // обновить состав, не зная, что было до.
                book[moment.OrderId] = book.TryGetValue(moment.OrderId, out OrderSnapshot before)
                    ? OrderFromEvent.Rebuild(in moment, before.VolumeTotal)
                    : OrderFromEvent.Rebuild(in moment);

                break;

            case OrderEventKind.Disappeared:
                _ = book.Remove(moment.OrderId);

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(moment), moment.Kind, "Неизвестный вид события");
        }
    }
}
