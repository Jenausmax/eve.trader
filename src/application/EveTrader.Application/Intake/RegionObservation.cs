using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Intake;

/// <summary>
/// Наблюдение стакана одного региона — единица, которой обмениваются все источники.
///
/// Форма общая намеренно: живой ESI, импорт архива и реплей собственного озера подают
/// именно её, и стадии после приёма не имеют ни одного способа различить, откуда она
/// пришла. Единственное, чем потребитель вправе различать происхождение, — объявленный
/// <see cref="Step" />, и он же доезжает до записи покрытия.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Observation">Идентификатор наблюдения; он же ключ идемпотентности.</param>
/// <param name="Collected">Границы интервала сбора.</param>
/// <param name="Outcome">Исход: наблюдение состоялось целиком, частично, не менялось или отказало.</param>
/// <param name="Step">Объявленный источником шаг между наблюдениями.</param>
/// <param name="Orders">Ордера; пусто при отказе и при «не изменилось».</param>
/// <param name="PagesReceived">Сколько страниц получено.</param>
/// <param name="PagesExpected">Сколько объявил источник.</param>
/// <param name="IsBaseline">
/// Источник знает, что это первое наблюдение региона. Приём может решить так и сам —
/// по тому, что наблюдателя ещё нет, — но охват знает больше: регион, вернувшийся
/// в охват после паузы, наблюдается впервые, даже если наблюдатель жив.
/// </param>
/// <param name="FailureReason">Причина отказа.</param>
public sealed record RegionObservation(
    RegionId Region,
    ObservationId Observation,
    TimeRange Collected,
    CoverageOutcome Outcome,
    TimeSpan Step,
    IReadOnlyList<OrderSnapshot> Orders,
    int PagesReceived,
    int PagesExpected,
    bool IsBaseline,
    string? FailureReason)
{
    /// <summary>Наблюдение, по которому можно судить о составе стакана.</summary>
    public bool CarriesBook => Outcome is CoverageOutcome.Success or CoverageOutcome.Partial;

    /// <summary>
    /// Полное наблюдение. «Не изменилось» полно: источник прямо сказал, что стакан тот же.
    /// </summary>
    public bool IsComplete => Outcome is CoverageOutcome.Success or CoverageOutcome.NotModified;
}
