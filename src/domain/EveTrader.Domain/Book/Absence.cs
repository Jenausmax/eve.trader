namespace EveTrader.Domain.Book;

/// <summary>
/// Ордер, пропавший из наблюдения и ждущий подтверждения. Не факт исчезновения, а
/// состояние процесса: источник допускает пропуски, и ордер часто возвращается.
/// </summary>
/// <param name="Order">Ордер, каким его видели в последний раз.</param>
/// <param name="LastSeenAt">Когда его видели.</param>
/// <param name="FirstAbsentAt">Когда впервые не увидели.</param>
/// <param name="Misses">Сколько полных наблюдений подряд его нет.</param>
internal readonly record struct Absence(
    OrderSnapshot Order,
    DateTimeOffset LastSeenAt,
    DateTimeOffset FirstAbsentAt,
    int Misses);
