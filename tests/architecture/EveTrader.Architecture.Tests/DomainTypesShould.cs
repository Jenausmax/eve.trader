using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace EveTrader.Architecture.Tests;

/// <summary>
/// Законы уровня типов. Дополняют проверку графа ссылок из
/// <see cref="LayerDependenciesShould" />: та ловит ссылку в csproj, эта — зависимость
/// типа, пролезшую через транзитивную ссылку или через nuget.
///
/// Запрет системных часов в домене здесь не проверяется: NetArchTest смотрит на
/// зависимости типов, а не на вызовы, и <c>DateTime</c> в домене легален, тогда как
/// <c>DateTime.UtcNow</c> — нет. Этот запрет держит BannedApiAnalyzers в
/// EveTrader.Domain.csproj.
/// </summary>
public sealed class DomainTypesShould
{
    private static readonly Assembly Domain = Assembly.Load("EveTrader.Domain");

    [Fact]
    public void NotDependOnIo()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void NotDependOnOtherLayers()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "EveTrader.Application",
                "EveTrader.Infrastructure",
                "EveTrader.Cli")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }
}
