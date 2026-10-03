using System.Globalization;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Ряды региона за интервал: приговор каждому окну и точки допущенных окон.
///
/// Здесь же задача 2.4 — детерминированность. Бэктест обязан быть воспроизводим побитово,
/// и проверяется это не на словах: один интервал считается дважды, во второй раз из
/// перемешанных входов, и выдачи сверяются до последнего бита значения.
/// </summary>
public sealed class SeriesComputationShould
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly RegionId Forge = RegionId.From(10000002);

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    private static readonly TimeRange Interval = TimeRange.Between(Start, Start.AddHours(6));

    private static readonly IReadOnlyList<SeriesDefinition> Definitions =
    [
        SeriesDefinition.Of(SeriesKind.ObservedTurnover, TimeSpan.FromHours(2), Step),
        SeriesDefinition.Of(SeriesKind.RelistPressure, TimeSpan.FromHours(2), Step),
        SeriesDefinition.Of(SeriesKind.FilledDisappearanceShare, TimeSpan.FromHours(2), Step),
        SeriesDefinition.Of(SeriesKind.BestPriceHold, TimeSpan.FromHours(2), Step),
        SeriesDefinition.Of(SeriesKind.CompetitorDepth, TimeSpan.FromHours(2), Step, bandBasisPoints: 100),
    ];

    [Fact]
    public void ComputeTheSameIntervalTwiceBitForBit()
    {
        SeriesInputs inputs = Inputs();
        SeriesInputs shuffled = inputs with
        {
            Events = [.. inputs.Events.Reverse()],
            Features = [.. inputs.Features.OrderBy(static known => known.Features.Observation.Value.GetHashCode(StringComparison.Ordinal))],
            Coverage = [.. inputs.Coverage.Reverse()],
        };

        SeriesComputed first = SeriesComputation.Compute(inputs, Definitions, Interval);
        SeriesComputed second = SeriesComputation.Compute(shuffled, [.. Definitions.Reverse()], Interval);

        first.Points.ShouldNotBeEmpty();

        // Сверка побитовая, включая порядок: значение сравнивается по битам, а не с
        // допуском, и ключи — в том порядке, в каком выданы.
        Shape(second).ShouldBe(Shape(first));
    }

    [Fact]
    public void RefuseAWindowReachingBeforeTheFirstObservation()
    {
        SeriesComputed computed = SeriesComputation.Compute(Inputs(), Definitions, Interval);

        // Окно, кончающееся в 01:00, начинается в 23:00 прошлых суток — тогда стакан
        // никто не снимал.
        SeriesWindowVerdict early = Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(1));

        early.Window.Admission.ShouldBe(SeriesAdmission.Refused);
        early.Window.Coverage.ShouldBe(CoverageState.NotObserved);
        computed.Points.ShouldNotContain(static point => point.Window.To == Start.AddHours(1));
    }

    [Fact]
    public void AdmitAWindowCoveredByAnUnbrokenChainOfSnapshots()
    {
        SeriesComputed computed = SeriesComputation.Compute(Inputs(), Definitions, Interval);

        // Снимки каждые полчаса с 00:00: окно [01:00, 03:00) пройдено цепочкой целиком,
        // хотя интервал сбора у каждого снимка — двадцать секунд.
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3)).Window.Admission
            .ShouldBe(SeriesAdmission.Admitted);
    }

    [Fact]
    public void RefuseAWindowAcrossABreakInTheChain()
    {
        SeriesInputs inputs = Inputs();
        SeriesInputs broken = inputs with
        {
            Coverage = [.. inputs.Coverage.Where(static entry => entry.Collected.To != Start.AddHours(4))],
        };

        SeriesComputed computed = SeriesComputation.Compute(broken, Definitions, Interval);

        // Снимка в 04:00 нет: отрезок [03:30, 04:00) никто не видел, и окно, через него
        // проходящее, не порождает признака.
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(5)).Window.Admission
            .ShouldBe(SeriesAdmission.Refused);
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3.5)).Window.Admission
            .ShouldBe(SeriesAdmission.Admitted);
    }

    [Fact]
    public void AdmitAWindowWhoseNextSnapshotIsNotYetDue()
    {
        // Снимки в :15 и :45. На 03:00 последний снимок был в 02:45, следующий ждут в
        // 03:15: отрезок [02:45, 03:00) ещё не наблюдён, но и не пропущен.
        SeriesComputed computed = SeriesComputation.Compute(Inputs(offsetMinutes: 15), Definitions, Interval);

        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3)).Window.Admission
            .ShouldBe(SeriesAdmission.Admitted);
    }

    [Fact]
    public void ForgiveTheSourceItsJitter()
    {
        // Объявлен шаг 30 минут, а снимки приходят через 31: источник публикует их с
        // плавающей секундой. Это дрожь, а не разрыв — окно допускается.
        SeriesComputed computed = SeriesComputation.Compute(
            Inputs(spacing: TimeSpan.FromMinutes(31)), Definitions, Interval);

        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3)).Window.Admission
            .ShouldBe(SeriesAdmission.Admitted);
    }

    [Fact]
    public void RefuseAWindowWhoseNextSnapshotIsOverdue()
    {
        SeriesInputs inputs = Inputs();
        SeriesInputs stalled = inputs with
        {
            Coverage = [.. inputs.Coverage.Where(static entry => entry.Collected.To <= Start.AddHours(2))],
        };

        SeriesComputed computed = SeriesComputation.Compute(stalled, Definitions, Interval);

        // После 02:00 снимков нет. Снимок 02:30 был должен и не пришёл — к 03:00 это уже
        // пропуск, а не ожидание.
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3)).Window.Admission
            .ShouldBe(SeriesAdmission.Refused);
    }

    [Fact]
    public void NotLetDailyHistoryVouchForTheBook()
    {
        SeriesInputs inputs = Inputs();
        SeriesInputs withHistory = inputs with
        {
            Coverage =
            [
                .. inputs.Coverage,
                CoverageEntries.Success(
                    ObservationId.From("history-2026-09-14"),
                    Forge,
                    TimeRange.Between(Start.AddDays(-1), Start),
                    pages: 1,
                    orderCount: 1,
                    source: "everef-archive",
                    observationStep: TimeSpan.FromDays(1),
                    knownAt: Start),
            ],
        };

        SeriesComputed computed = SeriesComputation.Compute(withHistory, Definitions, Interval);

        // Запись дневной истории накрывает прошлые сутки региона, но о стакане внутри
        // суток не говорит: окно по ней не допускается.
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(1)).Window.Admission
            .ShouldBe(SeriesAdmission.Refused);
    }

    [Fact]
    public void LeaveOutCoverageLearnedAfterTheWindowEnd()
    {
        SeriesInputs inputs = Inputs();
        SeriesInputs late = inputs with
        {
            // Снимок 02:00 стал известен системе только в 05:00 — досинхронизация задним
            // числом.
            Coverage =
            [
                .. inputs.Coverage.Select(static entry => entry.Collected.To == Start.AddHours(2)
                    ? entry with { KnownAt = Start.AddHours(5) }
                    : entry),
            ],
        };

        SeriesComputed computed = SeriesComputation.Compute(late, Definitions, Interval);

        // На 03:00 система о снимке 02:00 не знала — окно в тот момент выглядело
        // дырявым, и признак за него тогда не порождался.
        Verdict(computed, SeriesKind.ObservedTurnover, Start.AddHours(3)).Window.Admission
            .ShouldBe(SeriesAdmission.Refused);
    }

    [Fact]
    public void LeaveOutFeaturesLearnedAfterTheWindowEnd()
    {
        SeriesInputs inputs = Inputs();

        // Снимок за 02:30 с толпой конкурентов. Узнай система о нём сразу, он вошёл бы в
        // точку на 03:00; узнала она в 05:00 — досинхронизация задним числом.
        var timely = Depth(SeriesComputation.Compute(Crowded(inputs, Start.AddMinutes(150)), Definitions, Interval), Start.AddHours(3));
        var late = Depth(SeriesComputation.Compute(Crowded(inputs, Start.AddHours(5)), Definitions, Interval), Start.AddHours(3));
        var without = Depth(SeriesComputation.Compute(inputs, Definitions, Interval), Start.AddHours(3));

        timely.ShouldNotBe(without);
        late.ShouldBe(without);
    }

    [Fact]
    public void TakeTheVersionOfASnapshotKnownAtTheWindowEnd()
    {
        SeriesInputs inputs = Inputs();
        var without = Depth(SeriesComputation.Compute(inputs, Definitions, Interval), Start.AddHours(3));

        // Снимок за 02:30 уточнён: та же версия факта, пятьдесят конкурентов. Узнай система
        // об уточнении до 03:00, точка взяла бы его вместо исходной версии; узнала она в
        // 05:00 — точка на 03:00 считается по исходной версии, а не остаётся без снимка.
        var timely = Depth(SeriesComputation.Compute(Refined(inputs, Start.AddMinutes(165)), Definitions, Interval), Start.AddHours(3));
        var late = Depth(SeriesComputation.Compute(Refined(inputs, Start.AddHours(5)), Definitions, Interval), Start.AddHours(3));

        timely.ShouldBeGreaterThan(without);
        late.ShouldBe(without);
    }

    [Fact]
    public void ComputeEveryKindForAnAdmittedWindow()
    {
        SeriesComputed computed = SeriesComputation.Compute(Inputs(), Definitions, Interval);

        IReadOnlyList<SeriesKind> kinds =
        [
            .. computed.Points
                .Where(static point => point.Window.To == Start.AddHours(3))
                .Select(static point => point.Definition.Kind)
                .Distinct()
                .Order(),
        ];

        kinds.ShouldBe(
        [
            SeriesKind.ObservedTurnover,
            SeriesKind.RelistPressure,
            SeriesKind.FilledDisappearanceShare,
            SeriesKind.BestPriceHold,
            SeriesKind.CompetitorDepth,
        ]);
    }

    private static SeriesInputs Crowded(SeriesInputs inputs, DateTimeOffset knownAt) =>
        inputs with
        {
            Features =
            [
                .. inputs.Features,
                new KnownBookFeatures(
                    inputs.Features[0].Features with
                    {
                        SellOrdersWithin = [50, 50],
                        ObservedAt = Start.AddMinutes(150),
                        Observation = ObservationId.From("late-0230"),
                    },
                    "features/late-0230",
                    knownAt),
            ],
        };

    private static SeriesInputs Refined(SeriesInputs inputs, DateTimeOffset knownAt)
    {
        KnownBookFeatures original = inputs.Features.Single(static known => known.Features.ObservedAt == Start.AddMinutes(150));

        return inputs with
        {
            Features =
            [
                .. inputs.Features,
                new KnownBookFeatures(
                    original.Features with { SellOrdersWithin = [50, 50], Observation = ObservationId.From("refined-0230") },
                    original.FactKey,
                    knownAt),
            ],
        };
    }

    private static string FeatureKey(DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"features/34/60003760/{at:yyyyMMddTHHmmssZ}");

    private static double Depth(SeriesComputed computed, DateTimeOffset end) =>
        computed.Points.Single(point =>
            point.Definition.Kind == SeriesKind.CompetitorDepth && point.Side == SeriesSide.Sell && point.Window.To == end).Value;

    private static SeriesWindowVerdict Verdict(SeriesComputed computed, SeriesKind kind, DateTimeOffset end) =>
        computed.Windows.Single(window => window.Definition.Kind == kind && window.Window.Range.To == end);

    private static IReadOnlyList<string> Shape(SeriesComputed computed) =>
    [
        .. computed.Windows.Select(static window => string.Create(
            CultureInfo.InvariantCulture,
            $"{window.FactKey}|{window.Window.Admission}|{window.Window.Coverage}|{window.Window.PartialObservations}")),
        .. computed.Points.Select(static point => string.Create(
            CultureInfo.InvariantCulture,
            $"{point.FactKey}|{BitConverter.DoubleToInt64Bits(point.Value)}|{point.Incomplete}")),
    ];

    /// <summary>
    /// Снимки каждые полчаса с 00:00 до 06:00; первый — базовая линия. По паре 34 в
    /// Jita 4-4 идут исполнения, перестановки и одно исчезновение.
    /// </summary>
    private static SeriesInputs Inputs(int offsetMinutes = 0, TimeSpan? spacing = null)
    {
        var coverage = new List<CoverageEntry>();
        var features = new List<KnownBookFeatures>();
        var events = new List<OrderEvent>();

        for (var slot = 0; slot <= 12; slot++)
        {
            DateTimeOffset at = Start.AddMinutes(offsetMinutes) + ((spacing ?? Step) * slot);
            var observation = ObservationId.From($"obs-{slot:00}");

            coverage.Add(CoverageEntries.Success(
                observation,
                Forge,
                TimeRange.Between(at.AddSeconds(-20), at),
                pages: 1,
                orderCount: 10,
                source: "everef-orders",
                observationStep: Step,
                knownAt: at));

            features.Add(new KnownBookFeatures(new BookFeatures(
                34,
                60003760,
                IskPrice.FromIsk(90m + (slot % 3)),
                IskPrice.FromIsk(100m - (slot % 2)),
                BuyOrders: 5,
                SellOrders: 6,
                BuyDepth: [10, 30],
                SellDepth: [5, 55],
                BuyOrdersWithin: [1 + (slot % 2), 4],
                SellOrdersWithin: [2 + (slot % 3), 6],
                at,
                observation,
                Incomplete: false), FeatureKey(at), at));

            if (slot == 0)
            {
                continue;
            }

            events.Add(Event(OrderEventKind.ObservedFill, order: slot, isBuy: slot % 2 == 0, at) with { FilledVolume = slot * 3 });
            events.Add(Event(OrderEventKind.Repriced, order: 100 + slot, isBuy: false, at));

            if (slot == 5)
            {
                events.Add(Event(OrderEventKind.Disappeared, order: slot, isBuy: true, at));
            }
        }

        return new SeriesInputs(
            Forge,
            events,
            features,
            FeatureOptions.DefaultThresholds,
            coverage,
            new HashSet<ObservationId> { ObservationId.From("obs-00") },
            [],
            UpstreamCatalog.Empty);
    }

    private static OrderEvent Event(OrderEventKind kind, long order, bool isBuy, DateTimeOffset at) =>
        new(
            kind,
            order,
            TypeId: 34,
            LocationId: 60003760,
            isBuy,
            IskPrice.FromIsk(100m),
            IskPrice.FromIsk(100m),
            VolumeRemain: 100,
            FilledVolume: 0,
            EventTime.At(at),
            ObservedAt: at,
            IssuedUnix: Start.ToUnixTimeSeconds(),
            DurationDays: 30,
            IsNpc: false,
            UndersampledStep: false);
}
