namespace EveTrader.Domain.Book;

/// <summary>
/// Объявленный охват признаков стакана: для каких пар «тип и локация» признаки
/// материализуются, и на каком основании.
///
/// Основание — часть объявления, а не комментарий к нему. Полный охват пар измерен в
/// 770 ГиБ против ~62 ГиБ сырья, из которого он выводится, и признак, который никто не
/// читает, платить за себя не должен. Поэтому охват без названного основания не
/// объявляется вовсе: основанием служит перечень пар, нужных правилам сигналов, либо
/// явная перестройка того, что уже записано.
/// </summary>
public sealed record FeatureCoverage
{
    private readonly Func<int, long, bool> includes;

    private FeatureCoverage(string basis, Func<int, long, bool> includes)
    {
        Basis = basis;
        this.includes = includes;
    }

    /// <summary>Почему охват именно такой.</summary>
    public string Basis { get; }

    /// <summary>
    /// Все пары. Не умолчание, а объявление: так перестраивается то, что уже записано
    /// полным охватом, — приёмке и реплею сверять признаки не с чем, если охват у
    /// перестройки уже записанного.
    /// </summary>
    public static FeatureCoverage AllPairs { get; } =
        new("все пары: перестройка воспроизводит записанное полным охватом", static (_, _) => true);

    /// <summary>Признаки не материализуются вовсе.</summary>
    public static FeatureCoverage Nothing { get; } =
        new("признаки не нужны", static (_, _) => false);

    /// <summary>Пары, у которых локация входит в перечень, — так охват выводится из станций правила.</summary>
    public static FeatureCoverage Locations(string basis, IReadOnlyCollection<long> locations)
    {
        HashSet<long> wanted = [.. locations];

        return new FeatureCoverage(Named(basis), (_, locationId) => wanted.Contains(locationId));
    }

    /// <summary>Пары по произвольному отбору — с обязательным основанием.</summary>
    public static FeatureCoverage Pairs(string basis, Func<int, long, bool> includes) =>
        new(Named(basis), includes);

    public bool Includes(int typeId, long locationId) => includes(typeId, locationId);

    public static string Named(string basis) =>
        string.IsNullOrWhiteSpace(basis)
            ? throw new ArgumentException("Охват признаков объявляется с основанием", nameof(basis))
            : basis;

    public override string ToString() => Basis;
}
