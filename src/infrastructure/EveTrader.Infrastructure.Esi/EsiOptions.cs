namespace EveTrader.Infrastructure.Esi;

/// <summary>Настройки публичного ESI. Авторизации нет — доступ только read-only.</summary>
public sealed class EsiOptions
{
    public const string SectionName = "Esi";

    public Uri BaseAddress { get; init; } = new("https://esi.evetech.net/latest/");

    public string Datasource { get; init; } = "tranquility";

    /// <summary>
    /// Чем система представляется источнику. CCP требует от третьих сторон называть себя
    /// и оставлять контакт: безымянный клиент вправе получить отказ.
    /// </summary>
    public string UserAgent { get; init; } = "eve.trader/0.1 (+https://github.com/Jenausmax/eve.trader)";

    /// <summary>Сколько запросов к ESI держать одновременно.</summary>
    public int MaxParallelRequests { get; init; } = 4;
}
