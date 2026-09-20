using System.Globalization;
using System.Text;
using EveTrader.Domain.Book;
using EveTrader.Infrastructure.Esi.Orders;
using Shouldly;

namespace EveTrader.Infrastructure.Esi.Unit;

/// <summary>
/// Потоковый разбор страницы ордеров. Проверяется и корректность, и то, ради чего он
/// вообще писался потоковым: на ордер не должно аллоцироваться ничего.
/// </summary>
public sealed class EsiOrderReaderShould
{
    private static byte[] Page(int orders)
    {
        var json = new StringBuilder("[");

        for (var index = 0; index < orders; index++)
        {
            if (index > 0)
            {
                _ = json.Append(',');
            }

            _ = json.Append(CultureInfo.InvariantCulture,
                $$"""
                  {"duration":90,"is_buy_order":{{(index % 2 == 0 ? "false" : "true")}},
                  "issued":"2026-01-01T11:00:00Z","location_id":60003760,"min_volume":1,
                  "order_id":{{index + 1}},"price":{{100 + (index % 50)}}.25,"range":"region",
                  "system_id":30000142,"type_id":34,"volume_remain":{{index + 1}},"volume_total":100}
                  """);
        }

        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }

    [Fact]
    public void ReadEveryFieldRegardlessOfOrderInTheResponse()
    {
        var destination = new OrderSnapshot[4];

        // Поля идут не в том порядке, в каком объявлены в структуре, и между ними
        // попадаются незнакомые — разбор идёт по именам.
        var read = EsiOrderReader.Read(Page(3), destination, 0);

        read.ShouldBe(3);

        OrderSnapshot first = destination[0];
        first.OrderId.ShouldBe(1);
        first.TypeId.ShouldBe(34);
        first.LocationId.ShouldBe(60003760);
        first.IsBuy.ShouldBeFalse();
        first.Price.ToIsk().ShouldBe(100.25m);
        first.VolumeRemain.ShouldBe(1);
        first.VolumeTotal.ShouldBe(100);
        first.DurationDays.ShouldBe((short)90);
        first.Issued.ShouldBe(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero));

        destination[1].IsBuy.ShouldBeTrue();
    }

    [Fact]
    public void AppendIntoTheSameBufferAcrossPages()
    {
        var destination = new OrderSnapshot[8];

        var first = EsiOrderReader.Read(Page(3), destination, 0);
        var second = EsiOrderReader.Read(Page(2), destination, first);

        first.ShouldBe(3);
        second.ShouldBe(2);

        // Страницы легли подряд: буфер один на наблюдение, а не на страницу.
        destination[..(first + second)].Select(static order => order.OrderId).ShouldBe([1, 2, 3, 1, 2]);
    }

    [Fact]
    public void NotAllocatePerOrder()
    {
        var destination = new OrderSnapshot[20_000];
        var small = Page(1_000);
        var large = Page(10_000);

        // Прогрев: первый разбор тянет за собой инициализацию ридера.
        _ = EsiOrderReader.Read(small, destination, 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = EsiOrderReader.Read(small, destination, 0);
        var afterSmall = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        _ = EsiOrderReader.Read(large, destination, 0);
        var afterLarge = GC.GetAllocatedBytesForCurrentThread() - before;

        // Ордеров вдесятеро больше. Аллокации на ордер дали бы вдесятеро больше байт;
        // потоковый разбор в готовый массив не аллоцирует ничего.
        afterLarge.ShouldBeLessThan(
            Math.Max(4096, afterSmall * 2),
            string.Create(CultureInfo.InvariantCulture, $"1k: {afterSmall} байт; 10k: {afterLarge} байт"));
    }

    [Fact]
    public void RefuseToOverflowTheBuffer()
    {
        var destination = new OrderSnapshot[2];

        // Источник прислал больше, чем объявил. Молча потерять хвост нельзя — это
        // выглядело бы как исчезновение ордеров.
        _ = Should.Throw<InvalidOperationException>(() => EsiOrderReader.Read(Page(5), destination, 0));
    }

    [Fact]
    public void ReturnNothingForAnEmptyPage() =>
        EsiOrderReader.Read(Encoding.UTF8.GetBytes("[]"), new OrderSnapshot[4], 0).ShouldBe(0);
}
