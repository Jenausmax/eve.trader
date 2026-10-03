namespace EveTrader.Application.Signals;

/// <summary>Итог записи исходов.</summary>
/// <param name="Written">Порций записано.</param>
/// <param name="AlreadyPresent">Порций уже было: сигнал неизменяем, повтор его не переписывает.</param>
public sealed record SignalRecordingReport(int Written, int AlreadyPresent);
