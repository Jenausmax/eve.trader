using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Затравка пригодности регионов.
///
/// Пригодность определяется наблюдением, а наблюдаются только пригодные — и первый
/// цикл процесса без затравки не наблюдает ничего. Круг разрывается здесь: названные
/// оператором регионы объявляются пригодными при старте.
///
/// Это именно затравка, а не обход правила. Регион, объявленный источником, но
/// систематически пустой, всё равно виден как <see cref="RegionViability.Barren" /> —
/// но только если его не называли руками. Названный руками регион остаётся в охвате,
/// пока его оттуда не уберут тем же способом.
/// </summary>
internal static class ViabilitySeed
{
    public static RegionViability For(IReadOnlyList<RegionId> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var viability = new RegionViability();

        foreach (RegionId region in regions)
        {
            viability.Observed(region, 1);
        }

        return viability;
    }
}
