using System.Globalization;

namespace EveTrader.Domain.Facts;

/// <summary>
/// Полуинтервал времени <c>[From, To)</c>. Полуоткрытость не косметика: смежные
/// наблюдения не должны пересекаться по границе, иначе доля покрытого времени
/// считается с двойным учётом.
/// </summary>
public readonly record struct TimeRange
{
    private TimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    public TimeSpan Duration => To - From;

    public static TimeRange Between(DateTimeOffset from, DateTimeOffset to) =>
        to > from
            ? new TimeRange(from, to)
            : throw new ArgumentOutOfRangeException(nameof(to), to, "Конец интервала строго позже начала");

    public bool Contains(DateTimeOffset instant) => instant >= From && instant < To;

    public bool Overlaps(TimeRange other) => From < other.To && other.From < To;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{From:O}, {To:O})");
}
