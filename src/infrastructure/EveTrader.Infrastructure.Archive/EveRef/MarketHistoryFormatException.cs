namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Файл источника не разобрать: нет нужной колонки, не читается значение.
///
/// Отдельный тип нужен, чтобы отличить дефект данных от дефекта кода. Первый — повод
/// пропустить сутки и записать это в лог; второй обязан ронять прогон громко, а не
/// теряться среди пропущенных суток.
/// </summary>
public sealed class MarketHistoryFormatException : Exception
{
    public MarketHistoryFormatException(string message) : base(message)
    {
    }

    public MarketHistoryFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MarketHistoryFormatException()
    {
    }
}
