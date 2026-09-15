namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Исход одного цикла: упал ли и была ли работа. Расписание выбирает по нему
/// следующую паузу.
/// </summary>
/// <param name="Failed">Цикл завершился исключением.</param>
/// <param name="DidWork">Цикл сообщил хотя бы один ненулевой счётчик.</param>
public readonly record struct WorkerCycleOutcome(bool Failed, bool DidWork);
