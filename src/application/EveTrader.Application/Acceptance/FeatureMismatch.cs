namespace EveTrader.Application.Acceptance;

/// <summary>
/// Расхождение перестройки на одном наблюдении.
///
/// Разбивка по моментам, а не одно число на прогон: «сошлось на 99.8 %» и «сошлось везде,
/// кроме последнего наблюдения» — разные диагнозы, и лечатся они по-разному.
/// </summary>
/// <param name="Moment">Момент наблюдения.</param>
/// <param name="Recorded">Признаков записано исходным прогоном.</param>
/// <param name="Rebuilt">Признаков дала перестройка.</param>
/// <param name="Missing">Записанных признаков, которых перестройка не дала.</param>
/// <param name="Extra">Перестроенных признаков, которых нет среди записанных.</param>
public sealed record FeatureMismatch(
    DateTimeOffset Moment,
    int Recorded,
    int Rebuilt,
    int Missing,
    int Extra);
