using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Coverage;

/// <summary>
/// Непокрытая часть запрошенного интервала и причина непокрытия. Восполнимость —
/// не свойство пробела как такового, а ответ на вопрос, публикует ли источник этот
/// интервал наверху.
/// </summary>
/// <param name="Range">Границы пробела.</param>
/// <param name="State">Почему не покрыт.</param>
public sealed record CoverageGap(TimeRange Range, CoverageState State)
{
    /// <summary>Пробел восполним загрузкой.</summary>
    public bool Replenishable => State == CoverageState.NotMaterialized;
}
