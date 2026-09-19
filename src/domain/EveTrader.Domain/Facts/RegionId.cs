namespace EveTrader.Domain.Facts;

/// <summary>Идентификатор региона, как его называет источник.</summary>
public readonly record struct RegionId : IComparable<RegionId>
{
    private RegionId(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static RegionId From(int value) =>
        value > 0 ? new RegionId(value) : throw new ArgumentOutOfRangeException(nameof(value), value, "Идентификатор региона положителен");

    /// <summary>
    /// Упорядочивание по идентификатору. Нужно не для красоты: порядок регионов входит
    /// в порядок строк файла, а тот обязан быть детерминированным — реплей сверяется с
    /// живым прогоном побитово.
    /// </summary>
    public int CompareTo(RegionId other) => Value.CompareTo(other.Value);

    public static bool operator <(RegionId left, RegionId right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(RegionId left, RegionId right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(RegionId left, RegionId right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(RegionId left, RegionId right)
    {
        return left.CompareTo(right) >= 0;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
