namespace EveTrader.Domain.Signals;

/// <summary>
/// Имя набора параметров правила. Несётся на каждом сигнале и на каждом отчёте о
/// прогоне: без него два сигнала, порождённые разными порогами, неразличимы, а оценка
/// качества смешивает их в одну кучу и считает знаменатель по чужому правилу.
/// </summary>
public readonly record struct ParameterSetName
{
    private ParameterSetName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ParameterSetName From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Имя набора параметров непусто", nameof(value))
            : new ParameterSetName(value);

    public override string ToString() => Value;
}
