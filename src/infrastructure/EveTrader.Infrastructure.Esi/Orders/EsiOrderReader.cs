using System.Text.Json;
using EveTrader.Domain.Book;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Потоковый разбор страницы ордеров прямо в массив структур.
///
/// Не <c>JsonSerializer</c> в коллекцию: тот дал бы миллион объектов на наблюдение, то
/// есть паузы сборщика вместо работы. <see cref="Utf8JsonReader" /> читает байты и
/// заполняет уже выделенный массив — на ордер не аллоцируется ничего.
/// </summary>
public static class EsiOrderReader
{
    /// <summary>
    /// Читает ордера в <paramref name="destination" />, начиная с <paramref name="offset" />.
    /// Возвращает, сколько записал. Массив растёт вызывающим — он же его и переиспользует
    /// между наблюдениями.
    /// </summary>
    public static int Read(ReadOnlySpan<byte> json, OrderSnapshot[] destination, int offset)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var reader = new Utf8JsonReader(json);
        var written = offset;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                continue;
            }

            OrderSnapshot order = ReadOne(ref reader);

            if (written == destination.Length)
            {
                throw new InvalidOperationException(
                    $"Буфер на {destination.Length} ордеров переполнен: источник прислал больше, чем объявил");
            }

            destination[written++] = order;
        }

        return written - offset;
    }

    /// <summary>Один ордер. Поля читаются по именам: порядок в ответе не гарантирован.</summary>
    public static OrderSnapshot ReadOne(ref Utf8JsonReader reader)
    {
        long orderId = 0;
        var typeId = 0;
        long locationId = 0;
        var isBuy = false;
        var price = 0m;
        long remain = 0;
        long total = 0;
        short duration = 0;
        long issued = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals(Names.OrderId))
            {
                _ = reader.Read();
                orderId = reader.GetInt64();
            }
            else if (reader.ValueTextEquals(Names.TypeId))
            {
                _ = reader.Read();
                typeId = reader.GetInt32();
            }
            else if (reader.ValueTextEquals(Names.LocationId))
            {
                _ = reader.Read();
                locationId = reader.GetInt64();
            }
            else if (reader.ValueTextEquals(Names.IsBuyOrder))
            {
                _ = reader.Read();
                isBuy = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals(Names.Price))
            {
                _ = reader.Read();
                price = reader.GetDecimal();
            }
            else if (reader.ValueTextEquals(Names.VolumeRemain))
            {
                _ = reader.Read();
                remain = reader.GetInt64();
            }
            else if (reader.ValueTextEquals(Names.VolumeTotal))
            {
                _ = reader.Read();
                total = reader.GetInt64();
            }
            else if (reader.ValueTextEquals(Names.Duration))
            {
                _ = reader.Read();
                duration = (short)reader.GetInt32();
            }
            else if (reader.ValueTextEquals(Names.Issued))
            {
                _ = reader.Read();
                issued = reader.GetDateTimeOffset().ToUnixTimeSeconds();
            }
            else
            {
                _ = reader.Read();
                reader.Skip();
            }
        }

        return new OrderSnapshot(
            orderId, typeId, locationId, isBuy, IskPrice.FromIsk(price), remain, total, duration, issued);
    }

    /// <summary>Имена полей в байтах: сравнение без выделения строки на каждое поле.</summary>
    private static class Names
    {
        public static ReadOnlySpan<byte> OrderId => "order_id"u8;

        public static ReadOnlySpan<byte> TypeId => "type_id"u8;

        public static ReadOnlySpan<byte> LocationId => "location_id"u8;

        public static ReadOnlySpan<byte> IsBuyOrder => "is_buy_order"u8;

        public static ReadOnlySpan<byte> Price => "price"u8;

        public static ReadOnlySpan<byte> VolumeRemain => "volume_remain"u8;

        public static ReadOnlySpan<byte> VolumeTotal => "volume_total"u8;

        public static ReadOnlySpan<byte> Duration => "duration"u8;

        public static ReadOnlySpan<byte> Issued => "issued"u8;
    }
}
