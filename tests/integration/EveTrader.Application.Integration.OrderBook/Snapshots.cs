using System.Globalization;
using System.Text;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Сборка CSV снимка стакана в формате источника — v3, с полным набором колонок.
/// </summary>
internal static class Snapshots
{
    public const string HeaderV3 =
        "duration,is_buy_order,issued,location_id,min_volume,order_id,price,range,system_id,type_id," +
        "volume_remain,volume_total,http_last_modified,station_id,region_id,constellation_id";

    /// <summary>Поколение формата без колонки времени получения — время берётся от файла.</summary>
    public const string HeaderWithoutKnownAt =
        "duration,is_buy_order,issued,location_id,min_volume,order_id,price,range,system_id,type_id," +
        "volume_remain,volume_total,region_id";

    public static DateTimeOffset At(int minutes) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    /// <summary>Один ордер снимка.</summary>
    public static Order Of(
        long orderId,
        int region,
        decimal price,
        long remain = 100,
        long total = 100,
        int issuedMinute = 0,
        short duration = 90,
        bool isBuy = false,
        int typeId = 34,
        long locationId = 60003760) =>
        new(orderId, region, price, remain, total, issuedMinute, duration, isBuy, typeId, locationId);

    public static string Csv(DateTimeOffset collectedAt, params Order[] orders) =>
        Csv(HeaderV3, collectedAt, orders);

    public static string Csv(string header, DateTimeOffset collectedAt, params Order[] orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var carriesKnownAt = header.Contains("http_last_modified", StringComparison.Ordinal);
        StringBuilder text = new StringBuilder(header).AppendLine();

        foreach (Order order in orders)
        {
            DateTimeOffset issued = At(order.IssuedMinute);
            var system = 30000142 + (order.Region - 10000002);

            _ = carriesKnownAt
                ? text.AppendLine(CultureInfo.InvariantCulture,
                    $"{order.Duration},{Flag(order.IsBuy)},{issued:yyyy-MM-ddTHH:mm:ssZ},{order.LocationId},1," +
                    $"{order.OrderId},{order.Price},region,{system},{order.TypeId}," +
                    $"{order.Remain},{order.Total},{collectedAt:yyyy-MM-ddTHH:mm:ssZ},{order.LocationId}," +
                    $"{order.Region},20000020")
                : text.AppendLine(CultureInfo.InvariantCulture,
                    $"{order.Duration},{Flag(order.IsBuy)},{issued:yyyy-MM-ddTHH:mm:ssZ},{order.LocationId},1," +
                    $"{order.OrderId},{order.Price},region,{system},{order.TypeId}," +
                    $"{order.Remain},{order.Total},{order.Region}");
        }

        return text.ToString();
    }

    public static string Flag(bool value) => value ? "true" : "false";
}

/// <summary>Ордер для сборки снимка.</summary>
internal sealed record Order(
    long OrderId,
    int Region,
    decimal Price,
    long Remain,
    long Total,
    int IssuedMinute,
    short Duration,
    bool IsBuy,
    int TypeId,
    long LocationId);
