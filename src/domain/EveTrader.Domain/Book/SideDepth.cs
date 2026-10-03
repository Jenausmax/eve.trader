namespace EveTrader.Domain.Book;

/// <summary>Глубина одной стороны стакана по порогам: объём и число ордеров.</summary>
/// <param name="Volumes">Доступный объём внутри каждого порога.</param>
/// <param name="Orders">Число ордеров внутри каждого порога.</param>
internal sealed record SideDepth(IReadOnlyList<long> Volumes, IReadOnlyList<int> Orders)
{
    /// <summary>Стороны нет — нет ни объёма, ни счётчиков.</summary>
    public static SideDepth Absent { get; } = new([], []);
}
