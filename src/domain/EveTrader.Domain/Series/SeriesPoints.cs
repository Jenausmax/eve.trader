namespace EveTrader.Domain.Series;

/// <summary>Отбор точек ряда под разных потребителей.</summary>
public static class SeriesPoints
{
    /// <summary>
    /// Точки, годные для обучения: помеченные неполными в выборку не попадают.
    ///
    /// Отбор живёт здесь, а не в месте формирования выборки, чтобы забыть о нём можно
    /// было только явно. Неполная точка занижена на неизвестную долю, и модель,
    /// обученная на таких, объясняет дефекты сбора, а не рынок.
    /// </summary>
    public static IEnumerable<SeriesPoint> ForTraining(IEnumerable<SeriesPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return points.Where(static point => !point.Incomplete);
    }
}
