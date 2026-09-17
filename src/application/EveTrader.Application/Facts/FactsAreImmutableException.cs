using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Операция отклонена, потому что затрагивает подтверждённый факт. UPDATE строки факта
/// уничтожает би-темпоральность: перезаписанное наблюдение делает бэктест лжецом молча.
/// </summary>
public sealed class FactsAreImmutableException : InvalidOperationException
{
    public FactsAreImmutableException(string message) : base(message)
    {
    }

    public FactsAreImmutableException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public FactsAreImmutableException() : base("Подтверждённый факт не изменяется и не удаляется")
    {
    }

    public static FactsAreImmutableException ForRetention(FactSet set) =>
        new($"Срок хранения неприменим к набору '{FactSets.PathSegment(set)}': сырьё хранится бессрочно в пределах материализованных интервалов");

    public static FactsAreImmutableException ForDrop(FactSet set) =>
        new($"Набор '{FactSets.PathSegment(set)}' не удаляется: он не выводим из чего-либо ещё");
}
