using System.Reflection;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Signals;

/// <summary>
/// Сценарий <c>market-signals/station-trading</c> §«Сигнал — рекомендация, а не
/// действие». Потолок продукта — чтение, анализ, рекомендации и алерты: автоматизация
/// игрового клиента нарушает EULA, и пишущих эндпоинтов маркета у ESI нет.
/// </summary>
public sealed class SignalIsRecommendationShould
{
    private static readonly string[] RecordMembers = ["Equals", "GetHashCode", "ToString", "Deconstruct", "<Clone>$"];

    [Theory]
    [InlineData(typeof(StationTradingVerdict))]
    [InlineData(typeof(SignalJustification))]
    public void DescribeTheOpportunityWithoutAnythingToExecute(Type signal)
    {
        // Только данные: свойства и то, что компилятор порождает для записи. Ни одного
        // метода, который что-то делал бы, — исполнять в сигнале нечего.
        IEnumerable<string> behaviour = signal
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName && !RecordMembers.Contains(method.Name))
            .Select(static method => method.Name);

        behaviour.ShouldBeEmpty();
    }

    [Fact]
    public void CarryItsJustification()
    {
        // Описание возможности — это цены, маржа и признаки с порогами, а не команда.
        PropertyInfo[] properties = typeof(StationTradingVerdict).GetProperties();

        properties.ShouldContain(static property => property.Name == nameof(StationTradingVerdict.Justification));
        properties.ShouldAllBe(static property => !property.CanWrite || property.SetMethod!.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
    }
}
