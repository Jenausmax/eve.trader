using EveTrader.Application.Diagnostics;
using EveTrader.Application.Live;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Обвязка опроса региона: всё, что нужно проходу по страницам и не меняется от
/// наблюдения к наблюдению.
///
/// Собрана в один тип не ради краткости сигнатуры: сам проход живёт в
/// <see cref="EsiRegionPolling" /> отдельным типом, потому что приватные методы в
/// продакшн-коде запрещены, а восемь параметров у статического хелпера читаются хуже,
/// чем один контекст.
/// </summary>
/// <param name="Client">Клиент ESI.</param>
/// <param name="Options">Настройки источника.</param>
/// <param name="Budget">Общий на процесс бюджет ошибок.</param>
/// <param name="Clock">Часы.</param>
/// <param name="RawPages">Окно сырых страниц.</param>
/// <param name="Diagnostics">Метрики наблюдения.</param>
/// <param name="Buffer">Переиспользуемый буфер ордеров.</param>
/// <param name="Logger">Журнал.</param>
internal sealed record EsiPollContext(
    HttpClient Client,
    EsiOptions Options,
    ErrorBudget Budget,
    TimeProvider Clock,
    IRawPageArchive RawPages,
    IObservationDiagnostics Diagnostics,
    OrderBuffer Buffer,
    ILogger Logger);
