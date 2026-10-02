using System.Globalization;
using EveTrader.Domain.Coverage;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Исход рассмотрения пары в момент решения: сигнал, «условия не выполнены» или «данных
/// не хватило».
///
/// Сигнал — рекомендация, а не действие. Здесь нет ни команды, ни ордера, ни чего-либо,
/// что можно исполнить в игре: только описание возможности и её обоснование. Потолок
/// продукта — чтение, анализ, рекомендации и алерты; автоматизация клиента нарушает EULA.
/// </summary>
/// <param name="Scope">Станция.</param>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="Decision">Момент решения.</param>
/// <param name="ParameterSet">Имя набора параметров, которым порождён исход.</param>
/// <param name="Outcome">Исход.</param>
/// <param name="Reasons">Почему сигнала нет; пусто у сигнала.</param>
/// <param name="Coverage">Состояние покрытия окна, если данных не хватило из-за пробела.</param>
/// <param name="Justification">Значения признаков и пороги; нет, когда данных не хватило.</param>
public sealed record StationTradingVerdict(
    StationTradingScope Scope,
    int TypeId,
    DateTimeOffset Decision,
    ParameterSetName ParameterSet,
    ConsiderationOutcome Outcome,
    IReadOnlyList<VerdictReason> Reasons,
    CoverageState? Coverage,
    SignalJustification? Justification)
{
    public bool IsSignal => Outcome is ConsiderationOutcome.Signal;

    /// <summary>
    /// Ключ факта. Имя набора входит в ключ: сигналы разных наборов не сталкиваются и не
    /// смешиваются, а пересмотр другим набором порождает новый факт рядом с прежним.
    /// </summary>
    public string FactKey =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"signal/{ParameterSet.Value}/{Scope.Region.Value}/{Scope.StationId}/{TypeId}/{Decision:yyyyMMddTHHmmssZ}");
}
