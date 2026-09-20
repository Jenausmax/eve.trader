namespace EveTrader.Application.Live;

/// <summary>
/// Одна страница ответа источника и момент её получения.
///
/// Момент на странице, а не на наблюдении: источник не даёт атомарного среза региона,
/// крупный регион — двести с лишним страниц, собираемых десятки секунд, и приписать им
/// одно время значило бы объявить точность, которой нет.
/// </summary>
/// <param name="Number">Номер страницы, с единицы.</param>
/// <param name="ReceivedAt">Когда страница получена.</param>
/// <param name="Orders">Сколько ордеров на странице.</param>
public readonly record struct PageFetch(int Number, DateTimeOffset ReceivedAt, int Orders);
