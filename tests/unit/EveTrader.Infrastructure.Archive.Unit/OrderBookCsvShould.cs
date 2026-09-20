using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using Shouldly;

namespace EveTrader.Infrastructure.Archive.Unit;

/// <summary>
/// Разбор снимка стакана. Проверки взяты с настоящего файла источника
/// (<c>market-orders-2026-09-17_00-15-07.v3.csv.bz2</c>), а не выдуманы: там и пустые
/// колонки у структур, и шестиминутный разброс времени сбора.
/// </summary>
public sealed class OrderBookCsvShould
{
    private const string HeaderV3 =
        "duration,is_buy_order,issued,location_id,min_volume,order_id,price,range,system_id,type_id," +
        "volume_remain,volume_total,http_last_modified,station_id,region_id,constellation_id";

    private static DateTimeOffset Fallback => new(2026, 9, 17, 0, 20, 4, TimeSpan.Zero);

    private static Task<IReadOnlyList<RegionBook>> ReadAsync(string csv, params RegionId[] wanted) =>
        OrderBookCsv.ReadAsync(new StringReader(csv), Fallback, wanted, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReadTheColumnsByNameIntoAnOrderSnapshot()
    {
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-06-28T15:13:51Z,60014437,1,7367318263,600.0,region,30000001,20,11698,24500," +
            "2026-09-17T00:15:09Z,60014437,10000001,20000001\n").ConfigureAwait(true);

        RegionBook book = books.ShouldHaveSingleItem();

        book.Region.ShouldBe(RegionId.From(10000001));

        OrderSnapshot order = book.Orders.ShouldHaveSingleItem();

        order.OrderId.ShouldBe(7367318263);
        order.TypeId.ShouldBe(20);
        order.LocationId.ShouldBe(60014437);
        order.IsBuy.ShouldBeFalse();
        order.Price.ShouldBe(IskPrice.FromIsk(600m));
        order.VolumeRemain.ShouldBe(11698);
        order.VolumeTotal.ShouldBe(24500);
        order.DurationDays.ShouldBe((short)90);
        order.Issued.ShouldBe(new DateTimeOffset(2026, 6, 28, 15, 13, 51, TimeSpan.Zero));
    }

    [Fact]
    public async Task ReadOrdersInPlayerStructuresWhereTheSourceLeavesColumnsEmpty()
    {
        // У ордеров в игровых структурах источник не заполняет system_id, station_id и
        // constellation_id. Эти колонки не читаются вовсе — иначе разбор падал бы ровно
        // на рынках структур, ради которых окно и берётся.
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,true,2026-09-01T10:00:00Z,1029209158478,1,7400000001,1234.56,region,,34,10,10," +
            "2026-09-17T00:15:09Z,,10000002,\n").ConfigureAwait(true);

        OrderSnapshot order = books.ShouldHaveSingleItem().Orders.ShouldHaveSingleItem();

