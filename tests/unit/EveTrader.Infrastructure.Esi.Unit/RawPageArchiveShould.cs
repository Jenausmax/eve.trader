using System.Text;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Esi.Orders;
using Shouldly;

namespace EveTrader.Infrastructure.Esi.Unit;

/// <summary>
/// Скользящее окно сырых страниц. Оно существует только для разбора дефектов парсера,
/// и главное здесь — не то, что страницы хранятся, а то, что они уходят.
/// </summary>
public sealed class RawPageArchiveShould : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly RegionId Forge = RegionId.From(10000002);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "eve-trader-raw-unit", Guid.NewGuid().ToString("N"));

    private RawPageArchive Archive => new(root);

    private static ReadOnlyMemory<byte> Body => Encoding.UTF8.GetBytes("[]");

    [Fact]
    public async Task DropPagesOlderThanTheWindow()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RawPageArchive archive = Archive;

        await archive.StoreAsync(Forge, 1, Now.AddHours(-72), Body, token).ConfigureAwait(true);
        await archive.StoreAsync(Forge, 2, Now.AddHours(-49), Body, token).ConfigureAwait(true);
        await archive.StoreAsync(Forge, 3, Now.AddHours(-2), Body, token).ConfigureAwait(true);

        var removed = await archive.SweepAsync(Now.AddHours(-48), token).ConfigureAwait(true);

        removed.ShouldBe(2);

        List<string> left = [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)];
        left.Count.ShouldBe(1);
        left[0].ShouldContain("0003");
    }

    [Fact]
    public async Task RemoveEmptiedDaysSoTheWindowStaysReadable()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RawPageArchive archive = Archive;

        await archive.StoreAsync(Forge, 1, Now.AddHours(-72), Body, token).ConfigureAwait(true);

        _ = await archive.SweepAsync(Now.AddHours(-48), token).ConfigureAwait(true);

        Directory.EnumerateDirectories(root).ShouldBeEmpty();
    }

    [Fact]
    public async Task KeepPagesOfDifferentRegionsApart()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RawPageArchive archive = Archive;

        await archive.StoreAsync(Forge, 1, Now, Body, token).ConfigureAwait(true);
        await archive.StoreAsync(RegionId.From(10000043), 1, Now, Body, token).ConfigureAwait(true);

        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Count().ShouldBe(2);
    }

    [Fact]
    public void NotOfferAnyWayToReadPagesBack()
    {
        // Окно не является источником истины, и это закреплено устройством порта, а не
        // дисциплиной: читать из него нечем. Соблазн «перечитать сырьё» возникает ровно
        // тогда, когда в свёртке нашли ошибку, и уступка ему сделала бы окно
        // обязательным навсегда.
        Type port = typeof(Application.Live.IRawPageArchive);

        port.GetMethods().Select(static method => method.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["StoreAsync", "SweepAsync"]);
    }

    [Fact]
    public async Task ReturnZeroWhenThereIsNothingToSweep() =>
        (await Archive.SweepAsync(Now, TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldBe(0);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
