using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Когда писать чекпойнт полного состава стакана — раз в сутки на регион.
///
/// Часовой чекпойнт дал бы в двадцать четыре раза больше и не нужен: бэктест не
/// спрашивает точку, он проходит интервал подряд. Суточный стоит около миллиона строк
/// в сутки на полный охват — на фоне событий это шум, зато восстановление любого
/// момента сворачивает не больше суток.
/// </summary>
public sealed class DailyCheckpointPolicy
{
    private readonly Dictionary<RegionId, DateOnly> lastCheckpoint = [];

    /// <summary>
    /// Пора ли снять чекпойнт. Первое наблюдение суток по региону — да, остальные — нет.
    /// </summary>
    public bool ShouldCheckpoint(RegionId region, DateTimeOffset observedAt)
    {
        var day = DateOnly.FromDateTime(observedAt.UtcDateTime);

        return !lastCheckpoint.TryGetValue(region, out DateOnly last) || last != day;
    }

    public void Recorded(RegionId region, DateTimeOffset observedAt) =>
        lastCheckpoint[region] = DateOnly.FromDateTime(observedAt.UtcDateTime);
}
