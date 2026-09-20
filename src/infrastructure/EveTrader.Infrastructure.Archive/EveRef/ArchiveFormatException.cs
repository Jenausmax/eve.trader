namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Файл архива не разобрать: нет нужной колонки, не читается значение.
///
/// Отдельный тип нужен, чтобы отличить дефект данных от дефекта кода. Первый — повод
/// пропустить файл и записать это в лог; второй обязан ронять прогон громко, а не
/// теряться среди пропущенных суток.
///
/// Тип общий на все наборы архива: дневная история и снимки стакана приходят от одного
/// источника, и разбираться с их дефектами надо одинаково.
/// </summary>
public sealed class ArchiveFormatException : Exception
{
    public ArchiveFormatException(string message) : base(message)
    {
    }

    public ArchiveFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ArchiveFormatException()
    {
    }
}
