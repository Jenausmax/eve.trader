namespace EveTrader.Domain.Facts;

/// <summary>
/// Колонка, специфичная для набора фактов. Конверт (<see cref="FactEnvelope" />)
/// одинаков у всех наборов, а состав колонок задаёт тот чейндж, который набор порождает.
/// </summary>
public sealed record FactColumn
{
    private FactColumn(string name, FactColumnType type, Array values)
    {
        Name = name;
        Type = type;
        Values = values;
    }

    public string Name { get; }

    public FactColumnType Type { get; }

    public Array Values { get; }

    public int Length => Values.Length;

    public static FactColumn OfInt64(string name, IReadOnlyList<long> values) =>
        new(Named(name), FactColumnType.Int64, values.ToArray());

    public static FactColumn OfDouble(string name, IReadOnlyList<double> values) =>
        new(Named(name), FactColumnType.Double, values.ToArray());

    public static FactColumn OfString(string name, IReadOnlyList<string> values) =>
        new(Named(name), FactColumnType.String, values.ToArray());

    public static string Named(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Имя колонки непусто", nameof(name))
            : name;
}
