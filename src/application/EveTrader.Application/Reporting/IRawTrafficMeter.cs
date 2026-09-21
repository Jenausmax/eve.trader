using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>
/// Мера принятого трафика по окну сырых страниц.
///
/// Отдельный порт, а не метод на <see cref="Live.IRawPageArchive" />, и это не
/// формальность. У архива сырых страниц чтения нет по построению: окно не источник
/// истины, и способ достать из него содержимое не должен существовать даже как соблазн.
/// Здесь наружу выходит только размер — построить по нему стакан нельзя, а ответить
/// «сколько байт мы приняли» больше нечем.
/// </summary>
public interface IRawTrafficMeter
{
    Task<RawPageUsage> MeasureAsync(TimeRange within, CancellationToken cancellationToken);
}
