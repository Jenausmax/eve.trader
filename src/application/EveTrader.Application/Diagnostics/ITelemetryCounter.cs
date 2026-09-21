namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Счётчик: монотонно растущая величина.
///
/// Теги навешиваются <see cref="WithTag" />, а не перегрузкой <c>Add</c> со словарём:
/// словарь на каждый вызов — аллокация на горячем пути, а вызывающая сторона всё равно
/// пишет теги по одному.
/// </summary>
public interface ITelemetryCounter
{
    /// <summary>Тот же счётчик с добавленным тегом. Исходный не меняется.</summary>
    ITelemetryCounter WithTag(string key, object? value);

    void Add(double value);
}
