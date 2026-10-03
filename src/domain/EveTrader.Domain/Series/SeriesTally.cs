namespace EveTrader.Domain.Series;

/// <summary>
/// Накопитель целой суммы и числа слагаемых — для средних, которые обязаны выходить
/// побитово одинаковыми.
///
/// Сумма копится целым, а делится один раз в конце: сложение вещественных зависит от
/// порядка слагаемых, и среднее, накопленное в <see cref="double" /> в другом порядке
/// обхода, расходилось бы в последнем знаке. Бэктест на таких данных невоспроизводим.
/// </summary>
/// <param name="Sum">Сумма слагаемых.</param>
/// <param name="Count">Число слагаемых.</param>
public readonly record struct SeriesTally(long Sum, int Count)
{
    public SeriesTally Add(long value) => new(Sum + value, Count + 1);

    public double Mean => Count == 0 ? 0d : Sum / (double)Count;

    /// <summary>Доля: сумма единиц к числу случаев.</summary>
    public double Share => Mean;
}
