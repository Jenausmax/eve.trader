namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Метрики наблюдения рынка.
///
/// Состав выбран по принципу «по этому числу принимается решение», а не «пусть будет
/// на дашборде»:
/// <list type="bullet">
/// <item>остаток бюджета ошибок — продолжать ли сбор;</item>
/// <item>доля изменившихся ордеров — сходится ли бюджет объёма;</item>
/// <item>длительность наблюдения региона — влезает ли охват в пятиминутный шаг;</item>
/// <item>число пропусков источника — верна ли длина окна подтверждения исчезновения;</item>
/// <item>доля ответов «не изменилось» — не тратится ли трафик впустую.</item>
/// </list>
/// </summary>
public interface IObservationDiagnostics : IDiagnosticSource
{
    /// <summary>Запросов к источнику, включая условные.</summary>
    ITelemetryCounter SourceRequests { get; }

    /// <summary>Принято байт тела. Ответ «не изменилось» тела не несёт и сюда не попадает.</summary>
    ITelemetryCounter SourceBytes { get; }

    /// <summary>Ответов «не изменилось».</summary>
    ITelemetryCounter NotModifiedResponses { get; }

    /// <summary>Ордеров, пропавших и вернувшихся — дефектов источника.</summary>
    ITelemetryCounter SourceGaps { get; }

    /// <summary>Длительность наблюдения региона целиком, со всеми страницами.</summary>
    ITelemetryHistogram RegionObservationDuration { get; }

    /// <summary>Доля ордеров, по которым наблюдение дало событие.</summary>
    ITelemetryHistogram ChangedOrderFraction { get; }

    /// <summary>Остаток бюджета ошибок по последнему ответу источника.</summary>
    ITelemetryGauge ErrorBudgetRemaining { get; }
}
