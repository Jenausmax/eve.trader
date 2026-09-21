namespace EveTrader.Application.Diagnostics;

/// <summary>Гистограмма: распределение величины — длительности, доли, размера.</summary>
public interface ITelemetryHistogram
{
    /// <summary>Та же гистограмма с добавленным тегом. Исходная не меняется.</summary>
    ITelemetryHistogram WithTag(string key, object? value);

    void Record(double value);
}
