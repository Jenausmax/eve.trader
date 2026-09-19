using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Событие жизни ордера.
///
/// Причины исчезновения здесь нет и не будет: источник не различает выкуп, отмену и
/// истечение. Истечение при этом вычислимо — из <see cref="IssuedUnix" /> и
/// <see cref="DurationDays" />, — но как следствие, а не как записанная причина.
/// </summary>
/// <param name="Kind">Вид события.</param>
/// <param name="OrderId">Ордер.</param>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
/// <param name="IsBuy">Сторона.</param>
/// <param name="Price">Цена после события.</param>
/// <param name="PreviousPrice">Цена до события; значима для перестановки.</param>
/// <param name="VolumeRemain">Остаток после события.</param>
/// <param name="FilledVolume">Исполненный объём; значим для наблюдённого исполнения.</param>
/// <param name="EventTime">Когда событие произошло: момент либо интервал.</param>
/// <param name="ObservedAt">Когда мы это увидели.</param>
/// <param name="IssuedUnix">Момент последней правки ордера.</param>
/// <param name="DurationDays">Объявленная длительность ордера.</param>
/// <param name="IsNpc">Ордер несёт отпечаток NPC.</param>
/// <param name="UndersampledStep">
/// Событие получено при шаге наблюдения реже, чем игра допускает правку ордера, — часть
/// перестановок между наблюдениями пропущена безвозвратно.
/// </param>
public readonly record struct OrderEvent(
    OrderEventKind Kind,
    long OrderId,
    int TypeId,
    long LocationId,
    bool IsBuy,
    IskPrice Price,
    IskPrice PreviousPrice,
    long VolumeRemain,
    long FilledVolume,
    EventTime EventTime,
    DateTimeOffset ObservedAt,
    long IssuedUnix,
    short DurationDays,
    bool IsNpc,
    bool UndersampledStep)
{
    /// <summary>
    /// Момент истечения ордера, вычисленный из последней правки и длительности.
    /// Следствие, а не причина: если исчезновение подтверждено не раньше этого момента,
    /// истечение объясняет его — но событие такого утверждения не содержит.
    /// </summary>
    public DateTimeOffset ExpiresAt =>
        DateTimeOffset.FromUnixTimeSeconds(IssuedUnix).AddDays(DurationDays);
}
