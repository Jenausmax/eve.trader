using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Unit.Book;

/// <summary>Сборка наблюдений для тестов разметки.</summary>
internal static class Observations
{
    public static RegionId TheForge { get; } = RegionId.From(10000002);

    public static DateTimeOffset At(int minutes) =>
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    /// <summary>Наблюдение с пятиминутным шагом — полное по перестановкам.</summary>
    public static ObservationMeta Meta(
        int minute,
        bool complete = true,
        bool baseline = false,
        int stepMinutes = 5) =>
        new(TheForge,
            ObservationId.From($"obs-{minute}"),
            TimeRange.Between(At(minute), At(minute).AddSeconds(20)),
            complete,
            TimeSpan.FromMinutes(stepMinutes),
            baseline);

    public static OrderSnapshot Order(
        long id,
        decimal price,
        long remain = 100,
        long total = 100,
        int issuedMinute = 0,
        short duration = 90,
        bool isBuy = false,
        int typeId = 34,
        long locationId = 60003760) =>
        new(id, typeId, locationId, isBuy, IskPrice.FromIsk(price), remain, total, duration,
            At(issuedMinute).ToUnixTimeSeconds());

    /// <summary>Ордер NPC: идентификатор ниже порога, год длительности, полный остаток, продажа.</summary>
    public static OrderSnapshot NpcOrder(long id, decimal price, int issuedMinute = 0) =>
        new(id, 34, 60003760, false, IskPrice.FromIsk(price), 1000, 1000, 365,
            At(issuedMinute).ToUnixTimeSeconds());
}
