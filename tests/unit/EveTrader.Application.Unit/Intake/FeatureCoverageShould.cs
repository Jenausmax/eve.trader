using EveTrader.Application.Book;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Application.Workers;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace EveTrader.Application.Unit.Intake;

/// <summary>
/// Сценарий <c>market-observation/book-features</c> §«Охват признаков задаётся отдельно от
/// охвата наблюдения» — «Охват не объявлен». Полный охват пар измерен в 770 ГиБ против
/// ~62 ГиБ сырья, и материализовать его молча, умолчанием, больше нельзя.
/// </summary>
public sealed class FeatureCoverageShould
{
    [Fact]
    public async Task RejectARunWithoutDeclaredCoverage()
    {
        IFactWriter writer = Substitute.For<IFactWriter>();
        ObservationIntake intake = Intake(writer);
        IObservationSource source = Substitute.For<IObservationSource>();

        InvalidOperationException rejected = await Should.ThrowAsync<InvalidOperationException>(
            () => intake.RunAsync(
                source,
                DiffOptions.Default,
                FeatureOptions.Undeclared,
                StaticDataVersion.From("sde-test"),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // Отказ называет причину: охват обязателен.
        rejected.Message.ShouldContain("Охват признаков не объявлен");

        // И отказ случился до первого наблюдения — ничего не записано.
        _ = source.DidNotReceive().ObserveAsync(Arg.Any<CancellationToken>());
        _ = await writer.DidNotReceiveWithAnyArgs()
            .WriteAllAsync(default!, default!, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
    }

    [Fact]
    public void LeaveTheConfiguredCoverageUndeclaredByDefault() =>
        // Настройка наблюдения больше не подставляет «все пары»: не объявили — откажут.
        new ObservationOptions().Features.IsDeclared.ShouldBeFalse();

    [Fact]
    public void DeriveTheCoverageFromThePairsTheRuleReads()
    {
        FeatureCoverage coverage = StationTradingScope.CoverageFor([StationTradingScope.Jita44]);

        coverage.Includes(34, StationTradingScope.Jita44.StationId).ShouldBeTrue();
        coverage.Includes(34, 60008494).ShouldBeFalse();

        // Основание входит в объявление: охват без основания не объявляется.
        coverage.Basis.ShouldContain("Jita 4-4");
    }

    [Fact]
    public void RefuseACoverageWithoutABasis() =>
        Should.Throw<ArgumentException>(static () => FeatureCoverage.Locations(" ", [60003760L]));

    private static ObservationIntake Intake(IFactWriter writer) =>
        new(
            new ObservationDerivation(writer, new DailyCheckpointPolicy()),
            new ObservationDiagnostics(new MeterDiagnosticSource()),
            NullLogger<ObservationIntake>.Instance);
}
