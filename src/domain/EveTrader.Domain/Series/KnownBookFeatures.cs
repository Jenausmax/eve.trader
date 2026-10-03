using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Признаки стакана вместе с ключом факта и моментом, когда система о них узнала.
///
/// Время наблюдения признака и время знания о нём расходятся при досинхронизации задним
/// числом: снимок за 02:50, импортированный в 05:00, наблюдён раньше точки на 03:00, но
/// известен позже неё. Точка ряда отсекает входы по второму, иначе она содержала бы то,
/// чего система на её конце окна не знала.
///
/// Ключ нужен для уточнений: у одного снимка может быть несколько версий, и на каждом
/// конце окна берётся последняя из известных к нему (<see cref="Bitemporal.AsOf{T}" />),
/// а не последняя вообще.
/// </summary>
/// <param name="Features">Признаки стакана.</param>
/// <param name="FactKey">Ключ факта: уточнённая версия несёт тот же ключ, что и прежняя.</param>
/// <param name="KnownAt">Момент, когда признаки стали известны системе.</param>
public sealed record KnownBookFeatures(BookFeatures Features, string FactKey, DateTimeOffset KnownAt) : IBitemporalFact
{
    public ObservationId Observation => Features.Observation;
}
