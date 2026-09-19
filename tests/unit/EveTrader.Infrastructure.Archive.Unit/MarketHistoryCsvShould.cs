using EveTrader.Domain.History;
using EveTrader.Infrastructure.Archive.EveRef;
using Shouldly;

namespace EveTrader.Infrastructure.Archive.Unit;

/// <summary>
/// Поколения формата источника. Заголовки взяты с живого архива EVE Ref: формат менялся
/// за двадцать три года четырежды, и разбор обязан пережить это, потому что схема факта
/// одна.
/// </summary>
public sealed class MarketHistoryCsvShould
{
    private static readonly DateTimeOffset Fallback = new(2026, 8, 9, 10, 59, 4, TimeSpan.Zero);

    private static async Task<List<MarketHistoryRow>> ReadAsync(string csv, CancellationToken token)
    {
        using var reader = new StringReader(csv);

        return await MarketHistoryCsv.ReadAsync(reader, Fallback, token).ToListAsync(token).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadTheEarlyGenerationThatHasNoReceiptTime()
    {
        // 2003–2017: колонки времени получения нет вовсе.
        var csv = """
            date,region_id,type_id,average,highest,lowest,volume,order_count
            2003-10-01,10000002,34,10.5,12,9,1000,7

            """;

        MarketHistoryRow row = (await ReadAsync(csv, TestContext.Current.CancellationToken).ConfigureAwait(true)).Single();

        row.MarketDate.ShouldBe(new DateOnly(2003, 10, 1));
        row.Region.Value.ShouldBe(10000002);
        row.TypeId.ShouldBe(34);
        row.Highest.ShouldBe(12m);
        row.Lowest.ShouldBe(9m);
        row.Volume.ShouldBe(1000);
        row.OrderCount.ShouldBe(7);

        // Время получения взято у файла: строка его не несёт.
        row.KnownAt.ShouldBe(Fallback);
    }

    [Fact]
    public async Task NotSwapHighestAndLowestWhenTheSourceSwapsTheColumns()
    {
        // 2019: highest и lowest стоят в обратном порядке. Разбор идёт по именам,
        // поэтому перестановка ничего не меняет — позиционный разбор соврал бы молча.
        var csv = """
            date,region_id,type_id,average,lowest,highest,volume,order_count
            2019-06-15,10000002,34,10.5,9,12,1000,7

            """;

        MarketHistoryRow row = (await ReadAsync(csv, TestContext.Current.CancellationToken).ConfigureAwait(true)).Single();

        row.Highest.ShouldBe(12m);
        row.Lowest.ShouldBe(9m);
    }

    [Fact]
    public async Task ReadTheGenerationWhereReceiptTimeSitsLast()
    {
        // 2022: время получения появилось, но в конце строки.
        var csv = """
            average,date,highest,lowest,order_count,volume,region_id,type_id,http_last_modified
            10.5,2022-03-15,12,9,7,1000,10000002,34,2022-03-16T11:06:34Z

            """;

        MarketHistoryRow row = (await ReadAsync(csv, TestContext.Current.CancellationToken).ConfigureAwait(true)).Single();

        row.KnownAt.ShouldBe(new DateTimeOffset(2022, 3, 16, 11, 6, 34, TimeSpan.Zero));
    }

    [Fact]
    public async Task ReadTheCurrentGeneration()
    {
        var csv = """
            average,date,highest,lowest,order_count,volume,http_last_modified,region_id,type_id
            3,2026-01-15,3,3,13,6739187,2026-01-16T11:06:34Z,10000001,34
            700,2026-01-15,700,700,5,32500,2026-05-28T11:03:24Z,10000001,20

            """;

        List<MarketHistoryRow> rows = await ReadAsync(csv, TestContext.Current.CancellationToken).ConfigureAwait(true);

        rows.Count.ShouldBe(2);

        // Досинхронизация видна прямо в данных: строка за январь узнана в мае.
        rows.Select(static row => row.KnownAt.Month).Order().ShouldBe([1, 5]);
    }

    [Fact]
    public async Task FallBackToFileTimeWhenTheReceiptTimeIsBlank()
    {
        // Пятое поколение формата: колонка есть, значение пустое. В файлах середины
        // 2020 года таких строк сотни на сутки, и одна из них роняла весь прогон.
        var csv = """
            average,date,highest,lowest,order_count,volume,region_id,type_id,http_last_modified
            10.5,2020-06-18,12,9,7,1000,10000002,34,
            10.5,2020-06-18,12,9,7,2000,10000002,35,2020-06-19T11:06:34Z

            """;

        List<MarketHistoryRow> rows = await ReadAsync(csv, TestContext.Current.CancellationToken).ConfigureAwait(true);

        rows.Count.ShouldBe(2);

        // Пустое значение означает то же, что отсутствующая колонка: источник не сказал.
        rows[0].KnownAt.ShouldBe(Fallback);
        rows[1].KnownAt.ShouldBe(new DateTimeOffset(2020, 6, 19, 11, 6, 34, TimeSpan.Zero));
    }

    [Fact]
    public async Task ReportAnUnparseableRowAsAFormatDefectNotAsACrash()
    {
        var csv = """
            average,date,highest,lowest,order_count,volume,region_id,type_id,http_last_modified
            10.5,2026-01-15,12,9,7,не-число,10000002,34,2026-01-16T11:06:34Z

            """;

        MarketHistoryFormatException rejected = await Should
            .ThrowAsync<MarketHistoryFormatException>(() => ReadAsync(csv, TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        // Тип ошибки — то, по чему импорт отличает дефект данных от дефекта кода:
        // первый пропускает сутки, второй обязан ронять прогон громко.
        rejected.Message.ShouldContain("не-число");
    }

    [Fact]
    public async Task RefuseAHeaderWithoutTheColumnsAFactNeeds()
    {
        var csv = """
            date,region_id,average

            """;

        MarketHistoryFormatException rejected = await Should
            .ThrowAsync<MarketHistoryFormatException>(() => ReadAsync(csv, TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        rejected.Message.ShouldContain("type_id");
    }

    [Fact]
    public async Task ReturnNothingForAnEmptyFile() =>
        (await ReadAsync(string.Empty, TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBeEmpty();
}
