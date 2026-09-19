using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>
/// Сценарии спеки <c>market-observation/order-events</c> §«Восстановление наблюдённого
/// стакана» и <c>book-features</c> §«Сверка признаков с восстановленным стаканом».
/// </summary>
public sealed class BookReconstructionShould
{
    /// <summary>
    /// Случайная, но воспроизводимая последовательность наблюдений: сид задан снаружи,
    /// иначе упавший тест нечем повторить.
    /// </summary>
    // CA5394: генератор здесь нужен воспроизводимый, а не криптостойкий. Сид задан
    // снаружи именно затем, чтобы упавший прогон можно было повторить точь-в-точь;
    // криптостойкий генератор это свойство как раз и уничтожил бы.
#pragma warning disable CA5394
    private static List<OrderSnapshot[]> Sequence(int seed, int steps, int orders)
    {
        var random = new Random(seed);
        var live = new Dictionary<long, OrderSnapshot>();
        var next = 1L;
        var book = new List<OrderSnapshot[]>(steps);

        for (var step = 0; step < steps; step++)
        {
            // Появления.
            while (live.Count < orders)
            {
                var id = next++;
                live[id] = Observations.Order(
                    id,
                    price: 50 + random.Next(100),
                    remain: 10 + random.Next(90),
                    total: 100,
                    issuedMinute: step * 5,
                    isBuy: random.Next(2) == 0,
                    typeId: 34 + random.Next(3));
            }

            foreach (var id in live.Keys.ToList())
            {
                switch (random.Next(6))
                {
                    case 0: // перестановка цены
                        live[id] = live[id] with { };
                        live[id] = Observations.Order(
                            id, price: 50 + random.Next(100), remain: live[id].VolumeRemain,
                            total: live[id].VolumeTotal, issuedMinute: step * 5,
                            isBuy: live[id].IsBuy, typeId: live[id].TypeId);

                        break;

                    case 1: // исполнение
                        var left = Math.Max(1, live[id].VolumeRemain - random.Next(10));
                        live[id] = Observations.Order(
                            id, price: live[id].Price.ToIsk(), remain: left, total: live[id].VolumeTotal,
                            issuedMinute: (int)((live[id].IssuedUnix - Observations.At(0).ToUnixTimeSeconds()) / 60),
                            isBuy: live[id].IsBuy, typeId: live[id].TypeId);

                        break;

                    case 2: // исчезновение
                        _ = live.Remove(id);

                        break;

                    default:
                        break;
                }
            }

            book.Add([.. live.Values.OrderBy(static order => order.OrderId)]);
        }

        return book;
    }
#pragma warning restore CA5394

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1337)]
    public void RebuildTheSameBookTheFeaturesWereComputedFrom(int seed)
    {
        List<OrderSnapshot[]> steps = Sequence(seed, steps: 12, orders: 25);

        // Хвост из неизменных наблюдений: исчезновение — состояние процесса до
        // подтверждения окном и факт после, поэтому сверять состав имеет смысл там, где
        // все ожидающие подтверждения уже разрешились.
        for (var repeat = 0; repeat < DiffOptions.Default.DisappearanceWindow; repeat++)
        {
            steps.Add(steps[^1]);
        }

        var observer = new RegionObserver(Observations.TheForge, DiffOptions.Default, FeatureOptions.Default);
        var events = new List<OrderEvent>();
        IReadOnlyList<BookFeatures> lastFeatures = [];

        for (var step = 0; step < steps.Count; step++)
        {
            ObservationOutcome outcome = observer.Observe(steps[step], Observations.Meta(step * 5));
            events.AddRange(outcome.Events);
            lastFeatures = outcome.Features;
        }

        // Чекпойнт — состав на первом наблюдении; дальше только события.
        IReadOnlyList<OrderSnapshot> rebuilt = BookReconstruction.Rebuild(
            steps[0], events.Skip(steps[0].Length));

        rebuilt.Select(static order => order.OrderId).ShouldBe(
            steps[^1].Select(static order => order.OrderId),
            "восстановленный состав совпадает с наблюдённым");

        // Признаки, пересчитанные по восстановленному стакану, совпадают с теми, что
        // посчитаны в проходке. Если расходятся — либо события теряют состояние, либо
        // признаки считаются не из того, из чего объявлено.
        var builder = new BookFeatureBuilder(FeatureOptions.Default);

        foreach (OrderSnapshot order in rebuilt)
        {
            builder.Add(in order);
        }

        IReadOnlyList<BookFeatures> recomputed = builder.Build(
            Observations.Meta((steps.Count - 1) * 5), Observations.Meta((steps.Count - 1) * 5).Observation);

        recomputed.Count.ShouldBe(lastFeatures.Count);

        foreach ((BookFeatures fromPass, BookFeatures fromRebuild) in lastFeatures.Zip(recomputed))
        {
            fromRebuild.BestBid.ShouldBe(fromPass.BestBid);
            fromRebuild.BestAsk.ShouldBe(fromPass.BestAsk);
            fromRebuild.BuyOrders.ShouldBe(fromPass.BuyOrders);
            fromRebuild.SellOrders.ShouldBe(fromPass.SellOrders);
            fromRebuild.BuyDepth.ShouldBe(fromPass.BuyDepth);
            fromRebuild.SellDepth.ShouldBe(fromPass.SellDepth);
        }
    }

    [Fact]
    public void ReflectConfirmedKnowledgeNotRawAbsence()
    {
        var observer = new RegionObserver(Observations.TheForge, DiffOptions.Default, FeatureOptions.Default);

        OrderSnapshot[] start = [Observations.Order(1, price: 100), Observations.Order(2, price: 90)];
        var events = new List<OrderEvent>(observer.Observe(start, Observations.Meta(0)).Events);

        // Ордер пропал из наблюдения — но одного отсутствия мало, чтобы поверить.
        events.AddRange(observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5)).Events);

        IReadOnlyList<OrderSnapshot> pending = BookReconstruction.Rebuild(start, events.Skip(start.Length));

        pending.Count.ShouldBe(2, "исчезновение до подтверждения окном фактом не является");

        // Второе отсутствие подтверждает — и только теперь состав меняется.
        events.AddRange(observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(10)).Events);

        BookReconstruction.Rebuild(start, events.Skip(start.Length))
            .ShouldHaveSingleItem().OrderId.ShouldBe(1);
    }

    [Fact]
    public void BeDeterministicAcrossRepeatedRebuilds()
    {
        List<OrderSnapshot[]> steps = Sequence(seed: 99, steps: 8, orders: 20);

        var observer = new RegionObserver(Observations.TheForge, DiffOptions.Default, FeatureOptions.Default);
        var events = new List<OrderEvent>();

        for (var step = 0; step < steps.Count; step++)
        {
            events.AddRange(observer.Observe(steps[step], Observations.Meta(step * 5)).Events);
        }

        IReadOnlyList<OrderSnapshot> first = BookReconstruction.Rebuild(steps[0], events.Skip(steps[0].Length));
        IReadOnlyList<OrderSnapshot> second = BookReconstruction.Rebuild(steps[0], events.Skip(steps[0].Length));

        second.ShouldBe(first, "два восстановления одного момента идентичны, включая порядок");
    }

    [Fact]
    public void DropAnOrderWhoseDisappearanceWasConfirmed()
    {
        var observer = new RegionObserver(Observations.TheForge, DiffOptions.Default, FeatureOptions.Default);

        OrderSnapshot[] start = [Observations.Order(1, price: 100), Observations.Order(2, price: 90)];
        var events = new List<OrderEvent>(observer.Observe(start, Observations.Meta(0)).Events);

        events.AddRange(observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5)).Events);
        events.AddRange(observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(10)).Events);

        IReadOnlyList<OrderSnapshot> rebuilt = BookReconstruction.Rebuild(start, events.Skip(start.Length));

        rebuilt.ShouldHaveSingleItem().OrderId.ShouldBe(1);
    }
}
