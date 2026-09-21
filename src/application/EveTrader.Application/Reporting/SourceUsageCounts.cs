namespace EveTrader.Application.Reporting;

/// <summary>
/// Расход источника за интервал по журналу покрытия.
/// </summary>
/// <param name="Observations">Попыток наблюдения.</param>
/// <param name="Requests">
/// Запросов к источнику. Считается как страниц получено, но не меньше одного на попытку:
/// ответ «не изменилось» и отказ страниц не приносят, а запрос на них ушёл.
/// </param>
/// <param name="NotModified">Ответов «не изменилось».</param>
/// <param name="Failed">Отказов.</param>
public readonly record struct SourceUsageCounts(long Observations, long Requests, long NotModified, long Failed);
