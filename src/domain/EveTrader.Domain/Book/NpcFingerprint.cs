namespace EveTrader.Domain.Book;

/// <summary>
/// Отпечаток ордера NPC. Обязателен, а не желателен: при пополнении склада NPC
/// переставляют <c>issued</c> без изменения цены, и без фильтра каждое пополнение
/// попало бы в обучение как действие конкурента — причём именно по тем предметам,
/// которыми торгуют новички.
///
/// Признаки берутся совокупностью: ни один по отдельности не различает NPC надёжно.
/// Порог идентификатора подтверждается на архиве в <c>add-orderbook-archive-import</c>.
/// </summary>
/// <param name="MaxOrderId">Идентификаторы ордеров NPC лежат ниже этого порога.</param>
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
    /// Стартовые значения. Уточняются замером на архиве — до него они гипотеза, а не факт.
    /// </summary>
    public static NpcFingerprint Default { get; } = new(
        MaxOrderId: 5_000_000_000L,
        DurationDays: 365,
        RequireFullVolume: true,
        RequireSell: true,
        RoundPriceCents: 0);

    /// <summary>Отпечаток, который не узнаёт никого: для наборов без ордеров NPC.</summary>
    public static NpcFingerprint None { get; } = new(0, 0, false, false, 0);

    /// <summary>
    /// Все признаки разом. Совокупность, а не любой из них: идентификатор ниже порога
    /// бывает и у старых игроцких ордеров, а годовая длительность — у терпеливых.
    /// </summary>
    public bool Matches(in OrderSnapshot order) =>
        order.OrderId < MaxOrderId
        && order.DurationDays == DurationDays
        && (!RequireFullVolume || order.VolumeRemain == order.VolumeTotal)
        && (!RequireSell || !order.IsBuy)
        && (RoundPriceCents <= 0 || order.Price.Cents % RoundPriceCents == 0);
}
