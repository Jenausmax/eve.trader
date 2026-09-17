namespace EveTrader.Domain.Facts;

/// <summary>
/// Алгебра интервалов: слияние пересекающихся и вычитание покрытого. Нужна затем, что
/// покрытие считается не «есть записи или нет», а по долям интервала — наблюдения
/// приходят кусками и пересекаются.
/// </summary>
public static class TimeRanges
{
    /// <summary>Сливает пересекающиеся и смежные интервалы в непересекающийся набор.</summary>
    public static IReadOnlyList<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var ordered = ranges.OrderBy(static range => range.From).ToList();
        var merged = new List<TimeRange>(ordered.Count);

        foreach (TimeRange range in ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(range);
                continue;
            }

            TimeRange last = merged[^1];

            if (range.From <= last.To)
            {
                merged[^1] = TimeRange.Between(last.From, range.To > last.To ? range.To : last.To);
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>Возвращает части <paramref name="range" />, не покрытые ни одним из вычитаемых.</summary>
    public static IReadOnlyList<TimeRange> Subtract(TimeRange range, IEnumerable<TimeRange> subtracted)
    {
        ArgumentNullException.ThrowIfNull(subtracted);

        var remainder = new List<TimeRange> { range };

        foreach (TimeRange cut in Merge(subtracted))
        {
            var next = new List<TimeRange>(remainder.Count + 1);

            foreach (TimeRange piece in remainder)
            {
                if (!piece.Overlaps(cut))
                {
                    next.Add(piece);
                    continue;
                }

                if (piece.From < cut.From)
                {
                    next.Add(TimeRange.Between(piece.From, cut.From));
                }

                if (cut.To < piece.To)
                {
                    next.Add(TimeRange.Between(cut.To, piece.To));
                }
            }

            remainder = next;
        }

        return remainder;
    }

    /// <summary>Пересечение интервала с набором; пустое пересечение опускается.</summary>
    public static IReadOnlyList<TimeRange> Intersect(TimeRange range, IEnumerable<TimeRange> others)
    {
        ArgumentNullException.ThrowIfNull(others);

        var intersections = new List<TimeRange>();

        foreach (TimeRange other in others)
        {
            if (!range.Overlaps(other))
            {
                continue;
            }

            DateTimeOffset from = range.From > other.From ? range.From : other.From;
            DateTimeOffset to = range.To < other.To ? range.To : other.To;

            intersections.Add(TimeRange.Between(from, to));
        }

        return Merge(intersections);
    }

    /// <summary>Суммарная длительность набора интервалов после слияния.</summary>
    public static TimeSpan TotalDuration(IEnumerable<TimeRange> ranges) =>
        Merge(ranges).Aggregate(TimeSpan.Zero, static (total, range) => total + range.Duration);
}
