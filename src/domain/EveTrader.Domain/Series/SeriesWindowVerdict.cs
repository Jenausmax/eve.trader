using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Приговор окну ряда — записывается рядом с точками.
///
/// Без него отсутствие точки двусмысленно: «оборота за окно не было» и «окно не
/// наблюдалось» выглядят одинаково — как отсутствие строки. Приговор снимает
/// двусмысленность: окно допущено, а точки по паре нет — значит событий не было, и это
/// факт о рынке; окно отклонено — причина доступна состоянием покрытия.
/// </summary>
/// <param name="Definition">Определение ряда.</param>
/// <param name="Region">Регион.</param>
/// <param name="Window">Окно с разбором покрытия.</param>
public sealed record SeriesWindowVerdict(SeriesDefinition Definition, RegionId Region, SeriesWindow Window)
{
    public string FactKey =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"series/{Definition.Key}/{Region.Value}/window/{Window.Range.To:yyyyMMddTHHmmssZ}");
}
