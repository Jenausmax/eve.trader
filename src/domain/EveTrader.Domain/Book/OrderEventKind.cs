namespace EveTrader.Domain.Book;

/// <summary>
/// Виды событий жизни ордера. Шесть, а не «ордер изменился»: три из них иначе смешались
/// бы с четвёртым и отравили обучение.
/// </summary>
public enum OrderEventKind
{
    /// <summary>Первое наблюдение региона после включения. Калибровка, не рыночное событие.</summary>
    Baseline = 0,

    /// <summary>Идентификатор отсутствовал в предыдущем полном наблюдении.</summary>
    Appeared = 1,

    /// <summary>Владелец переставил цену. Время события известно точно — из <c>issued</c>.</summary>
    Repriced = 2,

    /// <summary>Остаток уменьшился при неподвижном <c>issued</c> — сделка по этому ордеру.</summary>
    ObservedFill = 3,

    /// <summary>Пополнение склада NPC. Не действие конкурента.</summary>
    NpcRestock = 4,

    /// <summary>Ордер отсутствует, подтверждено окном. Причина неизвестна.</summary>
    Disappeared = 5,
}
