using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Coverage;

/// <summary>
/// Запись журнала покрытия — одна попытка наблюдения региона. Она же делает строки
/// данных фактами: строка, на которую не ссылается запись покрытия, фактом не является.
/// </summary>
/// <param name="Observation">Наблюдение, к которому относятся строки данных.</param>
/// <param name="Region">Наблюдавшийся регион.</param>
/// <param name="Collected">Границы интервала сбора: атомарного среза источник не даёт.</param>
/// <param name="Outcome">Исход попытки.</param>
/// <param name="PagesReceived">Сколько страниц получено.</param>
/// <param name="PagesExpected">Сколько страниц объявил источник.</param>
/// <param name="OrderCount">Сколько ордеров увидено.</param>
/// <param name="Source">Источник наблюдения.</param>
/// <param name="ObservationStep">Объявленный источником шаг между наблюдениями.</param>
/// <param name="SourceGaps">Сколько ордеров пропало и вернулось — дефекты источника.</param>
/// <param name="FailureReason">Причина отказа; заполнена только при <see cref="CoverageOutcome.Failure" />.</param>
/// <param name="KnownAt">Когда система узнала о наблюдении.</param>
public sealed record CoverageEntry(
    ObservationId Observation,
    RegionId Region,
    TimeRange Collected,
    CoverageOutcome Outcome,
    int PagesReceived,
    int PagesExpected,
    int OrderCount,
    string Source,
    TimeSpan ObservationStep,
    int SourceGaps,
    string? FailureReason,
    DateTimeOffset KnownAt)
{
    /// <summary>
    /// Покрывает ли запись свой интервал. Отказ не покрывает: источник ничего не сказал
    /// о рынке, и выдать это за «изменений не было» — соврать.
    /// </summary>
    public bool Covers => Outcome is not CoverageOutcome.Failure;

    /// <summary>Полное наблюдение — то, на которое можно опираться при выводе об исчезновении.</summary>
    public bool IsComplete => Outcome is CoverageOutcome.Success or CoverageOutcome.NotModified;
}
