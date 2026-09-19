using System.Globalization;
using System.Text;

namespace EveTrader.Application.Integration.History;

/// <summary>Сборка CSV в формате источника.</summary>
internal static class Csv
{
    public const string Header = "average,date,highest,lowest,order_count,volume,http_last_modified,region_id,type_id";

    public static string Of(params (DateOnly Date, int Region, int Type, long Volume, DateTimeOffset KnownAt)[] rows)
    {
        StringBuilder text = new StringBuilder(Header).AppendLine();

        foreach ((DateOnly date, var region, var type, var volume, DateTimeOffset knownAt) in rows)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture,
                $"10.5,{date:yyyy-MM-dd},12,9,7,{volume},{knownAt:yyyy-MM-ddTHH:mm:ssZ},{region},{type}");
        }

        return text.ToString();
    }
}