        order.LocationId.ShouldBe(1029209158478);
        order.IsBuy.ShouldBeTrue();
        order.Price.ShouldBe(IskPrice.FromIsk(1234.56m));
    }

    [Fact]
    public async Task SplitOneFileIntoABookPerRegionOrderedById()
    {
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,1,10.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000043,20000020\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,2,11.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000002,20000020\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,3,12.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000002,20000020\n")
            .ConfigureAwait(true);

        books.Select(static book => book.Region.Value).ShouldBe([10000002, 10000043]);
        books[0].Orders.Count.ShouldBe(2);
        books[1].Orders.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DropRegionsThatWereNotAsked()
    {
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,1,10.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000043,20000020\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,2,11.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000002,20000020\n",
            RegionId.From(10000002)).ConfigureAwait(true);

        books.ShouldHaveSingleItem().Region.ShouldBe(RegionId.From(10000002));
    }

    [Fact]
    public async Task TakeTheCollectionIntervalFromTheExtremeRowTimesOfTheRegion()
    {
        // Полный обход рынка занимает у источника около шести минут, и границы интервала
        // сбора — это крайние отметки строк, а не время изменения файла.
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,1,10.0,region,30000142,34,1,1,2026-09-17T00:15:18Z,60003760,10000002,20000020\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,2,11.0,region,30000142,34,1,1,2026-09-17T00:10:09Z,60003760,10000002,20000020\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,3,12.0,region,30000142,34,1,1,2026-09-17T00:16:03Z,60003760,10000002,20000020\n")
            .ConfigureAwait(true);

        TimeRange collected = books.ShouldHaveSingleItem().Collected;

        collected.From.ShouldBe(new DateTimeOffset(2026, 9, 17, 0, 10, 9, TimeSpan.Zero));
        collected.To.ShouldBe(new DateTimeOffset(2026, 9, 17, 0, 16, 3, TimeSpan.Zero));
    }

    [Fact]
    public async Task StretchTheIntervalByASecondWhenEveryRowCarriesOneInstant()
    {
        // Полуинтервал требует строгого порядка границ, а маленький регион источник
        // обходит одним запросом и помечает все строки одной секундой.
        IReadOnlyList<RegionBook> books = await ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,1,10.0,region,30000142,34,1,1,2026-09-17T00:15:18Z,60003760,10000002,20000020\n")
            .ConfigureAwait(true);

        TimeRange collected = books.ShouldHaveSingleItem().Collected;

        collected.Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FallBackToTheFileTimeWhenTheGenerationCarriesNoCollectionTime()
    {
        IReadOnlyList<RegionBook> books = await ReadAsync(
            "duration,is_buy_order,issued,location_id,min_volume,order_id,price,range,system_id,type_id," +
            "volume_remain,volume_total,region_id\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,1,10.0,region,30000142,34,1,1,10000002\n")
            .ConfigureAwait(true);

        books.ShouldHaveSingleItem().Collected.From.ShouldBe(Fallback);
    }

    [Fact]
    public async Task RefuseAHeaderWithoutAMandatoryColumn()
    {
        ArchiveFormatException failure = await Should
            .ThrowAsync<ArchiveFormatException>(static () => ReadAsync("duration,is_buy_order,region_id\n90,false,10000002\n"))
            .ConfigureAwait(true);

        failure.Message.ShouldContain("'order_id'");
    }

    [Fact]
    public async Task RefuseARowThatDoesNotParseRatherThanGuessIt()
    {
        _ = await Should.ThrowAsync<ArchiveFormatException>(static () => ReadAsync(
            HeaderV3 + "\n" +
            "90,false,2026-09-01T10:00:00Z,60003760,1,не-число,10.0,region,30000142,34,1,1,2026-09-17T00:15:09Z,60003760,10000002,20000020\n"))
            .ConfigureAwait(true);
    }

    [Theory]
    [InlineData("market-orders-2026-09-17_00-15-07.v3.csv.bz2", "2026-09-17T00:15:07")]
    [InlineData("market-orders-2021-06-19_16-50-12.v3.csv.bz2", "2021-06-19T16:50:12")]
    [InlineData("market-orders-2023-06-01_00-16-52.v3.csv.bz2", "2023-06-01T00:16:52")]
    public void ReadTheSnapshotInstantFromTheFileName(string file, string expected)
    {
        // Секунды в имени плавают — источник пишет их такими, какими начался обход, —
        // поэтому момент берётся из имени, а не с получасовой сетки.
        EveRefOrderBookArchive.InstantOf(file)
            .ShouldBe(new DateTimeOffset(DateTime.Parse(expected, null), TimeSpan.Zero));
    }

    [Fact]
    public void IgnoreAFileThatIsNotASnapshot() =>
        EveRefOrderBookArchive.InstantOf("index.json").ShouldBeNull();

    [Fact]
    public void ReadTheListingNamesFromTheDocumentedJsonIndex()
    {
        const string Json = /*lang=json,strict*/ """
            {"directories":[{"name":"2026-09-17"},{"name":"2026-09-18"}],"path":"market-orders/history/2026"}
            """;

        EveRefOrderBookArchive.Names(Json, "directories").ShouldBe(["2026-09-17", "2026-09-18"]);
        EveRefOrderBookArchive.Names(Json, "files").ShouldBeEmpty();
    }
}
