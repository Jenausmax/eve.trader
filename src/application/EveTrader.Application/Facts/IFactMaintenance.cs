using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Обслуживание озера: уборка неподтверждённого и удаление производного. Всё, что здесь
/// разрешено, разрешено потому, что не трогает факты.
/// </summary>
public interface IFactMaintenance
{
    /// <summary>
    /// Убирает строки, не подтверждённые покрытием. Неизменяемость не нарушается:
    /// такие строки фактами не стали.
    /// </summary>
    Task<int> SweepUnconfirmedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Применяет срок хранения. Для сырья отклоняется: события, базовые линии,
    /// чекпойнты, дневная история и покрытие хранятся бессрочно в пределах
    /// материализованных интервалов.
    /// </summary>
    Task ApplyRetentionAsync(FactSet set, TimeSpan maxAge, CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет производный набор за интервал. Разрешено ровно потому, что производное
    /// перестраивается из сырья без обращения к внешнему источнику.
    /// </summary>
    Task<int> DropDerivedAsync(FactSet set, TimeRange observed, CancellationToken cancellationToken);
}
