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

    /// <summary>
    /// Все версии строк набора за интервал, известные к <paramref name="asOf" />. Выбор
    /// версии на момент — за вызывающим (<see cref="Bitemporal" />).
    ///
    /// Нужен там, где моментов знания много, а читать хочется один раз: бэктест решает в
    /// сотнях моментов подряд, и каждому нужна своя версия каждого факта. Читать по
    /// запросу на момент значило бы перечитывать одни и те же сутки сотни раз.
    /// </summary>
    Task<IReadOnlyList<FactRow>> SelectAsync(
        FactSet set,
        TimeRange observed,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken);
}
