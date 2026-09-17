namespace EveTrader.Domain.Facts;

/// <summary>
/// Что источник публикует наверху — по набору фактов и глубине. Отличает восполнимый
/// пробел от безвозвратного: интервал, которого нет ни локально, ни здесь, утрачен.
///
/// Это данные, а не порт: домен портов не содержит, каталог подаётся аргументом.
/// </summary>
public sealed record UpstreamCatalog
{
    private UpstreamCatalog(IReadOnlyDictionary<FactSet, TimeRange> published)
    {
        Published = published;
    }

    public IReadOnlyDictionary<FactSet, TimeRange> Published { get; }

    /// <summary>Источник не публикует ничего: любой пробел безвозвратен.</summary>
    public static UpstreamCatalog Empty { get; } =
        new(new Dictionary<FactSet, TimeRange>());

    public static UpstreamCatalog Of(IReadOnlyDictionary<FactSet, TimeRange> published)
    {
        ArgumentNullException.ThrowIfNull(published);

        return new UpstreamCatalog(new Dictionary<FactSet, TimeRange>(published));
    }

    /// <summary>Части запрошенного интервала, которые источник публикует.</summary>
    public IReadOnlyList<TimeRange> PublishedParts(FactSet set, TimeRange range) =>
        Published.TryGetValue(set, out TimeRange available)
            ? TimeRanges.Intersect(range, [available])
            : [];
}
