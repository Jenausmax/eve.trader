using System.Text.Json.Serialization;

namespace EveTrader.Infrastructure.Esi.History;

/// <summary>Строка ответа ESI <c>/markets/{region_id}/history/</c>.</summary>
public sealed record EsiHistoryEntry(
    [property: JsonPropertyName("average")] decimal Average,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("highest")] decimal Highest,
    [property: JsonPropertyName("lowest")] decimal Lowest,
    [property: JsonPropertyName("order_count")] long OrderCount,
    [property: JsonPropertyName("volume")] long Volume);
