using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace EveTrader.Architecture.Tests;

/// <summary>
/// Граница вычислений в хранилище (<c>market-facts/bitemporal-store</c> §«Границы
/// вычислений в хранилище»). Порт агрегатов физически недоступен домену и коду, который
/// считает признаки: посчитать признак запросом нельзя не потому, что не принято, а
/// потому, что нечем.
///
/// Причина именно такая: признак, посчитанный в SQL для обучения и в C# в бою,
/// расходится, и расхождение выглядит на бэктесте как отличная модель, а в бою как
/// случайная.
/// </summary>
public sealed class ComputationBoundaryShould
{
    private const string ReportingNamespace = "EveTrader.Application.Reporting";

    private static readonly Assembly Domain = Assembly.Load("EveTrader.Domain");

    private static readonly Assembly Application = Assembly.Load("EveTrader.Application");

    private static readonly Assembly Facts = Assembly.Load("EveTrader.Infrastructure.Facts");

    [Fact]
    public void KeepTheAggregatePortOutOfTheDomain()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOn(ReportingNamespace)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void KeepTheAggregatePortOutOfTheFlatRowSide()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Application)
            .That()
            .ResideInNamespace("EveTrader.Application.Facts")
            .Should()
            .NotHaveDependencyOn(ReportingNamespace)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void KeepTheAggregatePortOutOfTheFlatRowReader()
    {
        // Реализация порта плоских строк не знает про агрегаты: иначе граница держалась
        // бы на дисциплине, а не на устройстве.
        NetArchTest.Rules.TestResult result = Types.InAssembly(Facts)
            .That()
            .HaveNameEndingWith("FactRowReader")
            .Should()
            .NotHaveDependencyOn(ReportingNamespace)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void LetTheAggregatePortBeImplementedByInfrastructure()
    {
        // Запрет односторонний: инфраструктура обязана реализовывать оба порта,
        // иначе отчёты оператору не из чего строить.
        IEnumerable<Type> reporting = Types.InAssembly(Facts)
            .That()
            .ImplementInterface(typeof(Application.Reporting.IOperationalReportReader))
            .GetTypes();

        reporting.ShouldNotBeEmpty();
    }
}
