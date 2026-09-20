using EveTrader.Domain.Facts;

namespace EveTrader.Application.Live;

/// <summary>
/// Скользящее окно сырых страниц источника.
///
/// Существует **только для разбора дефектов парсера**. Источником истины не является и
/// стать им не должно: соблазн «перечитать сырьё» возникает ровно тогда, когда в свёртке
/// нашли ошибку, и уступка ему сделала бы окно обязательным навсегда — то есть терабайты
/// в год ради данных, полностью выводимых из чекпойнтов и событий.
///
/// Поэтому у этого порта нет чтения. Не «не понадобилось» — его нет по построению.
/// </summary>
public interface IRawPageArchive
{
    Task StoreAsync(
        RegionId region,
        int page,
        DateTimeOffset observedAt,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);

    /// <summary>Убирает страницы старше окна. Возвращает, сколько удалено.</summary>
    Task<int> SweepAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}
