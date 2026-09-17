namespace EveTrader.Domain.Facts;

/// <summary>
/// Версия статических игровых данных, от которой зависит вычисление или интерпретация
/// факта. Несётся на строке: пересчёт после обновления статики даёт новые строки с новой
/// версией, а прежние сохраняют свою.
/// </summary>
public readonly record struct StaticDataVersion
{
    private StaticDataVersion(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>Факт, не зависящий от статических данных.</summary>
    public static StaticDataVersion None { get; } = new(string.Empty);

    public static StaticDataVersion From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Версия статических данных непуста", nameof(value))
            : new StaticDataVersion(value);

    public override string ToString() => Value.Length == 0 ? "(none)" : Value;
}
