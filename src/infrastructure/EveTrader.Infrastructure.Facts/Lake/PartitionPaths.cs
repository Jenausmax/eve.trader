using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>Разбор партиционных сегментов пути обратно в значения.</summary>
public static class PartitionPaths
{
    public static DateOnly? ObservedDateOf(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var segment = Segment(file, FactColumnNames.ObservedDate + "=");

        return segment is not null
            && DateOnly.TryParseExact(segment, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    public static RegionId? RegionOf(string file)
    {
        var segment = Segment(file, FactColumnNames.Region + "=");

        return segment is not null && int.TryParse(segment, CultureInfo.InvariantCulture, out var value)
            ? RegionId.From(value)
            : null;
    }

    /// <summary>
    /// Попадает ли суточная партиция файла в интервал. Сравнение по партиции, а не по
    /// строкам: отсечение лишнего по путям — то, ради чего партиционирование и заведено.
    /// </summary>
    public static bool InRange(string file, TimeRange observed)
    {
        DateOnly? date = ObservedDateOf(file);

        if (date is not { } observedDate)
        {
            return false;
        }

        var from = new DateTimeOffset(observedDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return TimeRange.Between(from, from.AddDays(1)).Overlaps(observed);
    }

    public static string? Segment(string file, string prefix)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal))
            ?[prefix.Length..];
    }
}
