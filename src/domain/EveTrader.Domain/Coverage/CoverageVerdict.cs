using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Coverage;

/// <summary>
/// Ответ на вопрос о покрытии региона за интервал. Несёт не только состояние, но и
/// перечень пробелов: «наблюдалось на 90 %» и «наблюдалось целиком» — разные ответы,
/// и потребитель обязан их различать.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Requested">Запрошенный интервал.</param>
/// <param name="State">Худшее из состояний пробелов; <see cref="CoverageState.Observed" />, если пробелов нет.</param>
/// <param name="CoveredFraction">Доля покрытого времени, от 0 до 1.</param>
/// <param name="Gaps">Пробелы с причинами.</param>
/// <param name="SourceGaps">Сколько ордеров пропало и вернулось за интервал.</param>
/// <param name="PartialObservations">Сколько наблюдений получили не все страницы.</param>
/// <param name="UnchangedObservations">Сколько раз источник ответил «не изменилось».</param>
public sealed record CoverageVerdict(
    RegionId Region,
    TimeRange Requested,
    CoverageState State,
    double CoveredFraction,
    IReadOnlyList<CoverageGap> Gaps,
    int SourceGaps,
    int PartialObservations,
    int UnchangedObservations)
{
    /// <summary>
    /// Все пробелы восполнимы загрузкой. Пустой перечень пробелов тоже считается
    /// восполнимым — восполнять нечего.
    /// </summary>
    public bool Replenishable => Gaps.All(static gap => gap.Replenishable);

    /// <summary>
    /// Отсутствие изменений — факт о рынке, а не пробел: интервал покрыт целиком и
    /// источник сообщил, что данные не менялись.
    /// </summary>
    public bool UnchangedIsFact => State == CoverageState.Observed && UnchangedObservations > 0;
}
