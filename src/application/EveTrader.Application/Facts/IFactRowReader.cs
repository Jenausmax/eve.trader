using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Порт плоских строк — единственный путь к данным для признаков, сигналов и обучающих
/// выборок. Хранилище делает отбор, проекцию и отсечение партиций; вычисление происходит
/// снаружи, в домене на C#.
///
/// Граница проходит по потребителю, а не по сложности запроса: признак, посчитанный в SQL
/// для обучения и в C# в бою, расходится, и расхождение выглядит на бэктесте как отличная
/// модель, а в бою как случайная. Агрегаты — в <see cref="Reporting.IOperationalReportReader" />,
/// и этот порт о них не знает.
/// </summary>
public interface IFactRowReader
{
    /// <summary>
    /// Плоские строки набора за интервал. <paramref name="asOf" /> ограничивает выдачу
    /// тем, что было известно системе на этот момент; <see langword="null" /> — последняя
    /// известная версия каждого факта.
    /// </summary>
    IAsyncEnumerable<FactRow> ReadAsync(
        FactSet set,
        TimeRange observed,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken);
}
