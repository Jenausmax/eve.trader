namespace EveTrader.Domain.Facts;

/// <summary>Идентификатор региона, как его называет источник.</summary>
public readonly record struct RegionId
{
    private RegionId(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static RegionId From(int value) =>
        value > 0 ? new RegionId(value) : throw new ArgumentOutOfRangeException(nameof(value), value, "Идентификатор региона положителен");

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
