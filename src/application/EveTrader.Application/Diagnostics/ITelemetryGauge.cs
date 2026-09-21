namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Датчик: величина, у которой осмысленно последнее значение, а не сумма.
///
/// Остаток бюджета ошибок — именно такая: сложенные остатки не значат ничего, а по
/// последнему принимается решение продолжать сбор или ждать.
/// </summary>
public interface ITelemetryGauge
{
    /// <summary>Тот же датчик с добавленным тегом. Исходный не меняется.</summary>
    ITelemetryGauge WithTag(string key, object? value);

    void Record(double value);
}
