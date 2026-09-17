using System.Globalization;

namespace EveTrader.Domain.Facts;

/// <summary>
/// Время, к которому относится факт: либо точный момент, либо интервал, в котором
/// событие произошло. <see cref="Kind" /> различает их явно — тип не позволяет
/// прочитать интервал как момент, не заметив этого.
/// </summary>
public readonly record struct EventTime
{
    private EventTime(EventTimeKind kind, DateTimeOffset from, DateTimeOffset to)
    {
        Kind = kind;
        From = from;
        To = to;
    }

    public EventTimeKind Kind { get; }

    /// <summary>Начало интервала либо сам момент.</summary>
    public DateTimeOffset From { get; }

    /// <summary>Конец интервала; для момента совпадает с <see cref="From" />.</summary>
    public DateTimeOffset To { get; }

    public static EventTime At(DateTimeOffset instant) =>
        new(EventTimeKind.Instant, instant, instant);

    public static EventTime Between(DateTimeOffset from, DateTimeOffset to) =>
        to >= from
            ? new EventTime(EventTimeKind.Interval, from, to)
            : throw new ArgumentOutOfRangeException(nameof(to), to, "Конец интервала не раньше начала");

    /// <summary>
    /// Момент, если он известен точно. Для интервала — <see langword="null" />:
    /// потребитель, которому нужен момент, обязан столкнуться с его отсутствием.
    /// </summary>
    public DateTimeOffset? Instant => Kind == EventTimeKind.Instant ? From : null;

    public override string ToString() => Kind == EventTimeKind.Instant
        ? string.Create(CultureInfo.InvariantCulture, $"at {From:O}")
        : string.Create(CultureInfo.InvariantCulture, $"between {From:O} and {To:O}");
}
