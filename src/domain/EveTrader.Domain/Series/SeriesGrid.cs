using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Концы окон ряда внутри интервала.
///
/// Сетка привязана к эпохе UTC, а не к началу интервала: иначе два прогона, начатые с
/// разницей в минуту, посчитали бы точки на разных концах окон, и один и тот же признак
/// разъехался бы на два несовпадающих ряда.
/// </summary>
public static class SeriesGrid
{
    /// <summary>Концы окон с шагом <paramref name="step" />, попадающие в <c>(From, To]</c>.</summary>
    public static IReadOnlyList<DateTimeOffset> Ends(TimeRange interval, TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "Шаг сетки положителен");
        }

        var ends = new List<DateTimeOffset>();
        var first = (interval.From.UtcTicks / step.Ticks) + 1;

        for (var tick = first * step.Ticks; tick <= interval.To.UtcTicks; tick += step.Ticks)
        {
            ends.Add(new DateTimeOffset(tick, TimeSpan.Zero));
        }

        return ends;
    }

    /// <summary>Ближайший конец окна не позже момента — то, что известно на этот момент.</summary>
    public static DateTimeOffset Floor(DateTimeOffset instant, TimeSpan step) =>
        new(instant.UtcTicks / step.Ticks * step.Ticks, TimeSpan.Zero);
}
