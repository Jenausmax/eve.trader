namespace EveTrader.Domain.Facts;

/// <summary>
/// Идентификатор наблюдения. Связывает строки данных с записью покрытия,
/// которая делает их фактами, и служит ключом идемпотентности: повторная подача
/// того же наблюдения несёт тот же идентификатор.
/// </summary>
public readonly record struct ObservationId
{
    private ObservationId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ObservationId From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Идентификатор наблюдения непуст", nameof(value))
            : new ObservationId(value);

    public override string ToString() => Value;
}
