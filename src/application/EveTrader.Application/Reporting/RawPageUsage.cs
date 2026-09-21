namespace EveTrader.Application.Reporting;

/// <summary>Сколько сырых страниц и байт лежит в окне за интервал.</summary>
/// <param name="Pages">Страниц.</param>
/// <param name="Bytes">Байт.</param>
/// <param name="Earliest">Начало самого раннего часа, за который окно ещё что-то помнит.</param>
public readonly record struct RawPageUsage(int Pages, long Bytes, DateTimeOffset? Earliest);
