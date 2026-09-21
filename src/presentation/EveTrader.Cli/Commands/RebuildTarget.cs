namespace EveTrader.Cli.Commands;

/// <summary>
/// Куда писать перестроенное.
///
/// Отдельное озеро, а не то же самое, и это не предосторожность: идемпотентность
/// отбросила бы перестроенное по совпадению идентификаторов наблюдений, и сверять было
/// бы нечего.
/// </summary>
internal static class RebuildTarget
{
    public static string RootFor(CommandLine options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Scratch ?? (options.Lake.TrimEnd(Path.DirectorySeparatorChar) + "-rebuild");
    }
}
