using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Integration.Book;

/// <summary>Наблюдения для тестов записи.</summary>
internal static class Samples
{
    public static DateTimeOffset At(int minutes) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    public static ObservationMeta Meta(int minute, bool complete = true, bool baseline = false) =>
        new(BookLake.Region,
            ObservationId.From($"obs-{minute}"),
            TimeRange.Between(At(minute), At(minute).AddSeconds(20)),
            complete,
            TimeSpan.FromMinutes(5),
            baseline);

    public static OrderSnapshot Order(
        long id,
        decimal price,
        long remain = 100,
        int issuedMinute = 0,
        bool isBuy = false,
        int typeId = 34) =>
        new(id, typeId, 60003760, isBuy, IskPrice.FromIsk(price), remain, 100, 90,
            At(issuedMinute).ToUnixTimeSeconds());
}
