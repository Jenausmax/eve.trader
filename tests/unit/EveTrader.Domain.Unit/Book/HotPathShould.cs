using System.Globalization;
using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>
/// Замер проходки. Горячий путь — единственное место в проекте, где код пишется
/// «неудобно, зато без аллокаций», и утверждение это надо проверять, а не повторять.
/// </summary>
public sealed class HotPathShould
{
    /// <summary>Синтетический стакан: ордера идут подряд, как их выдаёт источник.</summary>
    private static OrderSnapshot[] Book(int orders, int pairs)
    {
        var book = new OrderSnapshot[orders];

        for (var index = 0; index < orders; index++)
        {
            book[index] = new OrderSnapshot(
                orderId: index + 1,
                typeId: 34 + (index % pairs),
                locationId: 60003760,
                isBuy: index % 2 == 0,
                price: IskPrice.FromCents(10_000 + (index % 500)),
                volumeRemain: 100,
                volumeTotal: 100,
                durationDays: 90,
                issuedUnix: Observations.At(0).ToUnixTimeSeconds());
        }

        return book;
    }

    /// <summary>
    /// Сколько аллоцирует повторное наблюдение того же стакана. Признаки выключены
    /// намеренно: их охват — отдельная настройка, а измеряется здесь именно дифф.
    /// </summary>
    private static (long Bytes, TimeSpan Elapsed) SteadyState(int orders)
    {
        OrderSnapshot[] book = Book(orders, pairs: 64);
        var observer = new RegionObserver(Observations.TheForge, DiffOptions.Default, FeatureOptions.None);

        _ = observer.Observe(book, Observations.Meta(0));
        _ = observer.Observe(book, Observations.Meta(5));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        _ = observer.Observe(book, Observations.Meta(10));

        clock.Stop();

        return (GC.GetAllocatedBytesForCurrentThread() - before, clock.Elapsed);
    }

    [Fact]
    public void NotGrowAllocationsWithTheSizeOfTheBook()
    {
        (var small, TimeSpan smallTime) = SteadyState(250_000);
        (var large, TimeSpan largeTime) = SteadyState(1_000_000);


        // Стакан вчетверо больше. Линейный рост дал бы вчетверо больше и аллокаций;
        // переиспользуемые буферы и индекс означают, что на ордер не аллоцируется ничего.
        // Замер печатается в сообщении всегда: цифры нужны в design.md, а не только
        // при падении.
        large.ShouldBeLessThan(
            small * 2,
            string.Create(CultureInfo.InvariantCulture,
                $"250k: {small} байт за {smallTime.TotalMilliseconds:F0} мс; 1M: {large} байт за {largeTime.TotalMilliseconds:F0} мс"));
    }

    [Fact]
    public void KeepSteadyStateAllocationsFarBelowTheBookItself()
    {
        (var bytes, TimeSpan elapsed) = SteadyState(1_000_000);

        // Сам стакан — порядка шестидесяти мегабайт. Повторная проходка не должна
        // аллоцировать ничего близкого: иначе каждые пять минут переписывался бы
        // миллион структур.
        bytes.ShouldBeLessThan(
            1_000_000,
            string.Create(CultureInfo.InvariantCulture, $"проходка миллиона ордеров: {bytes} байт за {elapsed.TotalMilliseconds:F0} мс"));
    }

    [Fact]
    public void KeepTheOrderStructSmallEnoughToLiveInAnArray()
    {
        var size = System.Runtime.CompilerServices.Unsafe.SizeOf<OrderSnapshot>();

        // Миллион ордеров при таком размере — десятки мегабайт на поколение, и это
        // бюджет, заложенный в design.
        size.ShouldBeLessThanOrEqualTo(72, string.Create(CultureInfo.InvariantCulture, $"размер структуры: {size} байт"));
    }

    [Fact]
    public void FindEveryOrderThroughTheOpenAddressedIndex()
    {
        var index = new OrderIndex();
        index.Reset(100_000);

        for (var slot = 0; slot < 100_000; slot++)
        {
            index.Add(slot + 1, slot);
        }

        index.Count.ShouldBe(100_000);

        for (var slot = 0; slot < 100_000; slot++)
        {
            index.TryGet(slot + 1, out var found).ShouldBeTrue();
            found.ShouldBe(slot);
        }

        index.TryGet(999_999, out _).ShouldBeFalse();
    }

    [Fact]
    public void ReuseIndexStorageAcrossObservations()
    {
        var index = new OrderIndex();
        index.Reset(50_000);

        var capacity = index.Capacity;

        index.Reset(50_000);

        index.Capacity.ShouldBe(capacity, "таблица не пересоздаётся под тот же размер");
        index.Count.ShouldBe(0);
    }
}
