namespace EveTrader.Domain.Book;

/// <summary>
/// Отпечаток ордера NPC. Обязателен, а не желателен: при пополнении склада NPC
/// переставляют <c>issued</c> без изменения цены, и без фильтра каждое пополнение
/// попало бы в обучение как действие конкурента — причём именно по тем предметам,
/// которыми торгуют новички.
///
/// Состав признаков подтверждён замером на архиве ордербука EVE Ref
/// (<c>add-orderbook-archive-import</c>, снимок 2026-09-17): разделяет длительность.
/// Игрок выбирает длительность из набора 1, 3, 7, 14, 30 и 90 суток — года в этом
/// наборе нет, и все 547 743 ордера с длительностью 365 в снимке шли с полным остатком.
/// Порог идентификатора и сторона сделки в разделении не участвуют: диапазоны
/// идентификаторов NPC и игроков перекрываются почти целиком, а восьмая часть ордеров
/// NPC — покупка.
/// </summary>
/// <param name="MaxOrderId">
/// Идентификаторы ордеров NPC лежат ниже этого порога. Ноль и меньше отключает проверку —
/// на архиве порог оказался неразделяющим, и по умолчанию он выключен.
/// </param>
/// <param name="DurationDays">Длительность ордера NPC.</param>
/// <param name="RequireFullVolume">Остаток равен полному объёму.</param>
/// <param name="RequireSell">Сторона продажи.</param>
/// <param name="RoundPriceCents">Цена кратна этому числу сотых долей ISK; ноль отключает проверку.</param>
public sealed record NpcFingerprint(
    long MaxOrderId,
    short DurationDays,
    bool RequireFullVolume,
    bool RequireSell,
    long RoundPriceCents)
{
    /// <summary>
    /// Значения, подтверждённые замером на архиве: после этого фильтра в снимке не
    /// остаётся ни одного ордера с двинувшимся <c>issued</c> и неизменной ценой.
    /// </summary>
    public static NpcFingerprint Default { get; } = new(
        MaxOrderId: 0,
        DurationDays: 365,
        RequireFullVolume: true,
        RequireSell: false,
        RoundPriceCents: 0);

    /// <summary>
    /// Отпечаток, который не узнаёт никого: для наборов без ордеров NPC. Длительности
    /// −1 не бывает ни у одного ордера, поэтому совпадение невозможно по построению —
    /// нулевая длительность для этого не годится, её несут мгновенные ордера.
    /// </summary>
    public static NpcFingerprint None { get; } = new(0, -1, false, false, 0);

    /// <summary>
    /// Все включённые признаки разом. Совокупность, а не любой из них: годовая
    /// длительность и полный остаток по отдельности встречаются и у игроцких ордеров.
    /// </summary>
    public bool Matches(in OrderSnapshot order) =>
        (MaxOrderId <= 0 || order.OrderId < MaxOrderId)
        && order.DurationDays == DurationDays
        && (!RequireFullVolume || order.VolumeRemain == order.VolumeTotal)
        && (!RequireSell || !order.IsBuy)
        && (RoundPriceCents <= 0 || order.Price.Cents % RoundPriceCents == 0);
}
