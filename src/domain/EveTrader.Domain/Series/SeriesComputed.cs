namespace EveTrader.Domain.Series;

/// <summary>Посчитанные ряды: приговоры окнам и точки — в детерминированном порядке.</summary>
/// <param name="Windows">Приговор каждому окну, включая отклонённые.</param>
/// <param name="Points">Точки допущенных окон.</param>
public sealed record SeriesComputed(
    IReadOnlyList<SeriesWindowVerdict> Windows,
    IReadOnlyList<SeriesPoint> Points);
