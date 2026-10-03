namespace EveTrader.Domain.Series;

/// <summary>Пара «тип и локация» со стороной стакана — то, по чему копится величина ряда.</summary>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
/// <param name="Side">Сторона, к которой относится величина.</param>
public readonly record struct SeriesKey(int TypeId, long LocationId, SeriesSide Side) : IComparable<SeriesKey>
{
    /// <summary>
    /// Порядок точек ряда: тип, локация, сторона. Явный, а не порядок обхода словаря —
    /// тот зависит от истории вставок, и два прогона одного интервала дали бы разный
    /// порядок строк.
    /// </summary>
    public int CompareTo(SeriesKey other)
    {
        var byType = TypeId.CompareTo(other.TypeId);

        if (byType != 0)
        {
            return byType;
        }

        var byLocation = LocationId.CompareTo(other.LocationId);

        return byLocation != 0 ? byLocation : ((int)Side).CompareTo((int)other.Side);
    }

    public static bool operator <(SeriesKey left, SeriesKey right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(SeriesKey left, SeriesKey right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(SeriesKey left, SeriesKey right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(SeriesKey left, SeriesKey right)
    {
        return left.CompareTo(right) >= 0;
    }
}
