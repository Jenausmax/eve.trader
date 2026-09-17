namespace EveTrader.Domain.Facts;

/// <summary>Тип колонки набора. Набор типов узкий намеренно — расширяется по мере надобности.</summary>
public enum FactColumnType
{
    Int64 = 0,
    Double = 1,
    String = 2,
}
