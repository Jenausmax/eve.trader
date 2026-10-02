using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Набор параметров правила станционной торговли: ставки, окно и пороги.
///
/// Имя набора не задаётся руками целиком, а выводится из значений: метка оператора плюс
/// отпечаток параметров. Причина — сценарий «порог изменён, прогон повторён»: с именем,
/// которое надо не забыть поменять, его однажды не поменяют, и два разных правила
/// съедутся в одну оценку качества под одним именем. С выведенным отпечатком это
/// невозможно.
/// </summary>
public sealed record StationTradingParameters
{
    private StationTradingParameters(
        string label,
        FeeSchedule fees,
        TimeSpan window,
        decimal minNetMarginRate,
        long minObservedTurnover,
        int competitorBandBasisPoints,
        int maxCompetitors,
        int maxRelistPressure,
        int expectedBuyRelists,
        int expectedSellRelists)
    {
        Label = label;
        Fees = fees;
        Window = window;
        MinNetMarginRate = minNetMarginRate;
        MinObservedTurnover = minObservedTurnover;
        CompetitorBandBasisPoints = competitorBandBasisPoints;
        MaxCompetitors = maxCompetitors;
        MaxRelistPressure = maxRelistPressure;
        ExpectedBuyRelists = expectedBuyRelists;
        ExpectedSellRelists = expectedSellRelists;
        Name = ParameterSetName.From($"{label}-{Digest(this)}");
    }

    /// <summary>Метка оператора: читаемая часть имени.</summary>
    public string Label { get; }

    /// <summary>Ставки удержаний. Умолчания нет — см. <see cref="FeeSchedule" />.</summary>
    public FeeSchedule Fees { get; }

    /// <summary>Окно рядов признаков, на которых правило принимает решение.</summary>
    public TimeSpan Window { get; }

    /// <summary>Минимальная маржа после удержаний как доля от затрат на покупку.</summary>
    public decimal MinNetMarginRate { get; }

    /// <summary>Минимальный наблюдённый оборот за окно, в единицах товара.</summary>
    public long MinObservedTurnover { get; }

    /// <summary>Полоса вокруг лучшей цены, внутри которой ордер считается конкурентом.</summary>
    public int CompetitorBandBasisPoints { get; }

    /// <summary>Сколько конкурентов в полосе ещё терпимо.</summary>
    public int MaxCompetitors { get; }

    /// <summary>Сколько перестановок цены за окно ещё терпимо.</summary>
    public int MaxRelistPressure { get; }

    /// <summary>Сколько перестановок закладывается в маржу на стороне покупки.</summary>
    public int ExpectedBuyRelists { get; }

    /// <summary>Сколько перестановок закладывается в маржу на стороне продажи.</summary>
    public int ExpectedSellRelists { get; }

    /// <summary>Имя набора: метка плюс отпечаток значений.</summary>
    public ParameterSetName Name { get; }

    public static StationTradingParameters Of(
        string label,
        FeeSchedule fees,
        TimeSpan window,
        decimal minNetMarginRate,
        long minObservedTurnover,
        int competitorBandBasisPoints,
        int maxCompetitors,
        int maxRelistPressure,
        int expectedBuyRelists,
        int expectedSellRelists)
    {
        ArgumentNullException.ThrowIfNull(fees);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(minNetMarginRate);
        ArgumentOutOfRangeException.ThrowIfNegative(minObservedTurnover);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(competitorBandBasisPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCompetitors);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRelistPressure);

        return new StationTradingParameters(
            label, fees, window, minNetMarginRate, minObservedTurnover,
            competitorBandBasisPoints, maxCompetitors, maxRelistPressure,
            FeeSchedule.Relists(expectedBuyRelists), FeeSchedule.Relists(expectedSellRelists));
    }

    /// <summary>
    /// Каноническое представление значений — то, из чего считается отпечаток.
    ///
    /// Метка в него не входит: два набора с одинаковыми порогами под разными метками
    /// должны быть видны как одинаковые по существу.
    /// </summary>
    public static string Canonical(StationTradingParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"broker={parameters.Fees.BrokerFeeRate};tax={parameters.Fees.SalesTaxRate};relist={parameters.Fees.RelistFeeRate};" +
            $"window={parameters.Window.Ticks};margin={parameters.MinNetMarginRate};turnover={parameters.MinObservedTurnover};" +
            $"band={parameters.CompetitorBandBasisPoints};competitors={parameters.MaxCompetitors};" +
            $"pressure={parameters.MaxRelistPressure};buyRelists={parameters.ExpectedBuyRelists};" +
            $"sellRelists={parameters.ExpectedSellRelists}");
    }

    /// <summary>
    /// Отпечаток набора. SHA-256, а не <see cref="object.GetHashCode" />: встроенный хеш
    /// строки рандомизирован на процесс, и имя набора менялось бы при каждом перезапуске,
    /// делая сравнение прогонов бессмысленным.
    /// </summary>
    public static string Digest(StationTradingParameters parameters) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(parameters))))[..8];
}
